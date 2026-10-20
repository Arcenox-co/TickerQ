using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TickerQ.SourceGenerator.Models;
using TickerQ.SourceGenerator.Utilities;

namespace TickerQ.SourceGenerator.Analysis
{
    internal static class ConstructorAnalyzer
    {
        /// <summary>
        /// Builds a ConstructorModel for a class that needs DI-based instantiation.
        /// Returns null for static classes.
        /// </summary>
        internal static ConstructorModel Analyze(ClassDeclarationSyntax classDecl, SemanticModel semanticModel)
        {
            if (classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                return null;

            var fullClassName = SourceGeneratorUtilities.GetFullClassName(classDecl);
            var factoryMethodName = "Create_" + fullClassName.Replace(".", "_");

            var model = new ConstructorModel
            {
                ClassName = classDecl.Identifier.Text,
                ClassFullName = Global(fullClassName),
                FactoryMethodName = factoryMethodName
            };

            // Symbols span every partial declaration, including constructor-only files.
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            var constructors = classSymbol?.InstanceConstructors ?? default;
            var selected = constructors.FirstOrDefault(c => c.GetAttributes().Any(IsTickerQConstructorAttribute))
                ?? constructors.FirstOrDefault(c => c.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is ClassDeclarationSyntax))
                ?? constructors.Where(c => c.DeclaredAccessibility == Accessibility.Public)
                    .OrderBy(c => c.Locations.FirstOrDefault()?.SourceTree?.FilePath, System.StringComparer.Ordinal)
                    .ThenBy(c => c.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0).FirstOrDefault();

            if (selected == null) return model;
            foreach (var paramSymbol in selected.Parameters)
            {
                var paramName = SourceGeneratorUtilities.FirstLetterToLower(paramSymbol.Name);
                var typeName = paramSymbol.Type.ToDisplayString();

                var keyedAttr = paramSymbol?.GetAttributes()
                    .FirstOrDefault(a =>
                    {
                        var name = a.AttributeClass?.Name;
                        var full = a.AttributeClass?.ToDisplayString();
                        return name == SourceGeneratorConstants.FromKeyedServicesAttributeName
                            || name == "FromKeyedServices"
                            || full == "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"
                            || full?.EndsWith(SourceGeneratorConstants.FromKeyedServicesAttributeName) == true;
                    });

                var paramModel = new ConstructorParamModel
                {
                    ParamName = paramName,
                    TypeFullName = Global(typeName)
                };

                if (keyedAttr != null)
                {
                    var serviceKey = SourceGeneratorUtilities.GetServiceKey(keyedAttr);
                    if (serviceKey != null)
                    {
                        paramModel.IsKeyed = true;
                        paramModel.ServiceKey = serviceKey;
                    }
                }

                model.Parameters.Add(paramModel);
            }

            return model;
        }

        private static bool IsTickerQConstructorAttribute(AttributeData attr)
        {
            var name = attr.AttributeClass?.Name;
            var full = attr.AttributeClass?.ToDisplayString();
            return name == "TickerQConstructorAttribute"
                || name == "TickerQConstructor"
                || full == "TickerQ.Utilities.TickerQConstructorAttribute"
                || full == "TickerQ.Utilities.TickerQConstructor";
        }

        private static string Global(string fullName) => MethodAnalyzer.Global(fullName);
    }
}
