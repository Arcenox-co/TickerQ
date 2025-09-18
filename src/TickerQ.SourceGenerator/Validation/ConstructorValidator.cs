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
            var constructors = classDeclaration.Members.OfType<ConstructorDeclarationSyntax>().ToList();
            var hasPrimaryConstructor = classDeclaration.ParameterList?.Parameters.Count > 0;
            
            // Count total constructors (regular + primary)
            var totalConstructors = constructors.Count + (hasPrimaryConstructor ? 1 : 0);
            
            // Check for TickerQConstructor attributes
            var constructorsWithTickerQAttribute = new List<ConstructorDeclarationSyntax>();
            
            foreach (var constructor in constructors)
            {
                var constructorSymbol = semanticModel.GetDeclaredSymbol(constructor);
                if (constructorSymbol != null)
                {
                    var hasTickerQAttribute = constructorSymbol.GetAttributes().Any(attr =>
                    {
                        var attributeClass = attr.AttributeClass;
                        if (attributeClass == null) return false;
                        
                        var attributeName = attributeClass.Name;
                        var fullName = attributeClass.ToDisplayString();
                        
                        return attributeName == "TickerQConstructorAttribute" || 
                               attributeName == "TickerQConstructor" ||
                               fullName == "TickerQ.Utilities.TickerQConstructorAttribute" ||
                               fullName == "TickerQ.Utilities.TickerQConstructor";
                    });
                    
                    if (hasTickerQAttribute)
                    {
                        constructorsWithTickerQAttribute.Add(constructor);
                    }
                }
            }
            
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
