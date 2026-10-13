using System;

namespace TickerQ.Utilities.Models;

internal static class RuntimeManifestAdmission
{
    internal static void Validate(
        DefinedCronSeedManifest manifest,
        bool bindingConfigured,
        bool schedulerEnabled,
        string boundScopeKey,
        long boundEpoch)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!bindingConfigured)
            return;

        if (boundScopeKey == null)
        {
            if (!schedulerEnabled && manifest.IsLegacyGlobal)
                return;
            throw new InvalidOperationException(
                "A namespaced defined-Cron manifest requires an explicitly bound runtime namespace.");
        }

        if (manifest.IsLegacyGlobal ||
            !string.Equals(new ReconciliationActivationScope(manifest.ApplicationNamespace).ScopeKey,
                boundScopeKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The defined-Cron manifest namespace does not match this provider's immutable runtime partition.");

        if (schedulerEnabled &&
            !StartupSeederAdmissionContext.Matches(boundScopeKey, boundEpoch))
            throw new InvalidOperationException(
                "Authoritative defined-Cron reconciliation requires the initializer's exact startup-seeder admission capability.");
    }
}
