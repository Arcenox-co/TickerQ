using Microsoft.CodeAnalysis;

namespace TickerQ.SourceGenerator.Models
{
    internal sealed class TickerMethodModel
    {
        // Attribute values
        public string FunctionName { get; set; }
        public string CronExpression { get; set; }
        public int TaskPriority { get; set; }
        public int MaxConcurrency { get; set; }

        // Method info
        public string MethodName { get; set; }
        public string ClassName { get; set; }
        public string ClassFullName { get; set; }
        public bool IsStatic { get; set; }
        public bool IsAsync { get; set; }

        // Parameter info
        public bool UsesGenericContext { get; set; }
        public string GenericRequestTypeName { get; set; }
        public string GenericRequestTypeFullName { get; set; }
        public bool HasContext { get; set; }
        public bool HasCancellationToken { get; set; }

        // True for ITickerFunction implementations
        public bool IsInterfaceBased { get; set; }

        // Compile-time request schema (Task 4). Populated only for typed functions.
        // RequestType is the CLR request type symbol; the schema/example strings are the
        // emitted JSON Schema 2020-12 document and one deterministic default example.
        // A null SchemaJson on a typed function means schema emission was unsupported
        // (a diagnostic was reported) and no misleading schema must be emitted.
        public ITypeSymbol RequestType { get; set; }
        public string SchemaJson { get; set; }
        public string DefaultExampleJson { get; set; }
        public Location DiagnosticLocation { get; set; }
    }
}
