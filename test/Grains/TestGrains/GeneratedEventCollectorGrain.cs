using Microsoft.Extensions.Logging;
using Orleans.Providers.Streams.Generator;
using Orleans.Streams;
using TestGrainInterfaces;
using UnitTests.Grains;

namespace TestGrains
{
    [RegexImplicitStreamSubscription("THIS.WONT.MATCH.ONLY.FOR.TESTING.SERIALIZATION")]
    [ImplicitStreamSubscription(StreamNamespace)]
    public class GeneratedEventCollectorGrain : Grain, IGeneratedEventCollectorGrain
    {
        public const string StreamNamespace = "Generated";

        private ILogger logger;
        private IAsyncStream<GeneratedEvent> stream;
        private int accumulated;

        public GeneratedEventCollectorGrain(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger($"{GetType().Name}-{IdentityString}");
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("OnActivateAsync");

            var streamProvider = this.GetStreamProvider(GeneratedStreamTestConstants.StreamProviderName);
            stream = streamProvider.GetStream<GeneratedEvent>(StreamNamespace, this.GetPrimaryKey());

            var handles = await stream.GetAllSubscriptionHandles();
            if (handles.Count == 0)
            {
                await stream.SubscribeAsync(OnNextAsync);
            }
            else
            {
                foreach (var handle in handles)
                {
                    await handle.ResumeAsync(OnNextAsync);
                }
            }
        }

        public Task OnNextAsync(IList<SequentialItem<GeneratedEvent>> items)
        {
            accumulated += items.Count;
            logger.LogInformation("Received {Count} generated event. Accumulated {Accumulated} events so far.", items.Count, accumulated);
            if (items.Last().Item.EventType == GeneratedEvent.GeneratedEventType.Fill)
            {
                return Task.CompletedTask;
            }
            var reporter = GrainFactory.GetGrain<IGeneratedEventReporterGrain>(GeneratedStreamTestConstants.ReporterId);
            return reporter.ReportResult(this.GetPrimaryKey(), GeneratedStreamTestConstants.StreamProviderName, StreamNamespace, accumulated);
        }
    }
}
