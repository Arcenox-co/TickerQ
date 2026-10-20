using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TickerQ.SourceGenerator.Validation
{
    /// <summary>
    /// Handles validation of constructor configurations for TickerFunction classes.
    /// </summary>
    internal static class ConstructorValidator
    {
        /// <summary>
        /// Validates that a class doesn't have multiple constructors.
        /// Issues a warning if multiple constructors are found and no TickerQConstructor attribute is present.
        /// Issues an error if multiple constructors have TickerQConstructor attribute.
        /// </summary>
        public static void ValidateMultipleConstructors(
            ClassDeclarationSyntax classDeclaration,
            SemanticModel semanticModel,
            SourceProductionContext context)
        {
            var symbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
            var constructors = symbol?.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared).ToList()
                ?? new List<IMethodSymbol>();
            var totalConstructors = constructors.Count;
            var constructorsWithTickerQAttribute = constructors.Where(c => c.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "TickerQConstructorAttribute"
                || attr.AttributeClass?.Name == "TickerQConstructor")).ToList();

            // Error if multiple constructors have TickerQConstructor attribute
            if (constructorsWithTickerQAttribute.Count > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MultipleTickerQConstructorAttributes,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                ));
            }
            // Warning if multiple constructors exist but no TickerQConstructor attribute
            else if (totalConstructors > 1 && constructorsWithTickerQAttribute.Count == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MultipleConstructors,
                    classDeclaration.Identifier.GetLocation(),
                    classDeclaration.Identifier.Text
                ));
            }
        }
    }
}
