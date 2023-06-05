using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Streams;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    public class MultipleSubscriptionConsumerGrain : Grain, IMultipleSubscriptionConsumerGrain
    {
        private readonly Dictionary<StreamSubscriptionHandle<int>, Tuple<Counter,Counter>> consumedMessageCounts;
        private ILogger logger;
        private int consumerCount = 0;

        public MultipleSubscriptionConsumerGrain(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger($"{GetType().Name}-{IdentityString}");
            consumedMessageCounts = new Dictionary<StreamSubscriptionHandle<int>, Tuple<Counter, Counter>>();
        }

        private class Counter
        {
            public int Value { get; private set; }

            public void Increment() => Value++;

            public void Clear() => Value = 0;
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("OnActivateAsync");
            return Task.CompletedTask;
        }

        public async Task<StreamSubscriptionHandle<int>> BecomeConsumer(Guid streamId, string streamNamespace, string providerToUse)
        {
            logger.LogInformation("BecomeConsumer");

            // new counter for this subscription
            var count = new Counter();
            var error = new Counter();

            // get stream
            var streamProvider = this.GetStreamProvider(providerToUse);
            var stream = streamProvider.GetStream<int>(streamNamespace, streamId);

            var countCapture = consumerCount;
            consumerCount++;
            // subscribe
            var handle = await stream.SubscribeAsync(
                (items) => OnNext(items, countCapture, count),
                e => OnError(e, countCapture, error));

            // track counter
            consumedMessageCounts.Add(handle, Tuple.Create(count,error));

            // return handle
            return handle;
        }

        public async Task<StreamSubscriptionHandle<int>> Resume(StreamSubscriptionHandle<int> handle)
        {
            logger.LogInformation("Resume");
            if(handle == null)
                throw new ArgumentNullException("handle");

            // new counter for this subscription
            Tuple<Counter,Counter> counters;
            if (!consumedMessageCounts.TryGetValue(handle, out counters))
            {
                counters = Tuple.Create(new Counter(), new Counter());
            }

            var countCapture = consumerCount;
            consumerCount++;
            // subscribe
            var newhandle = await handle.ResumeAsync(
                (items) => OnNext(items, countCapture, counters.Item1),
                e => OnError(e, countCapture, counters.Item2));

            // track counter
            consumedMessageCounts[newhandle] = counters;

            // return handle
            return newhandle;

        }

        public async Task StopConsuming(StreamSubscriptionHandle<int> handle)
        {
            logger.LogInformation("StopConsuming");
            // unsubscribe
            await handle.UnsubscribeAsync();

            // stop tracking event count for stream
            consumedMessageCounts.Remove(handle);
        }

        public Task<IList<StreamSubscriptionHandle<int>>> GetAllSubscriptions(Guid streamId, string streamNamespace, string providerToUse)
        {
            logger.LogInformation("GetAllSubscriptionHandles");

            // get stream
            var streamProvider = this.GetStreamProvider(providerToUse);
            var stream = streamProvider.GetStream<int>(streamNamespace, streamId);

            // get all active subscription handles for this stream.
            return stream.GetAllSubscriptionHandles();
        }

        public Task<Dictionary<StreamSubscriptionHandle<int>, Tuple<int,int>>> GetNumberConsumed()
        {
            logger.LogInformation(
                "ConsumedMessageCounts = {Counts}",
                Utils.EnumerableToString(
                    consumedMessageCounts,
                    kvp => $"Consumer: {kvp.Key.HandleId} -> count: {kvp.Value}"));

            return Task.FromResult(consumedMessageCounts.ToDictionary(kvp => kvp.Key, kvp => Tuple.Create(kvp.Value.Item1.Value, kvp.Value.Item2.Value)));
        }

        public Task ClearNumberConsumed()
        {
            logger.LogInformation("ClearNumberConsumed");
            foreach (var counters in consumedMessageCounts.Values)
            {
                counters.Item1.Clear();
                counters.Item2.Clear();
            }
            return Task.CompletedTask;
        }

        public Task Deactivate()
        {
            DeactivateOnIdle();
            return Task.CompletedTask;
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            logger.LogInformation("OnDeactivateAsync");
            return Task.CompletedTask;
        }

        private Task OnNext(IList<SequentialItem<int>> items, int countCapture, Counter count)
        {
            foreach(var item in items)
            {
                logger.LogInformation("Got next event {Item} on handle {Handle}", item.Item, countCapture);
                var contextValue = RequestContext.Get(SampleStreaming_ProducerGrain.RequestContextKey) as string;
                if (!string.Equals(contextValue, SampleStreaming_ProducerGrain.RequestContextValue))
                {
                    throw new Exception($"Got the wrong RequestContext value {contextValue}.");
                }
                count.Increment();
            }
            return Task.CompletedTask;
        }

        private Task OnError(Exception e, int countCapture, Counter error)
        {
            logger.LogInformation(e, "Got exception on handle {Handle}", countCapture);
            error.Increment();
            return Task.CompletedTask;
        }
    }
}
