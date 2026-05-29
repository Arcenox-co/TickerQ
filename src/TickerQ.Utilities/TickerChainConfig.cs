namespace TickerQ.Utilities
{
    /// <summary>
    /// Global limits for fluent chain builders (TimeTicker and Periodic chain templates).
    /// The execution engine itself supports arbitrary depth and parallel branching; these
    /// values only bound the fluent builders' recursive <c>WithChild</c> API to guard against
    /// accidentally huge graphs. Defaults preserve the legacy <c>FluentChainTickerBuilder</c>
    /// behavior (up to 5 children per node, 2 levels below the root: child + grandchild).
    /// Configure via <c>SetChainLimits(...)</c> on the TickerQ options builder.
    /// </summary>
    public static class TickerChainConfig
    {
        /// <summary>
        /// Maximum number of children allowed under a single node (breadth). Default: 5.
        /// </summary>
        public static int MaxChildrenPerNode { get; set; } = 5;

        /// <summary>
        /// Maximum nesting depth below the root (child = level 1, grandchild = level 2). Default: 2.
        /// </summary>
        public static int MaxDepth { get; set; } = 2;
    }
}

