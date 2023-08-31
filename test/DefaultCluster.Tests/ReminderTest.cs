using System.Threading.Tasks;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests
{
    public class ReminderTest : HostedTestClusterEnsureDefaultStarted
    {
        public ReminderTest(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        [Fact, TestCategory("BVT"), TestCategory("Reminders")]
        public async Task SimpleGrainGetGrain()
        {
            var grain = this.GrainFactory.GetGrain<IReminderTestGrain>(GetRandomGrainId());
            var notExists = await grain.IsReminderExists("not exists");
            Assert.False(notExists);

            await grain.AddReminder("dummy");
            Assert.True(await grain.IsReminderExists("dummy"));

            await grain.RemoveReminder("dummy");
            Assert.False(await grain.IsReminderExists("dummy"));
        }
    }
}