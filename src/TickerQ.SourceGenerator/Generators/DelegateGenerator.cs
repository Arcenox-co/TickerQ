using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TickerQ.SourceGenerator.Models;
using TickerQ.SourceGenerator.Utilities;

namespace TickerQ.SourceGenerator.Generators
{
    /// <summary>
    /// Handles generation of delegate code for TickerFunction methods.
    /// </summary>
    internal static class DelegateGenerator
    {
        /// <summary>
        /// Analyzes method parameters to determine usage patterns.
        /// </summary>
        public static MethodParameterInfo AnalyzeMethodParameters(MethodDeclarationSyntax methodDeclaration, SemanticModel semanticModel)
        {
            var info = new MethodParameterInfo();

            foreach (var parameter in methodDeclaration.ParameterList.Parameters)
            {
                var typeSymbol = ModelExtensions.GetSymbolInfo(semanticModel, parameter.Type).Symbol;
                var typeName = typeSymbol?.ToDisplayString() ?? parameter.Type.ToString();

                if (typeName.StartsWith(SourceGeneratorConstants.BaseGenericTickerFunctionContextTypeName.Replace("`1", "<"), StringComparison.Ordinal))
                {
                    info.UsesGenericContext = true;
                    
                    // Extract generic type name
                    var startIndex = typeName.IndexOf('<') + 1;
                    var endIndex = typeName.LastIndexOf('>');
                    if (startIndex > 0 && endIndex > startIndex)
                    {
                        info.GenericTypeName = typeName.Substring(startIndex, endIndex - startIndex);
                    }
                }
            }

            return info;
        }

        /// <summary>
        /// Generates the delegate code for a TickerFunction method.
        /// </summary>
        public static string GenerateDelegateCode(
            ClassDeclarationSyntax classDeclaration,
            MethodDeclarationSyntax methodDeclaration,
            MethodParameterInfo methodInfo,
            bool isAwaitable,
            string functionName,
            int functionPriority,
            string cronExpression,
            string assemblyName = null,
            HashSet<string> classNameConflicts = null,
            HashSet<string> typeNameConflicts = null)
        {
            var sb = new StringBuilder(1024); // Delegate methods are medium-sized
            // Only use async if we actually need to await something (generic context conversion or awaitable method)
            // This ensures we always have an await when async is used
            var needsAsync = methodInfo.UsesGenericContext || isAwaitable;
            var asyncFlag = needsAsync ? "async " : "";
            var cronExprFlag = string.IsNullOrEmpty(cronExpression) ? "string.Empty" : $"\"{cronExpression}\"";

            // Generate delegate registration with proper multiline format
            sb.AppendLine($"            tickerFunctionDelegateDict.TryAdd(\"{functionName}\", ({cronExprFlag}, (TickerTaskPriority){functionPriority}, new TickerFunctionDelegate({asyncFlag}(cancellationToken, serviceProvider, context) =>");
            sb.AppendLine("            {");

            var parametersList = new List<string>();
            AddStandardParameter(methodDeclaration, parametersList);
            GenerateMethodCall(sb, classDeclaration, methodDeclaration, parametersList, methodInfo, assemblyName, classNameConflicts, typeNameConflicts);

            sb.AppendLine("            })));");

            return sb.ToString();
        }

        /// <summary>
        /// Adds standard parameters (context, cancellationToken) to the parameter list.
        /// </summary>
        private static void AddStandardParameter(MethodDeclarationSyntax methodDeclaration, List<string> parametersList)
        {
            foreach (var parameter in methodDeclaration.ParameterList.Parameters)
            {
                var parameterType = parameter.Type.ToString();

                if (parameterType.Contains("CancellationToken"))
                {
                    parametersList.Add("cancellationToken");
                }
                else if (parameterType.Contains("TickerFunctionContext"))
                {
                    if (parameterType.Contains("<"))
                    {
                        parametersList.Add("genericContext");
                    }
                    else
                    {
                        parametersList.Add("context");
                    }
                }
            }
        }

        /// <summary>
        /// Generates the method call code within the delegate.
        /// </summary>
        private static void GenerateMethodCall(
            StringBuilder sb,
            ClassDeclarationSyntax classDeclaration,
            MethodDeclarationSyntax methodDeclaration,
            List<string> parametersList,
            MethodParameterInfo methodInfo,
            string assemblyName = null,
            HashSet<string> classNameConflicts = null,
            HashSet<string> typeNameConflicts = null)
        {
            var fullClassName = SourceGeneratorUtilities.GetFullClassName(classDeclaration);
            var classNamespace = SourceGeneratorUtilities.GetNamespace(classDeclaration);
            var isStatic = methodDeclaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword));
            
            // Use simple class name if in root namespace (due to using statement), otherwise use full name
            var simpleClassName = classDeclaration.Identifier.Text;
            var useSimpleName = assemblyName != null && classNamespace == assemblyName;
            
            string methodCall;
            if (isStatic)
            {
                // For static methods, use simple name if in root namespace
                var staticClassName = useSimpleName ? simpleClassName : fullClassName;
                methodCall = $"{staticClassName}.{methodDeclaration.Identifier.Text}";
            }
            else
            {
                // For instance methods, always use full class name for Create method name (to avoid conflicts)
                var createMethodName = $"Create{fullClassName.Replace(".", "")}";
                methodCall = $"{createMethodName}(serviceProvider).{methodDeclaration.Identifier.Text}";
            }

            if (methodInfo.UsesGenericContext)
            {
                // Use simple type name if no conflicts exist, otherwise use full name
                var typeName = GetTypeNameForGeneration(methodInfo.GenericTypeName, typeNameConflicts);
                sb.AppendLine($"                var genericContext = await ToGenericContextWithRequest<{typeName}>(context, cancellationToken);");
            }

            var parameters = string.Join(", ", parametersList);
            var isVoidMethod = !SourceGeneratorUtilities.IsMethodAwaitable(methodDeclaration);
            
            if (SourceGeneratorUtilities.IsMethodAwaitable(methodDeclaration))
            {
                sb.AppendLine($"                await {methodCall}({parameters});");
            }
            else
            {
                sb.AppendLine($"                {methodCall}({parameters});");
                // If method is void, we must return Task.CompletedTask to satisfy the Task-returning delegate
                // This is required whether the lambda is async or not
                if (isVoidMethod)
                {
                    sb.AppendLine("                return Task.CompletedTask;");
                }
            }
        }

        /// <summary>
        /// Gets the appropriate type name for code generation - simple name if no conflicts, full name if conflicts exist.
        /// </summary>
        private static string GetTypeNameForGeneration(string fullTypeName, HashSet<string> typeNameConflicts)
        {
            if (string.IsNullOrEmpty(fullTypeName) || typeNameConflicts == null || typeNameConflicts.Count == 0)
                return fullTypeName;
                
            var simpleName = fullTypeName.Contains('.') ? 
                fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1) : 
                fullTypeName;
                
            // Use full name if there's a conflict with the simple name
            return typeNameConflicts.Contains(simpleName) ? fullTypeName : simpleName;
        }
    }
}
