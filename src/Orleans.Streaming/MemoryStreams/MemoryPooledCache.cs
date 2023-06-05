
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers
{
    /// <summary>
    /// Pooled cache for memory stream provider
    /// </summary>
    public class MemoryPooledCache<TSerializer> : IQueueCache, ICacheDataAdapter
        where TSerializer : class, IMemoryMessageBodySerializer
    {
        private readonly IObjectPool<FixedSizeBuffer> bufferPool;
        private readonly TSerializer serializer;
        private readonly IEvictionStrategy evictionStrategy;
        private readonly PooledQueueCache cache;

        private FixedSizeBuffer currentBuffer;

        /// <summary>
        /// Pooled cache for memory stream provider.
        /// </summary>
        /// <param name="bufferPool">The buffer pool.</param>
        /// <param name="purgePredicate">The purge predicate.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="serializer">The serializer.</param>
        /// <param name="cacheMonitor">The cache monitor.</param>
        /// <param name="monitorWriteInterval">The monitor write interval.</param>
        public MemoryPooledCache(
            IObjectPool<FixedSizeBuffer> bufferPool,
            TimePurgePredicate purgePredicate,
            ILogger logger,
            TSerializer serializer,
            ICacheMonitor cacheMonitor,
            TimeSpan? monitorWriteInterval,
            TimeSpan? purgeMetadataInterval)
        {
            this.bufferPool = bufferPool;
            this.serializer = serializer;
            cache = new PooledQueueCache(this, logger, cacheMonitor, monitorWriteInterval, purgeMetadataInterval);
            evictionStrategy = new ChronologicalEvictionStrategy(logger, purgePredicate, cacheMonitor, monitorWriteInterval) {PurgeObservable = cache};
        }

        private CachedMessage QueueMessageToCachedMessage(MemoryMessageData queueMessage, DateTime dequeueTimeUtc)
        {
            var streamPosition = GetStreamPosition(queueMessage);
            return new CachedMessage()
            {
                StreamId = streamPosition.StreamId,
                SequenceNumber = queueMessage.SequenceNumber,
                EnqueueTimeUtc = queueMessage.EnqueueTimeUtc,
                DequeueTimeUtc = dequeueTimeUtc,
                Segment = SerializeMessageIntoPooledSegment(queueMessage)
            };
        }

        // Placed object message payload into a segment from a buffer pool.  When this get's too big, older blocks will be purged
        private ArraySegment<byte> SerializeMessageIntoPooledSegment(MemoryMessageData queueMessage)
        {
            // serialize payload
            var size = SegmentBuilder.CalculateAppendSize(queueMessage.Payload);

            // get segment from current block
            ArraySegment<byte> segment;
            if (currentBuffer == null || !currentBuffer.TryGetSegment(size, out segment))
            {
                // no block or block full, get new block and try again
                currentBuffer = bufferPool.Allocate();
                //call EvictionStrategy's OnBlockAllocated method
                evictionStrategy.OnBlockAllocated(currentBuffer);
                // if this fails with clean block, then requested size is too big
                if (!currentBuffer.TryGetSegment(size, out segment))
                {
                    var errmsg = String.Format(CultureInfo.InvariantCulture,
                        "Message size is too big. MessageSize: {0}", size);
                    throw new ArgumentOutOfRangeException(nameof(queueMessage), errmsg);
                }
            }
            // encode namespace, offset, partitionkey, properties and payload into segment
            var writeOffset = 0;
            SegmentBuilder.Append(segment, ref writeOffset, queueMessage.Payload);
            return segment;
        }

        private StreamPosition GetStreamPosition(MemoryMessageData queueMessage)
        {
            return new StreamPosition(queueMessage.StreamId,
                new EventSequenceTokenV2(queueMessage.SequenceNumber));
        }

        private class Cursor : IQueueCacheCursor
        {
            private readonly PooledQueueCache cache;
            private readonly object cursor;
            private IBatchContainer current;

            public Cursor(PooledQueueCache cache, StreamId streamId,
                StreamSequenceToken token)
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

        /// <inheritdoc/>
        public int GetMaxAddCount() => 100;

        /// <inheritdoc/>
        public void AddToCache(IList<IBatchContainer> messages)
        {
            var utcNow = DateTime.UtcNow;
            var memoryMessages = messages
                .Cast<MemoryBatchContainer<TSerializer>>()
                .Select(container => container.MessageData)
                .Select(batch => QueueMessageToCachedMessage(batch, utcNow))
                .ToList();
            cache.Add(memoryMessages, DateTime.UtcNow);
        }

        /// <inheritdoc/>
        public bool TryPurgeFromCache(out IList<IBatchContainer> purgedItems)
        {
            purgedItems = null;
            evictionStrategy.PerformPurge(DateTime.UtcNow);
            return false;
        }

        /// <inheritdoc/>
        public IQueueCacheCursor GetCacheCursor(StreamId streamId, StreamSequenceToken token) => new Cursor(cache, streamId, token);

        /// <inheritdoc/>
        public bool IsUnderPressure() => false;

        /// <inheritdoc/>
        public IBatchContainer GetBatchContainer(ref CachedMessage cachedMessage)
        {
            //Deserialize payload
            var readOffset = 0;
            var payload = SegmentBuilder.ReadNextBytes(cachedMessage.Segment, ref readOffset);
            var message = MemoryMessageData.Create(cachedMessage.StreamId, new ArraySegment<byte>(payload.ToArray()));
            message.SequenceNumber = cachedMessage.SequenceNumber;
            return new MemoryBatchContainer<TSerializer>(message, serializer);
        }

        /// <inheritdoc/>
        public StreamSequenceToken GetSequenceToken(ref CachedMessage cachedMessage) => new EventSequenceToken(cachedMessage.SequenceNumber);
    }
}
