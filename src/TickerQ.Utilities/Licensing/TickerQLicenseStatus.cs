namespace TickerQ.Utilities.Licensing
{
    /// <summary>
    /// Coarse, user-facing state of the offline TickerQ license certificate. Execution enforcement is
    /// exposed separately through <see cref="TickerQLicenseState.ExecutionAllowed"/> so an active but
    /// non-production Evaluation can still run while remaining visibly restricted.
    /// </summary>
    public enum TickerQLicenseStatus
    {
        /// <summary>No certificate was configured or found on disk.</summary>
        Missing,

        /// <summary>A certificate was found but failed cryptographic or schema-contract verification.</summary>
        Invalid,

        /// <summary>A verified, in-force certificate. Execution is allowed.</summary>
        Active,

        /// <summary>A verified certificate whose runtime expiry is within the warning window. Execution is allowed.</summary>
        Expiring,

        /// <summary>A verified certificate whose runtime expiry has passed. Readable for diagnosis, but execution is blocked.</summary>
        Expired
    }
}
