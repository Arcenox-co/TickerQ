using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Customizer
{
    internal class TickerModelCustomizer<TTimeTicker, TCronTicker> : RelationalModelCustomizer
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        public TickerModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder builder, DbContext context)
        {
            string schema;
            AssistantHistoryOptions assistantHistory;
            var executionLogs = false;
            try
            {
                var optionBuilder = context.GetService<TickerQEfCoreOptionBuilder<TTimeTicker, TCronTicker>>();
                schema = optionBuilder?.Schema ?? Constants.DefaultSchema;
                assistantHistory = optionBuilder?.AssistantHistory ?? AssistantHistoryOptions.Current;
                executionLogs = optionBuilder?.ExecutionLogs?.PersistToDatabase == true;
            }
            catch (InvalidOperationException)
            {
                schema = Constants.DefaultSchema;
                assistantHistory = AssistantHistoryOptions.Current;
            }

            builder.ApplyConfiguration(new TimeTickerConfigurations<TTimeTicker>(schema));
            builder.ApplyConfiguration(new CronTickerConfigurations<TCronTicker>(schema));
            builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<TCronTicker>(schema));
            builder.ApplyConfiguration(new TimeTickerResultConfigurations<TTimeTicker>(schema));
            builder.ApplyConfiguration(new CronTickerOccurrenceResultConfigurations<TCronTicker>(schema));
            builder.ApplyConfiguration(new NodeFinalizationOutboxConfigurations(schema));
            builder.ApplyConfiguration(new TickerQStoreMetadataConfigurations(schema));
            builder.ApplyConfiguration(new TickerQNodeConfigurations(schema));
            builder.ApplyConfiguration(new TickerQControlCommandConfigurations(schema));
            builder.ApplyConfiguration(new IntegrationSettingsConfigurations(schema));
            builder.ApplyConfiguration(new TickerQDefinitionConfigurations(schema));
            if (executionLogs) builder.ApplyConfiguration(new ExecutionLogArchiveConfiguration(schema));

            // Opt-in assistant conversation and investigation tables (AddAssistantHistory()).
            if (assistantHistory != null)
            {
                builder.ApplyConfiguration(new AssistantConversationConfigurations(schema));
                builder.ApplyConfiguration(new AssistantMessageConfigurations(schema));
                builder.ApplyConfiguration(new AssistantInvestigationConfigurations(schema));
                builder.ApplyConfiguration(new AssistantInvestigationLeaseConfigurations(schema));
                builder.ApplyConfiguration(new AssistantInvestigationBudgetConfigurations(schema));
            }

            base.Customize(builder, context);
        }
    }
}
