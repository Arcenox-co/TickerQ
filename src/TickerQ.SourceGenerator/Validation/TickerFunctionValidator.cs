using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TickerQ.SourceGenerator.Utilities;

namespace TickerQ.SourceGenerator.Validation
{
    /// <summary>
    /// Handles validation of TickerFunction attributes and their usage.
    /// </summary>
    internal static class TickerFunctionValidator
    {
        /// <summary>
        /// Validates class and method accessibility for TickerFunction usage.
        /// </summary>
        public static void ValidateClassAndMethod(
            ClassDeclarationSyntax classDeclaration,
            MethodDeclarationSyntax methodDeclaration,
            Compilation compilation,
            SourceProductionContext context)
        {
            ValidateClassAccessibility(classDeclaration, context);
            ValidateMethodAccessibility(methodDeclaration, context);

            var semanticModel = compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
            var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

            if (classSymbol != null && classSymbol.IsAbstract)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.AbstractClass,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                ));
            }
        }

        /// <summary>
        /// Validates that a class has appropriate accessibility for TickerFunction usage.
        /// </summary>
        private static void ValidateClassAccessibility(ClassDeclarationSyntax classDeclaration, SourceProductionContext context)
        {
            var hasPublicOrInternal = classDeclaration.Modifiers.Any(m =>
                m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword));

            if (!hasPublicOrInternal)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ClassAccessibility,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                ));
            }
        }

        /// <summary>
        /// Validates that a method has appropriate accessibility for TickerFunction usage.
        /// </summary>
        private static void ValidateMethodAccessibility(MethodDeclarationSyntax methodDeclaration, SourceProductionContext context)
        {
            var hasPublicOrInternal = methodDeclaration.Modifiers.Any(m =>
                m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword));

            if (!hasPublicOrInternal)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MethodAccessibility,
                    methodDeclaration.Identifier.GetLocation(),
                    methodDeclaration.Identifier.Text
                ));
            }
        }

        /// <summary>
        /// Validates cron expression format and correctness.
        /// </summary>
        public static void ValidateCronExpression(
            string cronExpression,
            string className,
            Location attributeLocation,
            SourceProductionContext context)
        {
            // Skip validation if cron expression is null or empty (function name only attribute)
            if (string.IsNullOrEmpty(cronExpression))
                return;
                
            if (IsConfigurationExpression(cronExpression))
                return; // Skip validation for configuration expressions

            if (!CronValidator.IsValidCronExpression(cronExpression))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InvalidCronExpression,
                    attributeLocation,
                    cronExpression,
                    className
                ));
            }
        }

        /// <summary>
        /// Determines if a cron expression is a configuration placeholder.
        /// </summary>
        private static bool IsConfigurationExpression(string cronExpression)
        {
            if (string.IsNullOrEmpty(cronExpression))
                return false;
                
            return cronExpression.StartsWith(SourceGeneratorConstants.ConfigExpressionPrefix, StringComparison.Ordinal) &&
                   cronExpression.EndsWith(SourceGeneratorConstants.ConfigExpressionSuffix, StringComparison.Ordinal) &&
                   cronExpression.Length >= SourceGeneratorConstants.MinConfigExpressionLength;
        }

        /// <summary>
        /// Validates that a class is not nested.
        /// </summary>
        public static void ValidateNotNestedClass(
            ClassDeclarationSyntax classDeclaration,
            SourceProductionContext context)
        {
            // Check if the class is nested (has a parent class)
            if (classDeclaration.Parent is ClassDeclarationSyntax)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.NestedClass,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                ));
            }
        }

        /// <summary>
        /// Validates that TickerFunction method parameters are only allowed types.
        /// </summary>
        public static void ValidateMethodParameters(
            MethodDeclarationSyntax methodDeclaration,
            IMethodSymbol methodSymbol,
            SourceProductionContext context)
        {
            foreach (var parameter in methodSymbol.Parameters)
            {
                var parameterType = parameter.Type;
                var parameterTypeString = parameterType.ToDisplayString();
                
                // Check if parameter is one of the allowed types
                var isValidParameter = false;
                
                // Check for CancellationToken
                if (parameterTypeString == SourceGeneratorConstants.CancellationTokenTypeName)
                {
                    isValidParameter = true;
                }
                // Check for non-generic TickerFunctionContext
                else if (parameterTypeString == SourceGeneratorConstants.BaseTickerFunctionContextTypeName)
                {
                    isValidParameter = true;
                }
                // Check for generic TickerFunctionContext<T> - try both namespaces for compatibility
                else if (parameterType is INamedTypeSymbol namedType && 
                         namedType.IsGenericType && 
                         (namedType.ConstructedFrom?.ToDisplayString() == "TickerQ.Utilities.TickerFunctionContext<T>" ||
                          namedType.ConstructedFrom?.ToDisplayString() == "TickerQ.Utilities.Base.TickerFunctionContext<T>"))
                {
                    isValidParameter = true;
                }
                // Also check by namespace and name for more robust detection
                else if (parameterType is INamedTypeSymbol namedType2)
                {
                    var namespaceName = namedType2.ContainingNamespace?.ToDisplayString();
                    var typeName = namedType2.Name;
                    
                    if ((namespaceName == "TickerQ.Utilities" || namespaceName == "TickerQ.Utilities.Base") && 
                        typeName == "TickerFunctionContext")
                    {
                        isValidParameter = true;
                    }
                }

                if (!isValidParameter)
                {
                    var parameterSyntax = methodDeclaration.ParameterList.Parameters
                        .FirstOrDefault(p => p.Identifier.Text == parameter.Name);
                    
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.InvalidMethodParameter,
                        parameterSyntax?.GetLocation() ?? methodDeclaration.Identifier.GetLocation(),
                        methodDeclaration.Identifier.Text,
                        parameter.Name,
                        parameterTypeString
                    ));
                }
            }
        }
    }
}
