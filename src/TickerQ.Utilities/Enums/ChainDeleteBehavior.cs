namespace TickerQ.Utilities.Enums
{
    /// <summary>Deletion policy for a time-ticker chain, configured on its root.</summary>
    public enum ChainDeleteBehavior
    {
        /// <summary>Delete the root and all descendants when the whole chain is safe to remove.</summary>
        Cascade = 0,

        /// <summary>Keep the whole chain until every descendant reaches a terminal status, then allow cascade removal.</summary>
        Restrict = 1
    }
}
