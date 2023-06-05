﻿using Orleans.Placement;
using Orleans.Runtime;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    [HashBasedPlacement]
    public class HashBasedBasedPlacementGrain : Grain, IHashBasedPlacementGrain
    {

        public Task<SiloAddress> GetSiloAddress() => Task.FromResult(Runtime.SiloAddress);
    }
}