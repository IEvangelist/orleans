using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    public interface IFaultInjectionTransactionalStateConfiguration : ITransactionalStateConfiguration
    {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class FaultInjectionTransactionalStateAttribute : Attribute, IFacetMetadata, IFaultInjectionTransactionalStateConfiguration
    {
        public string StateName { get; }
        public string StorageName { get; }

        public FaultInjectionTransactionalStateAttribute(string stateName, string storageName = null)
        {
            StateName = stateName;
            StorageName = storageName;
        }
    }

    public interface IFaultInjectionTransactionalStateFactory
    {
        IFaultInjectionTransactionalState<TState> Create<TState>(IFaultInjectionTransactionalStateConfiguration config) where TState : class, new();
    }

    public class FaultInjectionTransactionalStateFactory : IFaultInjectionTransactionalStateFactory
    {
        private readonly IGrainContextAccessor contextAccessor;
        public FaultInjectionTransactionalStateFactory(IGrainContextAccessor contextAccessor)
        {
            this.contextAccessor = contextAccessor;
        }

        public IFaultInjectionTransactionalState<TState> Create<TState>(IFaultInjectionTransactionalStateConfiguration config) where TState : class, new()
        {
            var currentContext = contextAccessor.GrainContext;
            var transactionalState = ActivatorUtilities.CreateInstance<TransactionalState<TState>>(currentContext.ActivationServices, new TransactionalStateConfiguration(config), contextAccessor);
            var deactivationTransactionalState = ActivatorUtilities.CreateInstance<FaultInjectionTransactionalState<TState>>(currentContext.ActivationServices, transactionalState);
            deactivationTransactionalState.Participate(currentContext.ObservableLifecycle);
            return deactivationTransactionalState;
        }
    }

    public class FaultInjectionTransactionalStateAttributeMapper : IAttributeToFactoryMapper<FaultInjectionTransactionalStateAttribute>
    {
        private static readonly MethodInfo create =
            typeof(IFaultInjectionTransactionalStateFactory).GetMethod("Create");
        public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, FaultInjectionTransactionalStateAttribute attribute)
        {
            IFaultInjectionTransactionalStateConfiguration config = attribute;
            // use generic type args to define collection type.
            var genericCreate = create.MakeGenericMethod(parameter.ParameterType.GetGenericArguments());
            var args = new object[] { config };
            return context => Create(context, genericCreate, args);
        }

        private object Create(IGrainContext context, MethodInfo genericCreate, object[] args)
        {
            var factory = context.ActivationServices.GetRequiredService<IFaultInjectionTransactionalStateFactory>();
            return genericCreate.Invoke(factory, args);
        }
    }
}
