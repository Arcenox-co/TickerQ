using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TickerQ.SourceGenerator.Analysis;
using TickerQ.SourceGenerator.AttributeSyntaxes;
using TickerQ.SourceGenerator.Generation;
using TickerQ.SourceGenerator.Models;
using TickerQ.SourceGenerator.Utilities;
using TickerQ.SourceGenerator.Validation;

namespace TickerQ.SourceGenerator
{
    [Generator]
    public sealed class TickerQIncrementalSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Path 1: [TickerFunction] attribute on methods (existing)
            var attributeMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: (ctx, _) => GetTickerMethodIfAny(ctx))
                .Where(pair => pair != null)
                .Select((pair, _) => pair.Value);

            // Path 2: Classes implementing ITickerFunction / ITickerFunction<T> (new)
            var interfaceClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => node is ClassDeclarationSyntax c && c.BaseList?.Types.Count > 0,
                    transform: (ctx, _) => GetTickerFunctionClassIfAny(ctx))
                .Where(info => info != null)
                .Select((info, _) => info.Value);

            var compilationAndMethods = context.CompilationProvider
                .Combine(attributeMethods.Collect())
                .Combine(interfaceClasses.Collect());

            var configOptionsProvider = context.AnalyzerConfigOptionsProvider;

            context.RegisterSourceOutput(compilationAndMethods.Combine(configOptionsProvider), (productionContext, source) =>
            {
                var (((compilation, methodPairs), interfacePairs), configOptions) = source;

                if (compilation.Assembly.Name == "TickerQ")
                    return;

                configOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
                var effectiveNamespace = !string.IsNullOrEmpty(rootNamespace)
                    ? rootNamespace
                    : SourceGeneratorUtilities.SanitizeNamespace(compilation.Assembly.Name);

                GenerateFactory(productionContext, compilation, methodPairs, interfacePairs, effectiveNamespace);
                GenerateFunctionRefs(productionContext, compilation, methodPairs, interfacePairs, effectiveNamespace);
            });
        }

        #region Discovery: [TickerFunction] attribute

        private static (ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)? GetTickerMethodIfAny(GeneratorSyntaxContext ctx)
        {
            if (!(ctx.Node is MethodDeclarationSyntax methodSyntax)) return null;

            var semanticModel = ctx.SemanticModel;
            if (!(semanticModel.GetDeclaredSymbol(methodSyntax) is IMethodSymbol methodSymbol)) return null;

            if (methodSymbol.ContainingAssembly.Name != semanticModel.Compilation.Assembly.Name)
                return null;

            var hasTickerFunction = methodSymbol.GetAttributes()
                .Any(attr => attr.AttributeClass?.Name == SourceGeneratorConstants.TickerFunctionAttributeName);
            if (!hasTickerFunction) return null;

            if (!(methodSyntax.Parent is ClassDeclarationSyntax cd)) return null;

            return (cd, methodSyntax);
        }

        #endregion

        #region Discovery: ITickerFunction interface

        private static TickerFunctionInterfaceInfo? GetTickerFunctionClassIfAny(GeneratorSyntaxContext ctx)
        {
            if (!(ctx.Node is ClassDeclarationSyntax classDecl)) return null;

            var semanticModel = ctx.SemanticModel;
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol == null) return null;
            if (classSymbol.IsAbstract) return null;

            if (classSymbol.ContainingAssembly.Name != semanticModel.Compilation.Assembly.Name)
                return null;

            // Check all interfaces for ITickerFunction variants
            foreach (var iface in classSymbol.AllInterfaces)
            {
                var ifaceName = iface.ToDisplayString();

                if (ifaceName == "TickerQ.Utilities.Interfaces.ITickerFunction" ||
                    ifaceName.StartsWith("TickerQ.Utilities.Interfaces.ITickerFunction<"))
                {
                    // Check it doesn't also have [TickerFunction] attribute on any method
                    var hasAttribute = classSymbol.GetMembers().OfType<IMethodSymbol>()
                        .Any(m => m.GetAttributes().Any(a => a.AttributeClass?.Name == SourceGeneratorConstants.TickerFunctionAttributeName));

                    if (hasAttribute)
                    {
                        // Will be handled by attribute path — skip to avoid TQ012
                        return null;
                    }

                    var isGeneric = iface.TypeArguments.Length > 0;
                    var requestType = isGeneric ? iface.TypeArguments[0].ToDisplayString() : null;

                    return new TickerFunctionInterfaceInfo
                    {
                        ClassDecl = classDecl,
                        ClassName = classSymbol.Name,
                        ClassFullName = classSymbol.ToDisplayString(),
                        HasGenericRequest = isGeneric,
                        RequestTypeFullName = requestType
                    };
                }
            }

            return null;
        }

        #endregion

        #region Factory Generation (TickerQInstanceFactory.g.cs)

        private static void GenerateFactory(
            SourceProductionContext productionContext,
            Compilation compilation,
            ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
            ImmutableArray<TickerFunctionInterfaceInfo> interfacePairs,
            string effectiveNamespace)
        {
            var methods = new List<TickerMethodModel>();
            var constructors = new List<ConstructorModel>();
            var validatedClasses = new HashSet<string>();
            var usedFunctionNames = new HashSet<string>();

            // Path 1: [TickerFunction] attribute methods
            foreach (var (classDecl, methodDecl) in methodPairs)
            {
                var semanticModel = compilation.GetSemanticModel(methodDecl.SyntaxTree);

                TickerFunctionValidator.ValidateClassAndMethod(classDecl, methodDecl, compilation, productionContext);

                if (validatedClasses.Add(classDecl.Identifier.Text + classDecl.SyntaxTree.FilePath))
                {
                    ConstructorValidator.ValidateMultipleConstructors(classDecl, semanticModel, productionContext);
                    TickerFunctionValidator.ValidateNotNestedClass(classDecl, productionContext);
                }

                var method = MethodAnalyzer.Analyze(classDecl, methodDecl, semanticModel);
                if (method == null) continue;

                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
                var tickerAttr = methodSymbol?.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == SourceGeneratorConstants.TickerFunctionAttributeName);
                if (tickerAttr == null) continue;

                var attrValues = tickerAttr.GetTickerFunctionAttributeValues();
                var attrLocation = tickerAttr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                                   ?? methodDecl.Identifier.GetLocation();

                AttributeValidator.ValidateTickerFunctionAttribute(
                    attrValues, classDecl, methodDecl, methodSymbol,
                    classDecl.Identifier.Text, attrLocation,
                    usedFunctionNames, productionContext);

                TickerFunctionValidator.ValidateMethodParameters(methodDecl, methodSymbol, productionContext);

                methods.Add(method);
            }

            // Path 2: ITickerFunction interface implementations
            foreach (var info in interfacePairs)
            {
                var semanticModel = compilation.GetSemanticModel(info.ClassDecl.SyntaxTree);
                var functionName = info.ClassName; // Type name = function name

                if (!usedFunctionNames.Add(functionName))
                {
                    var location = info.ClassDecl.Identifier.GetLocation();
                    productionContext.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateFunctionName, location, functionName));
                    continue;
                }

                var method = InterfaceMethodAnalyzer.Analyze(info);
                if (method != null)
                    methods.Add(method);
            }

            // Analyze constructors for all distinct classes (both paths)
            var allClasses = methodPairs.Select(p => p.ClassDecl)
                .Concat(interfacePairs.Select(p => p.ClassDecl))
                .Distinct();

            foreach (var classDecl in allClasses)
            {
                var semanticModel = compilation.GetSemanticModel(classDecl.SyntaxTree);
                var ctor = ConstructorAnalyzer.Analyze(classDecl, semanticModel);
                if (ctor != null)
                    constructors.Add(ctor);
            }

            var source = FactoryGenerator.Generate(effectiveNamespace, methods, constructors);
            var formatted = SourceGeneratorUtilities.FormatCode(source);

            productionContext.AddSource(
                SourceGeneratorConstants.GeneratedFileName,
                SourceText.From(formatted, Encoding.UTF8));
        }

        #endregion

        #region Function Refs Generation (TickerFunctions.g.cs)

        private static void GenerateFunctionRefs(
            SourceProductionContext productionContext,
            Compilation compilation,
            ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
            ImmutableArray<TickerFunctionInterfaceInfo> interfacePairs,
            string effectiveNamespace)
        {
            var methods = new List<TickerMethodModel>();
            var additionalNamespaces = new HashSet<string>();

            // Path 1: attribute methods
            foreach (var (classDecl, methodDecl) in methodPairs)
            {
                var semanticModel = compilation.GetSemanticModel(methodDecl.SyntaxTree);
                var method = MethodAnalyzer.Analyze(classDecl, methodDecl, semanticModel);
                if (method == null) continue;
                methods.Add(method);
            }

            // Path 2: interface implementations
            foreach (var info in interfacePairs)
            {
                var method = InterfaceMethodAnalyzer.Analyze(info);
                if (method != null)
                    methods.Add(method);
            }

            if (methods.Count == 0) return;

            var tickerFunctionRefType = compilation.GetTypeByMetadataName("TickerQ.Utilities.TickerFunctionRef");
            if (tickerFunctionRefType == null) return;

            var source = FunctionRefsGenerator.Generate(effectiveNamespace, methods, additionalNamespaces);
            var formatted = SourceGeneratorUtilities.FormatCode(source);

            productionContext.AddSource(
                "TickerFunctions.g.cs",
                SourceText.From(formatted, Encoding.UTF8));
        }

        #endregion
    }
}
