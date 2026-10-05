using System;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Durable activation partition for one application sharing a persistence store. The namespace is
    /// canonicalized through the same bounded identity contract as code-owned Cron definitions.
    /// </summary>
    public sealed class ReconciliationActivationScope
    {
        public ReconciliationActivationScope(string applicationNamespace)
        {
            RuntimePartition = new TickerQRuntimePartition(applicationNamespace);
            ApplicationNamespace = RuntimePartition.ApplicationNamespace;
            ScopeKey = RuntimePartition.StorageKey;
        }

        public TickerQRuntimePartition RuntimePartition { get; }
        public string ApplicationNamespace { get; }
        public string ScopeKey { get; }
    }
}
