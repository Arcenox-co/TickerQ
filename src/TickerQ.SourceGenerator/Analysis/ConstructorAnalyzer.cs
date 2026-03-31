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

            // Check for primary constructor (C# 12+)
            var isPrimaryConstructor = classDecl.ParameterList?.Parameters.Count > 0;

            SeparatedSyntaxList<ParameterSyntax> parameters;

            if (isPrimaryConstructor)
            {
                parameters = classDecl.ParameterList.Parameters;
            }
            else
            {
                var constructors = classDecl.Members.OfType<ConstructorDeclarationSyntax>().ToList();

                // Prefer [TickerQConstructor]-annotated constructor
                var tickerQCtor = constructors.FirstOrDefault(c =>
                {
                    var symbol = semanticModel.GetDeclaredSymbol(c);
                    return symbol?.GetAttributes().Any(a => IsTickerQConstructorAttribute(a)) == true;
                });

                var publicCtor = tickerQCtor ?? constructors
                    .FirstOrDefault(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));

                parameters = publicCtor?.ParameterList.Parameters ?? default;
            }

            foreach (var param in parameters)
            {
                if (param.Type == null)
                    continue;

                var paramName = SourceGeneratorUtilities.FirstLetterToLower(param.Identifier.Text);

                var typeSymbol = ModelExtensions.GetSymbolInfo(semanticModel, param.Type).Symbol;
                var typeName = typeSymbol?.ToDisplayString() ?? param.Type.ToString();

                // Check for keyed services
                var paramSymbol = isPrimaryConstructor
                    ? GetPrimaryConstructorParamSymbol(classDecl, param, semanticModel)
                    : semanticModel.GetDeclaredSymbol(param);

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

        private static IParameterSymbol GetPrimaryConstructorParamSymbol(
            ClassDeclarationSyntax classDecl, ParameterSyntax param, SemanticModel semanticModel)
        {
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            var primaryCtor = classSymbol?.Constructors.FirstOrDefault(c => c.Parameters.Length > 0);
            return primaryCtor?.Parameters.FirstOrDefault(p => p.Name == param.Identifier.Text);
        }

        private static string Global(string fullName) => MethodAnalyzer.Global(fullName);
    }
}
