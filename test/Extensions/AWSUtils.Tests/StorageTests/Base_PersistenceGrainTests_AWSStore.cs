using Orleans.TestingHost;
using System.Diagnostics;
using System.Globalization;
using Tester;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;
using Xunit.Abstractions;

namespace AWSUtils.Tests.StorageTests
{
    public abstract class Base_PersistenceGrainTests_AWSStore : OrleansTestingBase
    {
        private readonly ITestOutputHelper output;
        protected TestCluster HostedCluster { get; private set; }
        private readonly double timingFactor;

        private const int LoopIterations_Grain = 1000;
        private const int BatchSize = 100;

        private const int MaxReadTime = 200;
        private const int MaxWriteTime = 2000;
        private readonly BaseTestClusterFixture fixture;

        public Base_PersistenceGrainTests_AWSStore(ITestOutputHelper output, BaseTestClusterFixture fixture)
        {
            if (!AWSTestConstants.IsDynamoDbAvailable)
                throw new SkipException("Unable to connect to DynamoDB simulator");

            this.output = output;
            this.fixture = fixture;
            HostedCluster = fixture.HostedCluster;
            timingFactor = TestUtils.CalibrateTimings();
        }

        protected async Task Grain_AWSStore_Delete()
        {
            var id = Guid.NewGuid();
            var grain = fixture.GrainFactory.GetGrain<IAWSStorageTestGrain>(id);

            await grain.DoWrite(1);

            await grain.DoDelete();

            var val = await grain.GetValue(); // Should this throw instead?
            Assert.Equal(0, val);  // "Value after Delete"

            await grain.DoWrite(2);

            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Delete + New Write"
        }

        protected async Task Grain_AWSStore_Read()
        {
            var id = Guid.NewGuid();
            var grain = fixture.GrainFactory.GetGrain<IAWSStorageTestGrain>(id);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"
        }

        protected async Task Grain_GuidKey_AWSStore_Read_Write()
        {
            var id = Guid.NewGuid();
            var grain = fixture.GrainFactory.GetGrain<IAWSStorageTestGrain>(id);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();

            Assert.Equal(2, val);  // "Value after Re-Read"
        }

        protected async Task Grain_LongKey_AWSStore_Read_Write()
        {
            long id = Random.Shared.Next();
            var grain = fixture.GrainFactory.GetGrain<IAWSStorageTestGrain_LongKey>(id);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();

            Assert.Equal(2, val);  // "Value after Re-Read"
        }

        protected async Task Grain_LongKeyExtended_AWSStore_Read_Write()
        {
            long id = Random.Shared.Next();
            var extKey = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);

            var
                grain = fixture.GrainFactory.GetGrain<IAWSStorageTestGrain_LongExtendedKey>(id, extKey, null);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();
            Assert.Equal(2, val);  // "Value after DoRead"

            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Re-Read"

            var extKeyValue = await grain.GetExtendedKeyValue();
            Assert.Equal(extKey, extKeyValue);  // "Extended Key"
        }

        protected async Task Grain_GuidKeyExtended_AWSStore_Read_Write()
        {
            var id = Guid.NewGuid();
            var extKey = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);

            var
                grain = fixture.GrainFactory.GetGrain<IAWSStorageTestGrain_GuidExtendedKey>(id, extKey, null);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();
            Assert.Equal(2, val);  // "Value after DoRead"

            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Re-Read"

