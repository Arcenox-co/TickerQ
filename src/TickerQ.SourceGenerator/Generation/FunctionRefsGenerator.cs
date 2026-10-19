using System.Collections.Generic;
using System.Linq;
using System.Text;
using TickerQ.SourceGenerator.Models;
using TickerQ.SourceGenerator.Utilities;

namespace TickerQ.SourceGenerator.Generation
{
    internal static class FunctionRefsGenerator
    {
        internal static string Generate(string rootNamespace, List<TickerMethodModel> methods, HashSet<string> additionalNamespaces)
        {
            var grouped = methods
                .GroupBy(m => m.ClassName)
                .OrderBy(g => g.Min(m => m.ClassFullName), System.StringComparer.Ordinal)
                .ThenBy(g => g.Key, System.StringComparer.Ordinal);

            var classGroups = new StringBuilder();

            foreach (var group in grouped)
            {
                var refs = new StringBuilder();
                foreach (var method in group.OrderBy(MethodIdentity, System.StringComparer.Ordinal))
                {
                    var functionName = SourceGeneratorUtilities.FormatStringLiteral(method.FunctionName);
                    var line = method.UsesGenericContext
                        ? Templates.FunctionRefGeneric
                            .Replace("{{REQUEST_TYPE}}", method.GenericRequestTypeFullName)
                            .Replace("{{PROPERTY_NAME}}", method.MethodName)
                            .Replace("{{FUNCTION_NAME}}", functionName)
                        : Templates.FunctionRefSimple
                            .Replace("{{PROPERTY_NAME}}", method.MethodName)
                            .Replace("{{FUNCTION_NAME}}", functionName);

                    refs.AppendLine(line);
                }

                classGroups.AppendLine(Templates.FunctionRefClassGroup
                    .Replace("{{CLASS_NAME}}", group.Key)
                    .Replace("{{REFS}}", refs.ToString().TrimEnd()));
            }

            var usingsBlock = string.Empty;
            if (additionalNamespaces != null && additionalNamespaces.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var ns in additionalNamespaces.OrderBy(n => n))
                    sb.AppendLine($"using {ns};");
                usingsBlock = sb.ToString().TrimEnd();
            }

            return Templates.FunctionRefs
                .Replace("{{NAMESPACE}}", rootNamespace)
                .Replace("{{ADDITIONAL_USINGS}}", usingsBlock)
                .Replace("{{CLASS_GROUPS}}", classGroups.ToString().TrimEnd());
        }

        private static string MethodIdentity(TickerMethodModel method)
            => method.ClassFullName + "." + method.MethodName + "|" + method.FunctionName;
    }
}
