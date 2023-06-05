using Microsoft.Extensions.Logging;
using Orleans.Streams;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    public class ConsumerEventCountingGrain : Grain, IConsumerEventCountingGrain
    {
        private int _numConsumedItems;
        private ILogger _logger;
        private IAsyncObservable<int> _consumer;
        private StreamSubscriptionHandle<int> _subscriptionHandle;
        internal const string StreamNamespace = "HaloStreamingNamespace";

        public ConsumerEventCountingGrain(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger($"{GetType().Name}-{IdentityString}");
        }

        private class AsyncObserver<T> : IAsyncObserver<T>
        {
            private readonly Func<T, Task> _onNext;

            public AsyncObserver(Func<T, Task> onNext)
            {
                _onNext = onNext;
            }

            public Task OnNextAsync(T item, StreamSequenceToken token = null) => _onNext(item);

            public Task OnCompletedAsync() => Task.CompletedTask;

            public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Consumer.OnActivateAsync");
            _numConsumedItems = 0;
            _subscriptionHandle = null;
            return base.OnActivateAsync(cancellationToken);
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Consumer.OnDeactivateAsync");
            await StopConsuming();
            _numConsumedItems = 0;
            await base.OnDeactivateAsync(reason, cancellationToken);
        }

        public async Task BecomeConsumer(Guid streamId, string providerToUse)
        {
            _logger.LogInformation("Consumer.BecomeConsumer");
            if (String.IsNullOrEmpty(providerToUse))
            {
                throw new ArgumentNullException("providerToUse");
            }
            var streamProvider = this.GetStreamProvider(providerToUse);
            var stream = streamProvider.GetStream<int>(StreamNamespace, streamId);
            _consumer = stream;
            _subscriptionHandle = await _consumer.SubscribeAsync(new AsyncObserver<int>(EventArrived));
        }

        private Task EventArrived(int evt)
        {
            _numConsumedItems++;
            _logger.LogInformation("Consumer.EventArrived. NumConsumed so far: {Count}", _numConsumedItems);
            return Task.CompletedTask;
        }

        public async Task StopConsuming()
        {
            _logger.LogInformation("Consumer.StopConsuming");
            if (_subscriptionHandle != null && _consumer != null)
            {
                await _subscriptionHandle.UnsubscribeAsync();
                _subscriptionHandle = null;
                _consumer = null;
            }
        }

        public Task<int> GetNumberConsumed() => Task.FromResult(_numConsumedItems);
    }
}