            var extKeyValue = await grain.GetExtendedKeyValue();
            Assert.Equal(extKey, extKeyValue);  // "Extended Key"
        }

        protected async Task Grain_Generic_AWSStore_Read_Write()
        {
            long id = Random.Shared.Next();

            var grain = fixture.GrainFactory.GetGrain<IAWSStorageGenericGrain<int>>(id);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();

            Assert.Equal(2, val);  // "Value after Re-Read"
        }

        protected async Task Grain_Generic_AWSStore_DiffTypes()
        {
            long id1 = Random.Shared.Next();
            var id2 = id1;
            var id3 = id1;

            var grain1 = fixture.GrainFactory.GetGrain<IAWSStorageGenericGrain<int>>(id1);

            var grain2 = fixture.GrainFactory.GetGrain<IAWSStorageGenericGrain<string>>(id2);

            var grain3 = fixture.GrainFactory.GetGrain<IAWSStorageGenericGrain<double>>(id3);

            var val1 = await grain1.GetValue();
            Assert.Equal(0, val1);  // "Initial value - 1"

            var val2 = await grain2.GetValue();
            Assert.Null(val2);  // "Initial value - 2"

            var val3 = await grain3.GetValue();
            Assert.Equal(0.0, val3);  // "Initial value - 3"

            var expected1 = 1;
            await grain1.DoWrite(expected1);
            val1 = await grain1.GetValue();
            Assert.Equal(expected1, val1);  // "Value after Write#1 - 1"

            var expected2 = "Three";
            await grain2.DoWrite(expected2);
            val2 = await grain2.GetValue();
            Assert.Equal(expected2, val2);  // "Value after Write#1 - 2"

            var expected3 = 5.1;
            await grain3.DoWrite(expected3);
            val3 = await grain3.GetValue();
            Assert.Equal(expected3, val3);  // "Value after Write#1 - 3"

            val1 = await grain1.GetValue();
            Assert.Equal(expected1, val1);  // "Value before Write#2 - 1"
            expected1 = 2;
            await grain1.DoWrite(expected1);
            val1 = await grain1.GetValue();
            Assert.Equal(expected1, val1);  // "Value after Write#2 - 1"
            val1 = await grain1.DoRead();
            Assert.Equal(expected1, val1);  // "Value after Re-Read - 1"

            val2 = await grain2.GetValue();
            Assert.Equal(expected2, val2);  // "Value before Write#2 - 2"
            expected2 = "Four";
            await grain2.DoWrite(expected2);
            val2 = await grain2.GetValue();
            Assert.Equal(expected2, val2);  // "Value after Write#2 - 2"
            val2 = await grain2.DoRead();
            Assert.Equal(expected2, val2);  // "Value after Re-Read - 2"

            val3 = await grain3.GetValue();
            Assert.Equal(expected3, val3);  // "Value before Write#2 - 3"
            expected3 = 6.2;
            await grain3.DoWrite(expected3);
            val3 = await grain3.GetValue();
            Assert.Equal(expected3, val3);  // "Value after Write#2 - 3"
            val3 = await grain3.DoRead();
            Assert.Equal(expected3, val3);  // "Value after Re-Read - 3"
        }

        protected async Task Grain_AWSStore_SiloRestart()
        {
            var initialServiceId = HostedCluster.Options.ServiceId;
            var initialDeploymentId = HostedCluster.Options.ClusterId;
            var serviceId = await HostedCluster.Client.GetGrain<IServiceIdGrain>(Guid.Empty).GetServiceId();
            output.WriteLine("ClusterId={0} ServiceId={1}", HostedCluster.Options.ClusterId, serviceId);

            var id = Guid.NewGuid();
            var grain = fixture.GrainFactory.GetGrain<IAWSStorageTestGrain>(id);

            var val = await grain.GetValue();

            Assert.Equal(0, val);  // "Initial value"

            await grain.DoWrite(1);

            output.WriteLine("About to reset Silos");
            foreach (var silo in HostedCluster.GetActiveSilos().ToList())
            {
                await HostedCluster.RestartSiloAsync(silo);
            }
            await HostedCluster.StopClusterClientAsync();
            await HostedCluster.InitializeClientAsync();

            output.WriteLine("Silos restarted");

            serviceId = await HostedCluster.Client.GetGrain<IServiceIdGrain>(Guid.Empty).GetServiceId();
            output.WriteLine("ClusterId={0} ServiceId={1}", HostedCluster.Options.ClusterId, serviceId);
            Assert.Equal(initialServiceId, serviceId);  // "ServiceId same after restart."
            Assert.Equal(initialDeploymentId, HostedCluster.Options.ClusterId);  // "ClusterId same after restart."

            // Since the client was destroyed and restarted, the grain reference needs to be recreated.
            grain = fixture.GrainFactory.GetGrain<IAWSStorageTestGrain>(id);

            val = await grain.GetValue();
            Assert.Equal(1, val);  // "Value after Write-1"

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val);  // "Value after Write-2"

            val = await grain.DoRead();

            Assert.Equal(2, val);  // "Value after Re-Read"
        }

        protected void Persistence_Perf_Activate()
        {
            const string testName = "Persistence_Perf_Activate";
            var n = LoopIterations_Grain;
            var target = TimeSpan.FromMilliseconds(MaxReadTime * n);

            // Timings for Activate
            RunPerfTest(n, testName, target,
                grainNoState => grainNoState.PingAsync(),
                grainMemory => grainMemory.DoSomething(),
                grainMemoryStore => grainMemoryStore.GetValue(),
                grainAWSStore => grainAWSStore.GetValue());
        }

        protected void Persistence_Perf_Write()
        {
            const string testName = "Persistence_Perf_Write";
            var n = LoopIterations_Grain;
            var target = TimeSpan.FromMilliseconds(MaxWriteTime * n);

            // Timings for Write
            RunPerfTest(n, testName, target,
                grainNoState => grainNoState.EchoAsync(testName),
                grainMemory => grainMemory.DoWrite(n),
                grainMemoryStore => grainMemoryStore.DoWrite(n),
                grainAWSStore => grainAWSStore.DoWrite(n));
        }

        protected void Persistence_Perf_Write_Reread()
        {
            const string testName = "Persistence_Perf_Write_Read";
            var n = LoopIterations_Grain;
            var target = TimeSpan.FromMilliseconds(MaxWriteTime * n);

            // Timings for Write
            RunPerfTest(n, testName + "--Write", target,
                grainNoState => grainNoState.EchoAsync(testName),
                grainMemory => grainMemory.DoWrite(n),
                grainMemoryStore => grainMemoryStore.DoWrite(n),
                grainAWSStore => grainAWSStore.DoWrite(n));

            // Timings for Activate
            RunPerfTest(n, testName + "--ReRead", target,
                grainNoState => grainNoState.GetLastEchoAsync(),
                grainMemory => grainMemory.DoRead(),
                grainMemoryStore => grainMemoryStore.DoRead(),
                grainAWSStore => grainAWSStore.DoRead());
        }


        protected async Task Persistence_Silo_StorageProvider_AWS(string providerName)
        {
            var silos = HostedCluster.GetActiveSilos().ToList();
            foreach (var silo in silos)
            {
                var providers = await HostedCluster.Client.GetTestHooks(silo).GetStorageProviderNames();
                Assert.True(providers.Contains(providerName), $"No storage provider found: {providerName}");
            }
        }

        // ---------- Utility functions ----------

        protected void RunPerfTest(int n, string testName, TimeSpan target,
            Func<IEchoTaskGrain, Task> actionNoState,
            Func<IPersistenceTestGrain, Task> actionMemory,
            Func<IMemoryStorageTestGrain, Task> actionMemoryStore,
            Func<IAWSStorageTestGrain, Task> actionAWSTable)
        {
            var noStateGrains = new IEchoTaskGrain[n];
            var memoryGrains = new IPersistenceTestGrain[n];
            var awsStoreGrains = new IAWSStorageTestGrain[n];
            var memoryStoreGrains = new IMemoryStorageTestGrain[n];

            for (var i = 0; i < n; i++)
            {
                var id = Guid.NewGuid();
                noStateGrains[i] = fixture.GrainFactory.GetGrain<IEchoTaskGrain>(id);
                memoryGrains[i] = fixture.GrainFactory.GetGrain<IPersistenceTestGrain>(id);
                awsStoreGrains[i] = fixture.GrainFactory.GetGrain<IAWSStorageTestGrain>(id);
                memoryStoreGrains[i] = fixture.GrainFactory.GetGrain<IMemoryStorageTestGrain>(id);
            }

            TimeSpan baseline, elapsed;

            elapsed = baseline = TestUtils.TimeRun(n, TimeSpan.Zero, testName + " (No state)",
                () => RunIterations(testName, n, i => actionNoState(noStateGrains[i])));

            elapsed = TestUtils.TimeRun(n, baseline, testName + " (Local Memory Store)",
                () => RunIterations(testName, n, i => actionMemory(memoryGrains[i])));

            elapsed = TestUtils.TimeRun(n, baseline, testName + " (Dev Store Grain Store)",
                () => RunIterations(testName, n, i => actionMemoryStore(memoryStoreGrains[i])));

            elapsed = TestUtils.TimeRun(n, baseline, testName + " (AWS Table Store)",
                () => RunIterations(testName, n, i => actionAWSTable(awsStoreGrains[i])));

            if (elapsed > target.Multiply(timingFactor))
            {
                var msg = string.Format("{0}: Elapsed time {1} exceeds target time {2}", testName, elapsed, target);

                if (elapsed > target.Multiply(2.0 * timingFactor))
                {
                    Assert.True(false, msg);
                }
                else
                {
                    throw new SkipException(msg);
                }
            }
        }

        private void RunIterations(string testName, int n, Func<int, Task> action)
        {
            var promises = new List<Task>();
            var sw = Stopwatch.StartNew();
            // Fire off requests in batches
            for (var i = 0; i < n; i++)
            {
                var promise = action(i);
                promises.Add(promise);
                if ((i % BatchSize) == 0 && i > 0)
                {
                    Task.WaitAll(promises.ToArray());
                    promises.Clear();
                    //output.WriteLine("{0} has done {1} iterations  in {2} at {3} RPS",
                    //                  testName, i, sw.Elapsed, i / sw.Elapsed.TotalSeconds);
                }
            }
            Task.WaitAll(promises.ToArray());
            sw.Stop();
            output.WriteLine("{0} completed. Did {1} iterations in {2} at {3} RPS",
                              testName, n, sw.Elapsed, n / sw.Elapsed.TotalSeconds);
        }
    }
}
