using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Messaging.EventHubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Streaming.EventHubs.Testing
{
    /// <summary>
    /// Generate data for one stream
    /// </summary>
    public class SimpleStreamEventDataGenerator : IStreamDataGenerator<EventData>
    {
        /// <inheritdoc />
        public StreamId StreamId { get; set; }

        /// <inheritdoc />
        public IIntCounter SequenceNumberCounter { set; private get; }
        /// <inheritdoc />
        public bool ShouldProduce { private get; set; }

        private readonly ILogger logger;
        private readonly DeepCopier deepCopier;
        private readonly Serializer serializer;

        public SimpleStreamEventDataGenerator(StreamId streamId, ILogger<SimpleStreamEventDataGenerator> logger, DeepCopier deepCopier, Serializer serializer)
        {
            StreamId = streamId;
            this.logger = logger;
            ShouldProduce = true;
            this.deepCopier = deepCopier;
            this.serializer = serializer;
        }

        /// <inheritdoc />
        public bool TryReadEvents(int maxCount, out IEnumerable<EventData> events)
        {
            if (!ShouldProduce)
            {
                events = null;
                return false;
            }
            int count = maxCount;
            List<EventData> eventDataList = new List<EventData>();
            while (count-- > 0)
            {
                SequenceNumberCounter.Increment();

                var eventData = EventHubBatchContainer.ToEventData(
                    serializer,
                    StreamId,
                    GenerateEvent(SequenceNumberCounter.Value),
                    RequestContextExtensions.Export(deepCopier));

               var wrapper = new WrappedEventData(
                    eventData.Body,
                    eventData.Properties,
                    eventData.SystemProperties,
                    partitionKey: StreamId.GetKeyAsString(),
                    offset: DateTime.UtcNow.Ticks,
                    sequenceNumber: SequenceNumberCounter.Value);

                eventDataList.Add(wrapper);

                logger.LogInformation("Generate data of SequenceNumber {SequenceNumber} for stream {StreamId}", SequenceNumberCounter.Value, StreamId);
            }

            events = eventDataList;
            return eventDataList.Count > 0;
        }

        private IEnumerable<int> GenerateEvent(int sequenceNumber)
        {
            var events = new List<int>();
            events.Add(sequenceNumber);
            return events;
        }
        
        public static Func<StreamId, IStreamDataGenerator<EventData>> CreateFactory(IServiceProvider services)
        {
            return (streamId) => ActivatorUtilities.CreateInstance<SimpleStreamEventDataGenerator>(services, streamId);
        }


        private class WrappedEventData : EventData
        {
            public WrappedEventData(ReadOnlyMemory<byte> eventBody, IDictionary<string, object> properties = null, IReadOnlyDictionary<string, object> systemProperties = null, long sequenceNumber = long.MinValue, long offset = long.MinValue, DateTimeOffset enqueuedTime = default, string partitionKey = null) : base(eventBody, properties, systemProperties, sequenceNumber, offset, enqueuedTime, partitionKey)
            {
            }
        }
    }

    /// <summary>
    /// EHPartitionDataGenerator generate data for a EH partition, which can include data from different streams
    /// </summary>
    public class EventHubPartitionDataGenerator : IDataGenerator<EventData>, IStreamDataGeneratingController
    {
        //differnt stream in the same partition should use the same sequenceNumberCounter
        private readonly EventDataGeneratorStreamOptions options;
        private readonly IntCounter sequenceNumberCounter = new IntCounter();
        private readonly ILogger logger;
        private Func<StreamId, IStreamDataGenerator<EventData>> generatorFactory;
        private List<IStreamDataGenerator<EventData>> generators;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <param name="generatorFactory"></param>
        /// <param name="logger"></param>
        public EventHubPartitionDataGenerator(EventDataGeneratorStreamOptions options, Func<StreamId, IStreamDataGenerator<EventData>> generatorFactory, ILogger logger)
        {
            this.options = options;
            this.generatorFactory = generatorFactory;
            generators = new List<IStreamDataGenerator<EventData>>();
            this.logger = logger;
        }
        /// <inheritdoc />
        public void AddDataGeneratorForStream(StreamId streamId)
        {
            var generator =  generatorFactory(streamId);
            generator.SequenceNumberCounter = sequenceNumberCounter;
            logger.LogInformation("Data generator set up on stream {StreamId}.", streamId);
            generators.Add(generator);
        }
        /// <inheritdoc />
        public void StopProducingOnStream(StreamId streamId)
        {
            generators.ForEach(generator => {
                if (generator.StreamId.Equals(streamId))
                {
                    generator.ShouldProduce = false;
                    logger.LogInformation("Stop producing data on stream {StreamId}.", streamId);
                }
            });
        }
        /// <inheritdoc />
        public bool TryReadEvents(int maxCount, out IEnumerable<EventData> events)
        {
            if (generators.Count == 0)
            {
                events = new List<EventData>();
                return false;
            }
            var eventDataList = new List<EventData>();
            var iterator = generators.AsEnumerable().GetEnumerator();
            var batchCount = maxCount / generators.Count;
            batchCount = batchCount == 0 ? batchCount + 1 : batchCount;
            while (eventDataList.Count < maxCount)
            {
                //if reach to the end of the list, reset iterator to the head
                if (!iterator.MoveNext())
                {
                    iterator.Reset();
                    iterator.MoveNext();
                }
                IEnumerable<EventData> eventData;
                var remainingCount = maxCount - eventDataList.Count;
                var count = remainingCount > batchCount ? batchCount : remainingCount;
                if (iterator.Current.TryReadEvents(count, out eventData))
                {
                    foreach (var data in eventData)
                    {
                        eventDataList.Add(data);
                    }
                }
            }
            iterator.Dispose();
            events = eventDataList.AsEnumerable();
            return eventDataList.Count > 0;
        }
    }
}
