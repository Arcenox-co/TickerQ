using Microsoft.CodeAnalysis;

namespace TickerQ.SourceGenerator.Validation
{
    /// <summary>
    /// Contains all diagnostic descriptors used by the TickerQ source generator.
    /// </summary>
    internal static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor ClassAccessibility = new DiagnosticDescriptor(
            "TQ001",
            "Class accessibility issue",
            "The class '{0}' should be public or internal to be used with [TickerFunction]",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor MethodAccessibility = new DiagnosticDescriptor(
            "TQ002",
            "Method accessibility issue",
            "The method '{0}' should be public or internal to be used with [TickerFunction]",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor InvalidCronExpression = new DiagnosticDescriptor(
            "TQ003",
            "Invalid cron expression",
            "The cron expression '{0}' in function '{1}' is invalid",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor MissingFunctionName = new DiagnosticDescriptor(
            "TQ004",
            "Missing function name",
            "The [TickerFunction] attribute on method '{0}' in class '{1}' must specify a function name",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor DuplicateFunctionName = new DiagnosticDescriptor(
            "TQ005",
            "Duplicate function name",
            "The function name '{0}' is already used by another [TickerFunction] method",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor MultipleConstructors = new DiagnosticDescriptor(
            "TQ006",
            "Multiple constructors detected",
            "The class '{0}' has multiple constructors. Only the first constructor will be used for dependency injection. Consider using [TickerQConstructor] attribute to explicitly mark the preferred constructor.",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Warning,
            true
        );

        public static readonly DiagnosticDescriptor AbstractClass = new DiagnosticDescriptor(
            "TQ007",
            "Abstract class with TickerFunction",
            "The abstract class '{0}' contains [TickerFunction] methods",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor NestedClass = new DiagnosticDescriptor(
            "TQ008",
            "Nested class with TickerFunction",
            "The nested class '{0}' contains [TickerFunction] methods. TickerFunction methods are only allowed in top-level classes.",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor InvalidMethodParameter = new DiagnosticDescriptor(
            "TQ009",
            "Invalid TickerFunction parameter",
            "The method '{0}' has invalid parameter '{1}' of type '{2}'. TickerFunction methods can only have TickerFunctionContext, TickerFunctionContext<T>, CancellationToken parameters, or no parameters.",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public static readonly DiagnosticDescriptor MultipleTickerQConstructorAttributes = new DiagnosticDescriptor(
            "TQ010",
            "Multiple TickerQConstructor attributes",
            "The class '{0}' has multiple constructors with [TickerQConstructor] attribute. Only one constructor can be marked with [TickerQConstructor].",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );
    }
}
