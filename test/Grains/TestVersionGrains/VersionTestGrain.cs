using TestVersionGrainInterfaces;
using UnitTests.GrainInterfaces;

namespace TestVersionGrains
{
    public class VersionUpgradeTestGrain : Grain, IVersionUpgradeTestGrain
    {
        public Task<int> GetVersion() =>
#if VERSION_1
            Task.FromResult(1);
#else
            Task.FromResult(2);
#endif


        public Task<int> ProxyGetVersion(IVersionUpgradeTestGrain other) => other.GetVersion();

        public async Task<bool> LongRunningTask(TimeSpan taskTime)
        {
            await Task.Delay(taskTime);
            return true;
        }
    }

    [VersionAwareStrategy]
    public class VersionPlacementTestGrain : Grain, IVersionPlacementTestGrain
    {
        public Task<int> GetVersion() =>
#if VERSION_1
            Task.FromResult(1);
#else
            Task.FromResult(2);
#endif

    }
}
