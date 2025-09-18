using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TickerQ.SourceGenerator.Validation
{
    /// <summary>
    /// Handles validation of TickerFunction attribute values and usage.
    /// </summary>
    internal static class AttributeValidator
    {
        /// <summary>
        /// Validates all aspects of a TickerFunction attribute and its usage.
        /// </summary>
        public static void ValidateTickerFunctionAttribute(
            (string functionName, string cronExpression, int taskPriority) attributeValues,
            ClassDeclarationSyntax classDeclaration,
            MethodDeclarationSyntax methodDeclaration,
            IMethodSymbol methodSymbol,
            string className,
            Location attributeLocation,
            HashSet<string> usedFunctionNames,
            SourceProductionContext context)
        {
            // Validate function name
            if (string.IsNullOrWhiteSpace(attributeValues.functionName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingFunctionName,
                    attributeLocation,
                    methodDeclaration.Identifier.Text,
                    className
                ));
            }
            else
            {
                // Check for duplicate function names
                if (usedFunctionNames.Contains(attributeValues.functionName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateFunctionName,
                        attributeLocation,
                        attributeValues.functionName
                    ));
                }
                else
                {
                    usedFunctionNames.Add(attributeValues.functionName);
                }
            }

            // Validate cron expression
            TickerFunctionValidator.ValidateCronExpression(
                attributeValues.cronExpression,
                className,
                attributeLocation,
                context);
        }
    }
}
