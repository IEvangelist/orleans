﻿using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Orleans.Runtime;

namespace DependencyInjection.Tests
{
    public abstract class DependencyInjectionDisambiguationTestRunner
    {
        [Fact]
        public void DisambiguateByKeyTest()
        {
            var services = BuildServiceProvider(ConfigureServices());
            var actual0 = services.GetServiceByKey<int, IValue<int>>(0).Value;
            Assert.StrictEqual(0, actual0);
            var actual1 = services.GetServiceByKey<int, IValue<int>>(1).Value;
            Assert.StrictEqual(1, actual1);
        }

        [Fact]
        public void DisambiguateByNameTest()
        {
            var services = BuildServiceProvider(ConfigureServices());
            var actualThis = services.GetServiceByName<IValue<string>>("this").Value;
            Assert.Equal("this", actualThis);
            var actualThat = services.GetServiceByName<IValue<string>>("that").Value;
            Assert.Equal("that", actualThat);
        }

        private interface IValue<out TValue>
        {
            TValue Value { get; }
        }

        private class SomeValue<TValue> : IValue<TValue>
        {
            public TValue Value { get; set; }
        }

        private class ValueServiceCollection<TValue> : IKeyedServiceCollection<TValue, IValue<TValue>>
        {
            public IValue<TValue> GetService(IServiceProvider serviceProvider, TValue name) => new SomeValue<TValue> { Value = name };

            public IEnumerable<IKeyedService<TValue, IValue<TValue>>> GetServices(IServiceProvider services) => Enumerable.Empty<IKeyedService<TValue, IValue<TValue>>>();
        }

        //Build the service container, based on which DI solution you uses
        protected abstract IServiceProvider BuildServiceProvider(IServiceCollection services);

        private IServiceCollection ConfigureServices()
        {
            var services = new ServiceCollection();
            // add services by Key;
            services.AddSingleton<IKeyedServiceCollection<int, IValue<int>>, ValueServiceCollection<int>>();

            // add named services
            services.AddTransient<IKeyedServiceCollection<string, IValue<string>>, ValueServiceCollection<string>>();

            return services;
        }
    }
}
