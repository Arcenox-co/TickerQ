using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TickerQ.SourceGenerator.Models
{
    internal struct TickerFunctionInterfaceInfo
    {
        public ClassDeclarationSyntax ClassDecl { get; set; }
        public string ClassName { get; set; }
        public string ClassFullName { get; set; }
        public bool HasGenericRequest { get; set; }
        public string RequestTypeFullName { get; set; }
    }
}
