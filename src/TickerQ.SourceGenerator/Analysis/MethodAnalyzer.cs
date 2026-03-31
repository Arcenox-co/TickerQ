using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TickerQ.SourceGenerator.AttributeSyntaxes;
using TickerQ.SourceGenerator.Models;
using TickerQ.SourceGenerator.Utilities;

namespace TickerQ.SourceGenerator.Analysis
{
    internal static class MethodAnalyzer
    {
        /// <summary>
        /// Builds a TickerMethodModel from Roslyn syntax/semantic info.
        /// Returns null if the method doesn't have a valid TickerFunction attribute.
        /// </summary>
        internal static TickerMethodModel Analyze(
            ClassDeclarationSyntax classDecl,
            MethodDeclarationSyntax methodDecl,
            SemanticModel semanticModel)
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
            if (methodSymbol == null)
                return null;

            // Find [TickerFunction] attribute data
            var attrData = methodSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == SourceGeneratorConstants.TickerFunctionAttributeName
                                  || a.AttributeClass?.Name == "TickerFunctionAttribute");

            if (attrData == null)
                return null;

            var (functionName, cronExpression, taskPriority, maxConcurrency) = attrData.GetTickerFunctionAttributeValues();

            var fullClassName = SourceGeneratorUtilities.GetFullClassName(classDecl);
            var isStatic = methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            var isAsync = SourceGeneratorUtilities.IsMethodAwaitable(methodDecl);

            var model = new TickerMethodModel
            {
                FunctionName = functionName,
                CronExpression = !string.IsNullOrEmpty(cronExpression)
                    ? Validation.CronValidator.NormalizeToSixPart(cronExpression)
                    : cronExpression,
                TaskPriority = taskPriority,
                MaxConcurrency = maxConcurrency,
                MethodName = methodDecl.Identifier.Text,
                ClassName = classDecl.Identifier.Text,
                ClassFullName = Global(fullClassName),
                IsStatic = isStatic,
                IsAsync = isAsync,
            };

            AnalyzeParameters(methodDecl, semanticModel, model);

            return model;
        }

        private static void AnalyzeParameters(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel, TickerMethodModel model)
        {
            foreach (var parameter in methodDecl.ParameterList.Parameters)
            {
                var typeSymbol = ModelExtensions.GetSymbolInfo(semanticModel, parameter.Type).Symbol;
                var typeName = typeSymbol?.ToDisplayString() ?? parameter.Type.ToString();

                if (typeName.Contains("CancellationToken"))
                {
                    model.HasCancellationToken = true;
                }
                else if (typeName.StartsWith(
                    SourceGeneratorConstants.BaseGenericTickerFunctionContextTypeName.Replace("`1", "<"),
                    StringComparison.Ordinal))
                {
                    model.UsesGenericContext = true;
                    model.HasContext = true;

                    var startIndex = typeName.IndexOf('<') + 1;
                    var endIndex = typeName.LastIndexOf('>');
                    if (startIndex > 0 && endIndex > startIndex)
                    {
                        var rawRequestType = typeName.Substring(startIndex, endIndex - startIndex);
                        model.GenericRequestTypeFullName = Global(rawRequestType);
                        var simpleName = model.GenericRequestTypeFullName.Contains(".")
                            ? model.GenericRequestTypeFullName.Substring(model.GenericRequestTypeFullName.LastIndexOf('.') + 1)
                            : model.GenericRequestTypeFullName;
                        model.GenericRequestTypeName = simpleName;
                    }
                }
                else if (typeName.Contains("TickerFunctionContext"))
                {
                    model.HasContext = true;
                }
            }
        }

        /// <summary>
        /// Prefixes a type name with global:: for unambiguous resolution.
        /// Handles C# keyword aliases (string, int, etc.), nullable (int?), arrays (byte[]),
        /// and generic type arguments (List&lt;string&gt;).
        /// </summary>
        internal static string Global(string fullName)
        {
            if (string.IsNullOrEmpty(fullName) || fullName.StartsWith("global::", StringComparison.Ordinal))
                return fullName;

            // Replace all C# keyword aliases with their System.* equivalents first
            var resolved = ReplacePrimitiveAliases(fullName);

            // Now safe to prefix with global::
            return "global::" + resolved;
        }

        private static string ReplacePrimitiveAliases(string typeName)
        {
            // Handle exact matches (simple types like "string", "int")
            var systemType = TryGetSystemType(typeName);
            if (systemType != null) return systemType;

            // Handle nullable: "int?" → "System.Int32?"
            if (typeName.EndsWith("?", StringComparison.Ordinal))
            {
                var inner = typeName.Substring(0, typeName.Length - 1);
                var resolved = TryGetSystemType(inner);
                if (resolved != null) return resolved + "?";
            }

            // Handle arrays: "byte[]" → "System.Byte[]"
            if (typeName.EndsWith("[]", StringComparison.Ordinal))
            {
                var inner = typeName.Substring(0, typeName.Length - 2);
                var resolved = TryGetSystemType(inner);
                if (resolved != null) return resolved + "[]";
            }

            // Handle generic arguments: "System.Collections.Generic.List<string>" → "System.Collections.Generic.List<System.String>"
            var genericStart = typeName.IndexOf('<');
            if (genericStart >= 0)
            {
                var prefix = typeName.Substring(0, genericStart + 1);
                var suffix = typeName.Substring(typeName.LastIndexOf('>'));
                var argsStr = typeName.Substring(genericStart + 1, typeName.LastIndexOf('>') - genericStart - 1);

                // Split by comma, resolve each arg, rejoin
                var args = argsStr.Split(',');
                for (var i = 0; i < args.Length; i++)
                {
                    var arg = args[i].Trim();
                    var resolvedArg = TryGetSystemType(arg);
                    if (resolvedArg != null)
                        args[i] = " " + resolvedArg;
                }

                return prefix + string.Join(",", args) + suffix;
            }

            return typeName;
        }

        private static string TryGetSystemType(string alias)
        {
            switch (alias)
            {
                case "string": return "System.String";
                case "int": return "System.Int32";
                case "long": return "System.Int64";
                case "short": return "System.Int16";
                case "byte": return "System.Byte";
                case "bool": return "System.Boolean";
                case "double": return "System.Double";
                case "float": return "System.Single";
                case "decimal": return "System.Decimal";
                case "char": return "System.Char";
                case "uint": return "System.UInt32";
                case "ulong": return "System.UInt64";
                case "ushort": return "System.UInt16";
                case "sbyte": return "System.SByte";
                case "object": return "System.Object";
                default: return null;
            }
        }
    }
}
