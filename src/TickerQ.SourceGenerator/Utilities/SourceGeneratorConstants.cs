using System;
using System.Collections.Generic;

namespace TickerQ.SourceGenerator.Utilities
{
    /// <summary>
    /// Constants used throughout the TickerQ source generator for consistent behavior and performance.
    /// </summary>
    internal static class SourceGeneratorConstants
    {
        #region Type Names
        
        public const string TickerFunctionAttributeName = "TickerFunctionAttribute";
        public const string CancellationTokenTypeName = "System.Threading.CancellationToken";
        public const string BaseTickerFunctionContextTypeName = "TickerQ.Utilities.Base.TickerFunctionContext";
        public const string BaseGenericTickerFunctionContextTypeName = "TickerQ.Utilities.Base.TickerFunctionContext`1";
        public const string FromKeyedServicesAttributeName = "FromKeyedServicesAttribute";
        
        #endregion
        
        #region File and Configuration
        
        public const string GeneratedFileName = "TickerQInstanceFactory.g.cs";
        public const string ConfigExpressionPrefix = "%";
        public const string ConfigExpressionSuffix = "%";
        public const int MinConfigExpressionLength = 2;
        
        #endregion
        
        #region Performance Constants
        public const int InitialStringBuilderCapacity = 8192; // Pre-allocate reasonable capacity
        
        public static readonly string[] CommonNamespaces =
        {
            "System",
            "System.Collections.Generic", 
            "System.Threading",
            "System.Threading.Tasks",
            "Microsoft.Extensions.DependencyInjection"
        };
        
        // Pre-computed common variable names for performance (static readonly for better memory usage)
        public static readonly HashSet<string> CommonVariableNames = 
            new HashSet<string>(StringComparer.Ordinal)
        {
            "context", "service", "serviceProvider", "tickerFunctionDelegateDict",
            "cancellationToken", "genericContext", "requestTypes", "args",
            "sb", "delegates", "ctorCalls", "namespaces"
        };
        
        #endregion
        
        #region Regex Patterns
        
        // Pre-compiled regex patterns for optimal performance
        public static readonly System.Text.RegularExpressions.Regex[] CompiledPatterns = 
        {
            // Generic type parameters: typeof(Namespace.Type)
            new System.Text.RegularExpressions.Regex(@"typeof\s*\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*\)", 
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            // Type declarations: new Namespace.Type()
            new System.Text.RegularExpressions.Regex(@"new\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*\(", 
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            // Type casts: (Namespace.Type)
            new System.Text.RegularExpressions.Regex(@"\(\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*\)", 
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            // Generic type arguments: <Namespace.Type>
            new System.Text.RegularExpressions.Regex(@"<\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*>", 
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            // Static method calls: Namespace.Type.Method
            new System.Text.RegularExpressions.Regex(@"([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\.[A-Za-z_][A-Za-z0-9_]*\s*\(", 
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant)
        };
        
        #endregion
    }
}
