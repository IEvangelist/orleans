using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.ConsistentRing;
using Orleans.Streams;
using Xunit;
using Xunit.Abstractions;
using TestExtensions;

namespace UnitTests.LivenessTests
{
    public class ConsistentRingProviderTests : IClassFixture<ConsistentRingProviderTests.Fixture>
    {
        private readonly ITestOutputHelper output;

        public class Fixture
        {
            public Fixture()
            {
            }
        }

        public ConsistentRingProviderTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact, TestCategory("Functional"), TestCategory("Liveness"), TestCategory("Ring"), TestCategory("RingStandalone")]
        public void ConsistentRingProvider_Test1()
        {
            var silo1 = SiloAddressUtils.NewLocalSiloAddress(0);
            var ring = new ConsistentRingProvider(silo1, NullLoggerFactory.Instance);
            output.WriteLine("Silo1 range: {0}. The whole ring is: {1}", ring.GetMyRange(), ring.ToString());

            ring.AddServer(SiloAddressUtils.NewLocalSiloAddress(1));
            output.WriteLine("Silo1 range: {0}. The whole ring is: {1}", ring.GetMyRange(), ring.ToString());

            ring.AddServer(SiloAddressUtils.NewLocalSiloAddress(2));
            output.WriteLine("Silo1 range: {0}. The whole ring is: {1}", ring.GetMyRange(), ring.ToString());
        }

        [Fact, TestCategory("Functional"), TestCategory("Liveness"), TestCategory("Ring"), TestCategory("RingStandalone")]
        public void ConsistentRingProvider_Test2()
        {
            var silo1 = SiloAddressUtils.NewLocalSiloAddress(0);
            var ring = new VirtualBucketsRingProvider(silo1, NullLoggerFactory.Instance, 30);
            //ring.logger.SetSeverityLevel(Severity.Warning);
            output.WriteLine("\n\n*** Silo1 range: {0}.\n*** The whole ring with 1 silo is:\n{1}\n\n", ring.GetMyRange(), ring.ToString());

            for (var i = 1; i <= 10; i++)
            {
                ring.SiloStatusChangeNotification(SiloAddressUtils.NewLocalSiloAddress(i), SiloStatus.Active);
                output.WriteLine("\n\n*** Silo1 range: {0}.\n*** The whole ring with {1} silos is:\n{2}\n\n", ring.GetMyRange(), i + 1, ring.ToString());
            }
        }

        [Fact, TestCategory("Functional"), TestCategory("Liveness"), TestCategory("Ring"), TestCategory("RingStandalone")]
        public void ConsistentRingProvider_Test3()
        {
            var NUM_SILOS = 100;
            var NUM_QUEUES = 10024.0;
            var NUM_AGENTS = 4;

            var random = new Random();
            var silo1 = SiloAddressUtils.NewLocalSiloAddress(random.Next(100000));
            var ring = new VirtualBucketsRingProvider(silo1, NullLoggerFactory.Instance, 50);
            //ring.logger.SetSeverityLevel(Severity.Warning);
            
            for (var i = 1; i <= NUM_SILOS - 1; i++)
            {
                ring.SiloStatusChangeNotification(SiloAddressUtils.NewLocalSiloAddress(random.Next(100000)), SiloStatus.Active);
            }
  
            var siloRanges = ring.GetRanges();
            var sortedSiloRanges = siloRanges.ToList();
            sortedSiloRanges.Sort((t1, t2) => t1.Item2.RangePercentage().CompareTo(t2.Item2.RangePercentage()));

            var allAgentRanges = new List<(SiloAddress, List<IRingRangeInternal>)>();
            foreach (var siloRange in siloRanges)
            {
                var agentRanges = new List<IRingRangeInternal>();
                for(var i=0; i < NUM_AGENTS; i++)
                {
                    var agentRange = (IRingRangeInternal)RangeFactory.GetEquallyDividedSubRange(siloRange.Value, NUM_AGENTS, i);
                    agentRanges.Add(agentRange);
                }
                allAgentRanges.Add((siloRange.Key, agentRanges));
            }

            var queueHistogram = GetQueueHistogram(allAgentRanges, (int)NUM_QUEUES);
            var str = Utils.EnumerableToString(sortedSiloRanges,
                tuple => String.Format("Silo {0} -> Range {1:0.000}%, {2} queues: {3}", 
                    tuple.Item1,
                    tuple.Item2.RangePercentage(),
                    queueHistogram[tuple.Item1].Sum(),
                    Utils.EnumerableToString(queueHistogram[tuple.Item1])), "\n");

            output.WriteLine("\n\n*** The whole ring with {0} silos is:\n{1}\n\n", NUM_SILOS, str);

            output.WriteLine("Total number of queues is: {0}", queueHistogram.Values.Sum(list => list.Sum()));
            output.WriteLine("Expected average range per silo is: {0:0.00}%, expected #queues per silo is: {1:0.00}, expected #queues per agent is: {2:0.000}.",
                100.0 / NUM_SILOS, NUM_QUEUES / NUM_SILOS, NUM_QUEUES / (NUM_SILOS * NUM_AGENTS));
            output.WriteLine("Min #queues per silo is: {0}, Max #queues per silo is: {1}.",
                queueHistogram.Values.Min(list => list.Sum()), queueHistogram.Values.Max(list => list.Sum()));
        }

        private Dictionary<SiloAddress, List<int>> GetQueueHistogram(List<(SiloAddress Key, List<IRingRangeInternal> Value)> siloRanges, int totalNumQueues)
        {
            var options = new HashRingStreamQueueMapperOptions();
            options.TotalQueueCount = totalNumQueues;
            var queueMapper = new HashRingBasedStreamQueueMapper(options, "AzureQueues");
            _ = queueMapper.GetAllQueues();

            var queueHistogram = new Dictionary<SiloAddress, List<int>>();
            foreach (var siloRange in siloRanges)
            {
                var agentRanges = new List<int>();
                foreach (var agentRange in siloRange.Value)
                {
                    var numQueues = queueMapper.GetQueuesForRange(agentRange).Count();
                    agentRanges.Add(numQueues);
                }
                agentRanges.Sort();
                queueHistogram.Add(siloRange.Key, agentRanges);
            }
            //queueHistogram.Sort((t1, t2) => t1.Item2.CompareTo(t2.Item2));
            return queueHistogram;
        }
    }
}

