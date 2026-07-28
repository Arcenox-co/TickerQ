using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
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
            // Only scan [TickerFunction] attribute on methods
            // ITickerFunction interface implementations are handled at runtime via MapTicker<T>()
            var attributeMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: (ctx, _) => GetTickerMethodIfAny(ctx))
                .Where(pair => pair != null)
                .Select((pair, _) => pair.Value);

            var compilationAndMethods = context.CompilationProvider
                .Combine(attributeMethods.Collect());

            var configOptionsProvider = context.AnalyzerConfigOptionsProvider;

            context.RegisterSourceOutput(compilationAndMethods.Combine(configOptionsProvider), (productionContext, source) =>
            {
                var ((compilation, methodPairs), configOptions) = source;

                if (compilation.Assembly.Name == "TickerQ")
                    return;

                configOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
                var effectiveNamespace = !string.IsNullOrEmpty(rootNamespace)
                    ? rootNamespace
                    : SourceGeneratorUtilities.SanitizeNamespace(compilation.Assembly.Name);

                GenerateFactory(productionContext, compilation, methodPairs, effectiveNamespace);
                GenerateFunctionRefs(productionContext, compilation, methodPairs, effectiveNamespace);
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

        #region Factory Generation (TickerQInstanceFactory.g.cs)

        private static void GenerateFactory(
            SourceProductionContext productionContext,
            Compilation compilation,
            ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
            string effectiveNamespace)
        {
            var methods = new List<TickerMethodModel>();
            var constructors = new List<ConstructorModel>();
            var validatedClasses = new HashSet<string>();
            var usedFunctionNames = new HashSet<string>();

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

                method.DiagnosticLocation = methodDecl.Identifier.GetLocation();

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
                ValidateResultContract(method, methodSymbol, productionContext);

                methods.Add(method);
            }

            // Analyze constructors for distinct classes
            var distinctClasses = methodPairs.Select(p => p.ClassDecl).Distinct();
            foreach (var classDecl in distinctClasses)
            {
                var semanticModel = compilation.GetSemanticModel(classDecl.SyntaxTree);
                var ctor = ConstructorAnalyzer.Analyze(classDecl, semanticModel);
                if (ctor != null)
                    constructors.Add(ctor);
            }

            if (methods.Count == 0)
                return;

            EmitRequestSchemas(methods, productionContext);
            EmitResultSchemas(methods);

            var source = FactoryGenerator.Generate(effectiveNamespace, methods, constructors);
            var formatted = SourceGeneratorUtilities.FormatCode(source);

            productionContext.AddSource(
                SourceGeneratorConstants.GeneratedFileName,
                SourceText.From(formatted, Encoding.UTF8));

        }

        /// <summary>
        /// Runs the compile-time JSON Schema 2020-12 emitter for every typed function and stashes the
        /// resulting schema + default example on the model. Unsupported wire shapes are reported as
        /// diagnostics (TQ012/TQ013) and leave <see cref="TickerMethodModel.SchemaJson"/> null so no
        /// misleading schema is emitted.
        /// </summary>
        private static void EmitRequestSchemas(List<TickerMethodModel> methods, SourceProductionContext productionContext)
        {
            foreach (var method in methods)
            {
                if (!method.UsesGenericContext || method.RequestType == null)
                    continue;

                var result = Generation.Schema.JsonSchemaEmitter.Emit(method.RequestType);
                if (result.Supported)
                {
                    method.SchemaJson = result.SchemaJson;
                    method.DefaultExampleJson = result.ExampleJson;
                    continue;
                }

                var location = method.DiagnosticLocation ?? Location.None;
                if (result.ConverterType != null)
                {
                    productionContext.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.CustomConverterRequestSchema, location,
                        method.FunctionName, result.ConverterType));
                }
                else
                {
                    productionContext.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.UnsupportedRequestSchema, location,
                        method.FunctionName, result.Reason));
                }
            }
        }

        private static void ValidateResultContract(
            TickerMethodModel method,
            IMethodSymbol methodSymbol,
            SourceProductionContext productionContext)
        {
            if (method.ResultType == null || method.HasValidResultContract)
                return;

            var invalidType = method.ResultType.SpecialType == SpecialType.System_Void
                || method.ResultType is INamedTypeSymbol named && named.IsUnboundGenericType;
            productionContext.ReportDiagnostic(Diagnostic.Create(
                invalidType
                    ? DiagnosticDescriptors.InvalidResultContract
                    : DiagnosticDescriptors.ResultContractReturnTypeMismatch,
                method.DiagnosticLocation ?? Location.None,
                method.FunctionName,
                method.ResultType.ToDisplayString(),
                methodSymbol.ReturnType.ToDisplayString()));
        }

        private static void EmitResultSchemas(List<TickerMethodModel> methods)
        {
            foreach (var method in methods)
            {
                if (!method.HasValidResultContract)
                    continue;

                var result = Generation.Schema.JsonSchemaEmitter.EmitResult(method.ResultType);
                if (result.Supported)
                    method.ResultSchemaJson = result.SchemaJson;
            }
        }

        #endregion

        #region Function Refs Generation (TickerFunctions.g.cs)

        private static void GenerateFunctionRefs(
            SourceProductionContext productionContext,
            Compilation compilation,
            ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
            string effectiveNamespace)
        {
            var methods = new List<TickerMethodModel>();

            foreach (var (classDecl, methodDecl) in methodPairs)
            {
                var semanticModel = compilation.GetSemanticModel(methodDecl.SyntaxTree);
                var method = MethodAnalyzer.Analyze(classDecl, methodDecl, semanticModel);
                if (method == null) continue;
                methods.Add(method);
            }

            if (methods.Count == 0) return;

            var tickerFunctionRefType = compilation.GetTypeByMetadataName("TickerQ.Utilities.TickerFunctionRef");
            if (tickerFunctionRefType == null) return;

            var source = FunctionRefsGenerator.Generate(effectiveNamespace, methods, new HashSet<string>());
            var formatted = SourceGeneratorUtilities.FormatCode(source);

            productionContext.AddSource(
                "TickerFunctions.g.cs",
                SourceText.From(formatted, Encoding.UTF8));
        }

        #endregion
    }
}
