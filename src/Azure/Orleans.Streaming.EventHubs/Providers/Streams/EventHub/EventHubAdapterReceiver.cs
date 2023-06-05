using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Streaming.EventHubs.Testing;
using Azure.Messaging.EventHubs;
using Orleans.Statistics;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// Event Hub Partition settings
    /// </summary>
    public class EventHubPartitionSettings
    {
        /// <summary>
        /// Eventhub settings
        /// </summary>
        public EventHubOptions Hub { get; set; }

        public EventHubReceiverOptions ReceiverOptions { get; set; }

        /// <summary>
        /// Partition name
        /// </summary>
        public string Partition { get; set; }
    }

    internal class EventHubAdapterReceiver : IQueueAdapterReceiver, IQueueCache
    {
        public const int MaxMessagesPerRead = 1000;
        private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(5);

        private readonly EventHubPartitionSettings settings;
        private readonly Func<string, IStreamQueueCheckpointer<string>, ILoggerFactory, IEventHubQueueCache> cacheFactory;
        private readonly Func<string, Task<IStreamQueueCheckpointer<string>>> checkpointerFactory;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger logger;
        private readonly IQueueAdapterReceiverMonitor monitor;
        private readonly LoadSheddingOptions loadSheddingOptions;
        private readonly IHostEnvironmentStatistics _hostEnvironmentStatistics;
        private IEventHubQueueCache cache;

        private IEventHubReceiver receiver;

        private Func<EventHubPartitionSettings, string, ILogger, IEventHubReceiver> eventHubReceiverFactory;

        private IStreamQueueCheckpointer<string> checkpointer;
        private AggregatedQueueFlowController flowController;

        // Receiver life cycle
        private int receiverState = ReceiverShutdown;

        private const int ReceiverShutdown = 0;
        private const int ReceiverRunning = 1;

        public int GetMaxAddCount()
        {
            return flowController.GetMaxAddCount();
        }

        public EventHubAdapterReceiver(EventHubPartitionSettings settings,
            Func<string, IStreamQueueCheckpointer<string>, ILoggerFactory, IEventHubQueueCache> cacheFactory,
            Func<string, Task<IStreamQueueCheckpointer<string>>> checkpointerFactory,
            ILoggerFactory loggerFactory,
            IQueueAdapterReceiverMonitor monitor,
            LoadSheddingOptions loadSheddingOptions,
            IHostEnvironmentStatistics hostEnvironmentStatistics,
            Func<EventHubPartitionSettings, string, ILogger, IEventHubReceiver> eventHubReceiverFactory = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.cacheFactory = cacheFactory ?? throw new ArgumentNullException(nameof(cacheFactory));
            this.checkpointerFactory = checkpointerFactory ?? throw new ArgumentNullException(nameof(checkpointerFactory));
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            logger = this.loggerFactory.CreateLogger<EventHubAdapterReceiver>();
            this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            this.loadSheddingOptions = loadSheddingOptions ?? throw new ArgumentNullException(nameof(loadSheddingOptions));
            _hostEnvironmentStatistics = hostEnvironmentStatistics;
            this.eventHubReceiverFactory = eventHubReceiverFactory == null ? EventHubAdapterReceiver.CreateReceiver : eventHubReceiverFactory;
        }

        public Task Initialize(TimeSpan timeout)
        {
            logger.LogInformation("Initializing EventHub partition {EventHubName}-{Partition}.", settings.Hub.EventHubName, settings.Partition);

            // if receiver was already running, do nothing
            return ReceiverRunning == Interlocked.Exchange(ref receiverState, ReceiverRunning)
                ? Task.CompletedTask
                : Initialize();
        }

        /// <summary>
        /// Initialization of EventHub receiver is performed at adapter receiver initialization, but if it fails,
        ///  it will be retried when messages are requested
        /// </summary>
        /// <returns></returns>
        private async Task Initialize()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                checkpointer = await checkpointerFactory(settings.Partition);
                if(cache != null)
                {
                    cache.Dispose();
                    cache = null;
                }
                cache = cacheFactory(settings.Partition, checkpointer, loggerFactory);
                flowController = new AggregatedQueueFlowController(MaxMessagesPerRead) { cache, LoadShedQueueFlowController.CreateAsPercentOfLoadSheddingLimit(loadSheddingOptions, _hostEnvironmentStatistics) };
                var offset = await checkpointer.Load();
                receiver = eventHubReceiverFactory(settings, offset, logger);
                watch.Stop();
                monitor?.TrackInitialization(true, watch.Elapsed, null);
            }
            catch (Exception ex)
            {
                watch.Stop();
                monitor?.TrackInitialization(false, watch.Elapsed, ex);
                throw;
            }
        }

        public async Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount)
        {
            if (receiverState == ReceiverShutdown || maxCount <= 0)
            {
                return new List<IBatchContainer>();
            }

            // if receiver initialization failed, retry
            if (receiver == null)
            {
                logger.Warn(OrleansEventHubErrorCode.FailedPartitionRead,
                    "Retrying initialization of EventHub partition {0}-{1}.", settings.Hub.EventHubName, settings.Partition);
                await Initialize();
                if (receiver == null)
                {
                    // should not get here, should throw instead, but just incase.
                    return new List<IBatchContainer>();
                }
            }
            var watch = Stopwatch.StartNew();
            List<EventData> messages;
            try
            {

                messages = (await receiver.ReceiveAsync(maxCount, ReceiveTimeout))?.ToList();
                watch.Stop();

                monitor?.TrackRead(true, watch.Elapsed, null);
            }
            catch (Exception ex)
            {
                watch.Stop();
                monitor?.TrackRead(false, watch.Elapsed, ex);
                logger.Warn(OrleansEventHubErrorCode.FailedPartitionRead,
                    "Failed to read from EventHub partition {0}-{1}. : Exception: {2}.", settings.Hub.EventHubName,
                    settings.Partition, ex);
                throw;
            }

            var batches = new List<IBatchContainer>();
            if (messages == null || messages.Count == 0)
            {
                monitor?.TrackMessagesReceived(0, null, null);
                return batches;
            }

            // monitor message age
            var dequeueTimeUtc = DateTime.UtcNow;

            var oldestMessageEnqueueTime = messages[0].EnqueuedTime.UtcDateTime;
            var newestMessageEnqueueTime = messages[messages.Count - 1].EnqueuedTime.UtcDateTime;

            monitor?.TrackMessagesReceived(messages.Count, oldestMessageEnqueueTime, newestMessageEnqueueTime);

            var messageStreamPositions = cache.Add(messages, dequeueTimeUtc);
            foreach (var streamPosition in messageStreamPositions)
            {
                batches.Add(new StreamActivityNotificationBatch(streamPosition));
            }
            if (!checkpointer.CheckpointExists)
            {
                checkpointer.Update(
                    messages[0].Offset.ToString(),
                    DateTime.UtcNow);
            }
            return batches;
        }

        public void AddToCache(IList<IBatchContainer> messages)
        {
            // do nothing, we add data directly into cache.  No need for agent involvement
        }

        public bool TryPurgeFromCache(out IList<IBatchContainer> purgedItems)
        {
            purgedItems = null;

            //if not under pressure, signal the cache to do a time based purge
            //if under pressure, which means consuming speed is less than producing speed, then shouldn't purge, and don't read more message into the cache
            if (!IsUnderPressure())
                cache.SignalPurge();
            return false;
        }

        public IQueueCacheCursor GetCacheCursor(StreamId streamId, StreamSequenceToken token)
        {
            return new Cursor(cache, streamId, token);
        }

        public bool IsUnderPressure()
        {
            return GetMaxAddCount() <= 0;
        }

        public Task MessagesDeliveredAsync(IList<IBatchContainer> messages)
        {
            return Task.CompletedTask;
        }

        public async Task Shutdown(TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                // if receiver was already shutdown, do nothing
                if (ReceiverShutdown == Interlocked.Exchange(ref receiverState, ReceiverShutdown))
                {
                    return;
                }

                logger.LogInformation("Stopping reading from EventHub partition {EventHubName}-{Partition}", settings.Hub.EventHubName, settings.Partition);

                // clear cache and receiver
                var localCache = Interlocked.Exchange(ref cache, null);

                var localReceiver = Interlocked.Exchange(ref receiver, null);

                // start closing receiver
                var closeTask = Task.CompletedTask;
                if (localReceiver != null)
                {
                    closeTask = localReceiver.CloseAsync();
                }
                // dispose of cache
                localCache?.Dispose();

                // finish return receiver closing task
                await closeTask;
                watch.Stop();
                monitor?.TrackShutdown(true, watch.Elapsed, null);
            }
            catch (Exception ex)
            {
                watch.Stop();
                monitor?.TrackShutdown(false, watch.Elapsed, ex);
                throw;
            }
        }

        private static IEventHubReceiver CreateReceiver(EventHubPartitionSettings partitionSettings, string offset, ILogger logger)
        {
            return new EventHubReceiverProxy(partitionSettings, offset, logger);
        }

        /// <summary>
        /// For test purpose. ConfigureDataGeneratorForStream will configure a data generator for the stream
        /// </summary>
        /// <param name="streamId"></param>
        internal void ConfigureDataGeneratorForStream(StreamId streamId)
        {
            (receiver as EventHubPartitionGeneratorReceiver)?.ConfigureDataGeneratorForStream(streamId);
        }

        internal void StopProducingOnStream(StreamId streamId)
        {
            (receiver as EventHubPartitionGeneratorReceiver)?.StopProducingOnStream(streamId);
        }

        [GenerateSerializer]
        internal class StreamActivityNotificationBatch : IBatchContainer
        {
            [Id(0)]
            public StreamPosition Position { get; }

            public StreamId StreamId => Position.StreamId;
            public StreamSequenceToken SequenceToken => Position.SequenceToken;

            public StreamActivityNotificationBatch(StreamPosition position)
            {
                Position = position;
            }

            public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>() { throw new NotSupportedException(); }
            public bool ImportRequestContext() { throw new NotSupportedException(); }
        }

        private class Cursor : IQueueCacheCursor
        {
            private readonly IEventHubQueueCache cache;
            private readonly object cursor;
            private IBatchContainer current;

            public Cursor(IEventHubQueueCache cache, StreamId streamId, StreamSequenceToken token)
            {
                this.cache = cache;
                cursor = cache.GetCursor(streamId, token);
            }

            public void Dispose()
            {
            }

            public IBatchContainer GetCurrent(out Exception exception)
            {
                exception = null;
                return current;
            }

            public bool MoveNext()
            {
                IBatchContainer next;
                if (!cache.TryGetNextMessage(cursor, out next))
                {
                    return false;
                }

                current = next;
                return true;
            }

            public void Refresh(StreamSequenceToken token)
            {
            }

            public void RecordDeliveryFailure()
            {
            }
        }
    }
}