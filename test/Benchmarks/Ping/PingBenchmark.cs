using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkGrainInterfaces.Ping;
using BenchmarkGrains.Ping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;

namespace Benchmarks.Ping
{
    [MemoryDiagnoser]
    public class PingBenchmark : IDisposable
    {
        private readonly ConsoleCancelEventHandler _onCancelEvent;
        private readonly List<IHost> hosts = new List<IHost>();
        private readonly IPingGrain grain;
        private readonly IClusterClient client;
        private readonly IHost clientHost;

        public PingBenchmark() : this(1, true) { }

        public PingBenchmark(int numSilos, bool startClient, bool grainsOnSecondariesOnly = false)
        {
            for (var i = 0; i < numSilos; ++i)
            {
                var primary = i == 0 ? null : new IPEndPoint(IPAddress.Loopback, 11111);
                var hostBuilder = new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder.UseLocalhostClustering(
                        siloPort: 11111 + i,
                        gatewayPort: 30000 + i,
                        primarySiloEndpoint: primary);

                    if (i == 0 && grainsOnSecondariesOnly)
                    {
                        siloBuilder.Configure<GrainTypeOptions>(options => options.Classes.Remove(typeof(PingGrain)));
                    }
                })
                    //.ConfigureLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Debug).AddFilter("Orleans.Runtime.Placement.PlacementService", LogLevel.Information))
                    ;

                var host = hostBuilder.Build();

                host.StartAsync().GetAwaiter().GetResult();
                hosts.Add(host);
            }

            if (grainsOnSecondariesOnly) Thread.Sleep(4000);

            if (startClient)
            {
                var hostBuilder = new HostBuilder().UseOrleansClient((ctx, clientBuilder) =>
                {
                    if (numSilos == 1)
                    {
                        clientBuilder.UseLocalhostClustering();
                    }
                    else
                    {
                        var gateways = Enumerable.Range(30000, numSilos).Select(i => new IPEndPoint(IPAddress.Loopback, i)).ToArray();
                        clientBuilder.UseStaticClustering(gateways);
                    }
                });

                //hostBuilder.ConfigureLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Debug));
                clientHost = hostBuilder.Build();
                clientHost.StartAsync().GetAwaiter().GetResult();

                client = clientHost.Services.GetRequiredService<IClusterClient>();
                var grainFactory = client;

                grain = grainFactory.GetGrain<IPingGrain>(Guid.NewGuid().GetHashCode());
                grain.Run().AsTask().GetAwaiter().GetResult();
            }

            _onCancelEvent = CancelPressed;
            Console.CancelKeyPress += _onCancelEvent;
        }

        private void CancelPressed(object sender, ConsoleCancelEventArgs e)
        {
            Environment.Exit(0);
        }

        [Benchmark]
        public ValueTask Ping() => grain.Run();

        public async Task PingForever()
        {
            while (true)
            {
                await grain.Run();
            }
        }

        public Task PingConcurrentForever() => Run(
            runs: int.MaxValue,
            grainFactory: client,
            blocksPerWorker: 10);

        public Task PingConcurrent() => Run(
            runs: 3,
            grainFactory: client,
            blocksPerWorker: 10);

        public Task PingConcurrentHostedClient(int blocksPerWorker = 30) => Run(
            runs: 3,
            grainFactory: (IGrainFactory)hosts[0].Services.GetService(typeof(IGrainFactory)),
            blocksPerWorker: blocksPerWorker);

        private async Task Run(int runs, IGrainFactory grainFactory, int blocksPerWorker)
        {
            var loadGenerator = new ConcurrentLoadGenerator<IPingGrain>(
                maxConcurrency: 250,
                blocksPerWorker: blocksPerWorker,
                requestsPerBlock: 500,
                issueRequest: g => g.Run(),
                getStateForWorker: workerId => grainFactory.GetGrain<IPingGrain>(workerId));
            await loadGenerator.Warmup();
            while (runs-- > 0) await loadGenerator.Run();
        }

        public async Task PingPongForever()
        {
            var other = client.GetGrain<IPingGrain>(Guid.NewGuid().GetHashCode());
            while (true)
            {
                await grain.PingPongInterleave(other, 100);
            }
        }

        public async Task Shutdown()
        {
            if (clientHost is { } client)
            {
                await client.StopAsync();
                if (client is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    client.Dispose();
                }
            }

            hosts.Reverse();
            foreach (var host in hosts)
            {
                await host.StopAsync();
                if (host is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    host.Dispose();
                }
            }
        }

        [GlobalCleanup]
        public void Dispose()
        {
            (client as IDisposable)?.Dispose();
            hosts.ForEach(h => h.Dispose());

            Console.CancelKeyPress -= _onCancelEvent;
        }
    }
}
