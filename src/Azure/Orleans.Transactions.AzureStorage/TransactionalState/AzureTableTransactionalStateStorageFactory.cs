using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.AzureStorage
{
    public class AzureTableTransactionalStateStorageFactory : ITransactionalStateStorageFactory, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly string name;
        private readonly AzureTableTransactionalStateOptions options;
        private readonly ClusterOptions clusterOptions;
        private readonly JsonSerializerSettings jsonSettings;
        private readonly ILoggerFactory loggerFactory;
        private TableClient table;

        public static ITransactionalStateStorageFactory Create(IServiceProvider services, string name)
        {
            var optionsMonitor = services.GetRequiredService<IOptionsMonitor<AzureTableTransactionalStateOptions>>();
            return ActivatorUtilities.CreateInstance<AzureTableTransactionalStateStorageFactory>(services, name, optionsMonitor.Get(name));
        }

        public AzureTableTransactionalStateStorageFactory(string name, AzureTableTransactionalStateOptions options, IOptions<ClusterOptions> clusterOptions, IServiceProvider services, ILoggerFactory loggerFactory)
        {
            this.name = name;
            this.options = options;
            this.clusterOptions = clusterOptions.Value;
            jsonSettings = TransactionalStateFactory.GetJsonSerializerSettings(services);
            this.loggerFactory = loggerFactory;
        }

        public ITransactionalStateStorage<TState> Create<TState>(string stateName, IGrainContext context) where TState : class, new()
        {
            var partitionKey = MakePartitionKey(context, stateName);
            return ActivatorUtilities.CreateInstance<AzureTableTransactionalStateStorage<TState>>(context.ActivationServices, table, partitionKey, jsonSettings);
        }

        public void Participate(ISiloLifecycle lifecycle) => lifecycle.Subscribe(OptionFormattingUtilities.Name<AzureTableTransactionalStateStorageFactory>(name), options.InitStage, Init);

        private string MakePartitionKey(IGrainContext context, string stateName)
        {
            var grainKey = context.GrainReference.GrainId.ToString();
            var key = $"{grainKey}_{clusterOptions.ServiceId}_{stateName}";
            return AzureTableUtils.SanitizeTableProperty(key);
        }

        private async Task CreateTable()
        {
            var tableManager = new AzureTableDataManager<TableEntity>(
                options,
                loggerFactory.CreateLogger<AzureTableDataManager<TableEntity>>());
            await tableManager.InitTableAsync();
            table = tableManager.Table;
        }

        private Task Init(CancellationToken cancellationToken) => CreateTable();
    }
}
