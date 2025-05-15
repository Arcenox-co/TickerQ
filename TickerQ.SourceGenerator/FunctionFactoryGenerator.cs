#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TickerQ.SourceGenerator.AttributeSyntaxes;

namespace TickerQ.SourceGenerator
{
    [Generator]
    public sealed class TickerQSourceGenerator : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor NonInstanceClassError = new DiagnosticDescriptor(
            "TQ001",
            "Class should be public or internal",
            "The class '{0}' must be public or internal to be used with [TickerFunction]",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        private static readonly DiagnosticDescriptor NonReachableMethodError = new DiagnosticDescriptor(
            "TQ002",
            "Method should be public",
            "The method '{0}' in class '{1}' must be public or internal to be used with [TickerFunction]",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        private static readonly DiagnosticDescriptor InvalidCronExpression = new DiagnosticDescriptor(
            "TQ003",
            "Invalid cron expression",
            "The cron expression '{0}' in class '{1}' is invalid",
            "TickerQ.SourceGenerator",
            DiagnosticSeverity.Error,
            true
        );

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var tickerMethods = context.SyntaxProvider.CreateSyntaxProvider(
                    (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    (ctx, _) => GetTickerMethodIfAny(ctx)
                )
                .Where(pair => pair != null)
                .Select((pair, _) => pair.Value);

            var compilationAndMethods = context.CompilationProvider.Combine(tickerMethods.Collect());

            context.RegisterSourceOutput(
                compilationAndMethods,
                (productionContext, source) =>
                {
                    var compilation = source.Left;
                    var methodPairs = source.Right;

                    if (compilation.Assembly.Name == "TickerQ")
                        return;

                    var delegates = BuildTickerFunctionDelegates(methodPairs, compilation, productionContext).ToList();
                    var ctorCalls = BuildCtorMethodCalls(methodPairs, compilation);
                    var code = GenerateSource(
                        delegates.Select(x => x.Item1),
                        ctorCalls,
                        delegates.Select(x => x.Item2),
                        compilation.Assembly.Name
                    );

                    productionContext.AddSource(
                        "TickerQInstanceFactory.g.cs",
                        SourceText.From(FormatCode(code), Encoding.UTF8)
                    );
                }
            );
        }

        private static (ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)? GetTickerMethodIfAny(GeneratorSyntaxContext ctx)
        {
            var methodSyntax = ctx.Node as MethodDeclarationSyntax;
            if (methodSyntax == null) return null;

            var semanticModel = ctx.SemanticModel;
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodSyntax) as IMethodSymbol;
            if (methodSymbol == null) return null;

            if (methodSymbol.ContainingAssembly.Name != semanticModel.Compilation.Assembly.Name)
                return null;

            var hasTickerFunction = methodSymbol.GetAttributes()
                .Any(attr => attr.AttributeClass?.Name == "TickerFunctionAttribute");
            if (!hasTickerFunction) return null;

            var cd = methodSyntax.Parent as ClassDeclarationSyntax;
            if (cd == null) return null;

            return (cd, methodSyntax);
        }

        private static void ValidateClassAndMethod(
            ClassDeclarationSyntax cd,
            MethodDeclarationSyntax method,
            Compilation comp,
            SourceProductionContext context)
        {
            var semanticModel = comp.GetSemanticModel(method.SyntaxTree);
            var methodSymbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            var classSymbol = semanticModel.GetDeclaredSymbol(cd) as INamedTypeSymbol;
            if (methodSymbol == null || classSymbol == null) return;

            if (classSymbol.DeclaredAccessibility == Accessibility.Private || classSymbol.IsAbstract)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    NonInstanceClassError,
                    cd.Identifier.GetLocation(),
                    cd.Identifier.Text
                ));
            }

            if (methodSymbol.DeclaredAccessibility == Accessibility.Private ||
                methodSymbol.DeclaredAccessibility == Accessibility.Protected)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    NonReachableMethodError,
                    method.Identifier.GetLocation(),
                    method.Identifier.Text,
                    cd.Identifier.Text
                ));
            }
        }

        private static IEnumerable<(string, (string, string))> BuildTickerFunctionDelegates(
            ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
            Compilation comp,
            SourceProductionContext context)
        {
            foreach (var pair in methodPairs)
            {
                var cd = pair.ClassDecl;
                var method = pair.MethodDecl;
                ValidateClassAndMethod(cd, method, comp, context);

                var semanticModel = comp.GetSemanticModel(method.SyntaxTree);
                var ms = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
                if (ms == null) continue;

                var tickerAttrData = ms.GetAttributes()
                    .FirstOrDefault(ad => ad.AttributeClass?.Name == "TickerFunctionAttribute");
                if (tickerAttrData == null) continue;

                var attributes = tickerAttrData.GetTickerFunctionAttributeValues();
                var attributeLocation = tickerAttrData.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                                        ?? method.Identifier.GetLocation();

                if (!string.IsNullOrWhiteSpace(attributes.cronExpression))
                {
                    var isFromConfig = attributes.cronExpression.StartsWith("%", StringComparison.Ordinal) &&
                                       attributes.cronExpression.EndsWith("%", StringComparison.Ordinal) &&
                                       attributes.cronExpression.Length >= 2;
                    var isValid = isFromConfig ||
                                  CronValidator.IsValidCronExpression(attributes.cronExpression);

                    if (!isValid)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidCronExpression,
                            attributeLocation,
                            attributes.cronExpression,
                            cd.Identifier.Text
                        ));
                    }
                }

                yield return BuildSingleDelegate(
                    cd,
                    method,
                    ms,
                    semanticModel,
                    comp,
                    attributes.functionName,
                    attributes.taskPriority,
                    attributes.cronExpression
                );
            }
        }

        private static bool ValidatePart(string part, int min, int max)
        {
            if (part == "*") return true;

            ReadOnlySpan<char> span = part.AsSpan();
            var values = new HashSet<int>();
            int i = 0;

            while (i < span.Length)
            {
                int num1 = 0, num2 = -1, step = 1;

                if (span[i] == '*')
                {
                    num1 = min;
                    num2 = max;
                    i++;
                }
                else if (char.IsDigit(span[i]))
                {
                    num1 = ReadNumber(span, ref i);
                }
                else
                {
                    return false;
                }

                if (i < span.Length && span[i] == '-')
                {
                    i++;
                    if (i < span.Length && char.IsDigit(span[i]))
                        num2 = ReadNumber(span, ref i);
                    else
                        return false;
                }

                if (i < span.Length && span[i] == '/')
                {
                    i++;
                    if (i < span.Length && char.IsDigit(span[i]))
                        step = ReadNumber(span, ref i);
                    else
                        return false;
                }

                if (num2 == -1) num2 = num1;
                if (num1 < min || num2 > max || num1 > num2 || step < 1 || step > max)
                    return false;

                for (int v = num1; v <= num2; v += step)
                    values.Add(v);

                if (i < span.Length && span[i] == ',')
                    i++;
            }

            return values.Count > 0;
        }

        private static int ReadNumber(ReadOnlySpan<char> span, ref int index)
        {
            int num = 0;
            while (index < span.Length && char.IsDigit(span[index]))
            {
                num = num * 10 + (span[index] - '0');
                index++;
            }

            return num;
        }

        private static (string, (string, string)) BuildSingleDelegate(
            ClassDeclarationSyntax cd,
            MethodDeclarationSyntax method,
            IMethodSymbol ms,
            SemanticModel model,
            Compilation comp,
            string functionName,
            int functionPriority,
            string cronExpression)
        {
            bool usesGenericContext = false;
            string genericTypeName = null;
            var paramsList = new List<string>();

            foreach (var p in method.ParameterList.Parameters)
            {
                var typeSymbol = model.GetTypeInfo(p.Type).Type;
                if (typeSymbol is INamedTypeSymbol nts && nts.IsGenericType && nts.Name == "TickerFunctionContext")
                {
                    usesGenericContext = true;
                    genericTypeName = nts.TypeArguments.FirstOrDefault()?.ToDisplayString();
                    paramsList.Add("genericContext");
                }
                else
                {
                    var displayType = typeSymbol?.ToDisplayString();
                    switch (displayType)
                    {
                        case "System.Threading.CancellationToken":
                            paramsList.Add("cancellationToken");
                            break;
                        case "TickerQ.Utilities.Models.TickerFunctionContext":
                        case "TickerQ.Utilities.Models.TickerFunctionContext`1":
                            paramsList.Add("context");
                            break;
                    }
                }
            }

            bool isStatic = method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            var awaitable = IsMethodAwaitable(ms, comp);
            var asyncFlag = (usesGenericContext || awaitable) ? "async " : "";
            var cronExprFlag = string.IsNullOrEmpty(cronExpression)
                ? "string.Empty"
                : "\"" + cronExpression + "\"";

            var sb = new StringBuilder();
            sb.AppendLine("      tickerFunctionDelegateDict.TryAdd(\"" + functionName + "\", (" + cronExprFlag + ", (TickerTaskPriority)" + functionPriority + ", new TickerFunctionDelegate(" + asyncFlag + "(cancellationToken, serviceProvider, context) =>");
            sb.AppendLine("      {");

            if (!isStatic)
                sb.AppendLine("        var service = Create" + cd.Identifier.Text + "(serviceProvider);");

            if (usesGenericContext)
                sb.AppendLine("        var genericContext = await ToGenericContextWithRequest<" + genericTypeName + ">(context, serviceProvider, context.Id, context.Type);");

            var maybeAwait = awaitable ? "await " : "";
            var returnLine = (!awaitable && !usesGenericContext) ? "        return Task.CompletedTask;" : "";

            if (isStatic)
                sb.AppendLine("        " + maybeAwait + GetFullClassName(cd) + "." + method.Identifier.Text + "(" + string.Join(", ", paramsList) + ");");
            else
                sb.AppendLine("        " + maybeAwait + "service." + method.Identifier.Text + "(" + string.Join(", ", paramsList) + ");");

            if (!string.IsNullOrEmpty(returnLine))
                sb.AppendLine(returnLine);

            sb.AppendLine("      })));");
            return (sb.ToString(), (genericTypeName, functionName));
        }

        private static IEnumerable<string> BuildCtorMethodCalls(
            IEnumerable<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
            Compilation comp)
        {
            var distinct = methodPairs.Select(p => p.ClassDecl).Distinct();
            foreach (var cd in distinct)
            {
                var model = comp.GetSemanticModel(cd.SyntaxTree);
                var call = BuildCtorCall(cd, model);
                if (!string.IsNullOrEmpty(call))
                    yield return call;
            }
        }

        private static string BuildCtorCall(ClassDeclarationSyntax cd, SemanticModel model)
        {
            var ctor = cd.Members
                .OfType<ConstructorDeclarationSyntax>()
                .FirstOrDefault(x => x.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));

            if (ctor == null)
            {
                return "    private static " + GetFullClassName(cd) + " Create" + cd.Identifier.Text + "(IServiceProvider _) => new " + GetFullClassName(cd) + "();";
            }

            var sb = new StringBuilder();
            sb.AppendLine("    private static " + GetFullClassName(cd) + " Create" + cd.Identifier.Text + "(IServiceProvider serviceProvider)");
            sb.AppendLine("    {");
            var args = new List<string>();
            foreach (var p in ctor.ParameterList.Parameters)
            {
                var name = FirstLetterToLower(p.Identifier.Text.AsSpan());
                var sym = model.GetSymbolInfo(p.Type).Symbol as ITypeSymbol;
                if (sym == null) continue;

                if (name == "serviceProvider")
                {
                    args.Add(name);
                }
                else
                {
                    sb.AppendLine("      var " + name + " = serviceProvider.GetService<" + sym.ToDisplayString() + ">();");
                    args.Add(name);
                }
            }
            sb.AppendLine("      return new " + GetFullClassName(cd) + "(" + string.Join(", ", args) + ");");
            sb.AppendLine("    }");
            return sb.ToString();
        }

        private static string GenerateSource(
            IEnumerable<string> delegates,
            IEnumerable<string> ctorCalls,
            IEnumerable<(string, string)> requestTypes,
            string assemblyName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("//v++");
            sb.AppendLine("using System;");
            sb.AppendLine($"using {assemblyName};");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using TickerQ.Utilities;");
            sb.AppendLine("using TickerQ.Utilities.Models;");
            sb.AppendLine("using TickerQ.Utilities.Enums;");
            sb.AppendLine();
            sb.AppendLine("namespace " + assemblyName);
            sb.AppendLine("{");
            sb.AppendLine("  public static class TickerQInstanceFactory");
            sb.AppendLine("  {");
            sb.AppendLine("#if NET5_0_OR_GREATER\n    [System.Runtime.CompilerServices.ModuleInitializer]\n #endif");
            sb.AppendLine("    public static void Initialize()");
            sb.AppendLine("    {");
            sb.AppendLine("      var tickerFunctionDelegateDict = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>();");
            foreach (var d in delegates) sb.AppendLine(d);
            sb.AppendLine("      TickerFunctionProvider.RegisterFunctions(tickerFunctionDelegateDict);");
            sb.AppendLine("      RegisterRequestTypes();");
            sb.AppendLine("    }");
            foreach (var c in ctorCalls) sb.AppendLine(c);
            sb.AppendLine("    private static async Task<TickerFunctionContext<T>> ToGenericContextWithRequest<T>(");
            sb.AppendLine("      TickerFunctionContext context,");
            sb.AppendLine("      IServiceProvider serviceProvider,");
            sb.AppendLine("      Guid tickerId,");
            sb.AppendLine("      TickerType tickerType)");
            sb.AppendLine("    {");
            sb.AppendLine("      var request = await TickerRequestProvider.GetRequestAsync<T>(serviceProvider, tickerId, tickerType);");
            sb.AppendLine("      return new TickerFunctionContext<T>(context, request);");
            sb.AppendLine("    }");
            sb.AppendLine("    private static void RegisterRequestTypes()");
            sb.AppendLine("    {");
            sb.AppendLine("      var requestTypes = new Dictionary<string, (string, Type)>();");
            foreach (var rt in requestTypes)
            {
                if (!string.IsNullOrEmpty(rt.Item1))
                {
                    sb.AppendLine("      requestTypes.TryAdd(\"" + rt.Item2 + "\", (typeof(" + rt.Item1 + ").FullName, typeof(" + rt.Item1 + ")));");
                }
            }
            sb.AppendLine("      TickerFunctionProvider.RegisterRequestType(requestTypes);");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string GetFullClassName(ClassDeclarationSyntax node)
        {
            var ns = GetNamespace(node);
            return string.IsNullOrEmpty(ns)
                ? node.Identifier.Text
                : ns + "." + node.Identifier.Text;
        }

        private static string GetNamespace(SyntaxNode node)
        {
            while (node != null)
            {
                var nd = node as NamespaceDeclarationSyntax;
                if (nd != null)
                    return nd.Name.ToString();
                node = node.Parent;
            }
            return "";
        }

        private static string FirstLetterToLower(ReadOnlySpan<char> str)
        {
            if (str.Length == 0) return "";
            return char.ToLower(str[0]) + str.Slice(1).ToString();
        }

        private static bool IsMethodAwaitable(IMethodSymbol ms, Compilation comp)
        {
            if (ms.IsAsync) return true;

            var t = comp.GetTypeByMetadataName("System.Threading.Tasks.Task");
            var tg = comp.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
            var vt = comp.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
            var vtg = comp.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
            var rt = ms.ReturnType;

            if (t != null && SymbolEqualityComparer.Default.Equals(rt, t)) return true;
            if (tg!= null && SymbolEqualityComparer.Default.Equals(rt.OriginalDefinition, tg)) return true;
            if (vt!= null && SymbolEqualityComparer.Default.Equals(rt, vt)) return true;
            if (vtg!=null && SymbolEqualityComparer.Default.Equals(rt.OriginalDefinition, vtg)) return true;

            var awaiter = rt.GetMembers("GetAwaiter").OfType<IMethodSymbol>().FirstOrDefault();
            return awaiter != null && awaiter.ReturnType != null;
        }

        private static string FormatCode(string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            var formatted = root.NormalizeWhitespace("  ", "\n", false);
            return formatted.ToFullString();
        }
    }
}
#else
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TickerQ.SourceGenerator.AttributeSyntaxes;

namespace TickerQ.SourceGenerator
{
    [Generator]
    public class TickerQSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var compilation = context.Compilation;
            var receiver = context.SyntaxReceiver as SyntaxReceiver;
            var methodPairs = new List<(ClassDeclarationSyntax, MethodDeclarationSyntax)>();
            
            if (compilation.Assembly.Name == "TickerQ")
                return;

            if (receiver != null)
            {
                foreach (var method in receiver.CandidateMethods)
                {
                    var semanticModel = compilation.GetSemanticModel(method.SyntaxTree);
                    var pair = GetTickerMethodIfAny(method, semanticModel);
                    if (pair.HasValue)
                        methodPairs.Add(pair.Value);
                }
            }

            var delegates = BuildTickerFunctionDelegates(methodPairs, compilation, context).ToList();
            var ctorCalls = BuildCtorMethodCalls(methodPairs, compilation);
            var code = GenerateSource(
                delegates.Select(x => x.Item1),
                ctorCalls,
                delegates.Select(x => x.Item2),
                compilation.Assembly.Name
            );

            context.AddSource(
                "TickerQInstanceFactory.g.cs",
                SourceText.From(FormatCode(code), Encoding.UTF8)
            );
        }

        private class SyntaxReceiver : ISyntaxReceiver
        {
            public List<MethodDeclarationSyntax> CandidateMethods { get; } = new List<MethodDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode node)
            {
                if (node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0)
                    CandidateMethods.Add(m);
            }
        }

        private static (ClassDeclarationSyntax, MethodDeclarationSyntax)? GetTickerMethodIfAny(
            MethodDeclarationSyntax methodSyntax,
            SemanticModel semanticModel)
        {
            var methodSymbol = ModelExtensions.GetDeclaredSymbol(semanticModel, methodSyntax) as IMethodSymbol;
            if (methodSymbol == null) return null;

            if (methodSymbol.ContainingAssembly.Name != semanticModel.Compilation.Assembly.Name)
                return null;

            var hasTickerFunction = methodSymbol.GetAttributes()
                .Any(attr => attr.AttributeClass?.Name == "TickerFunctionAttribute");
            if (!hasTickerFunction)
                return null;

            if (!(methodSyntax.Parent is ClassDeclarationSyntax cd))
                return null;
            return (cd, methodSyntax);
        }

        private static void ValidateClassAndMethod(
            ClassDeclarationSyntax cd,
            MethodDeclarationSyntax method,
            Compilation comp,
            GeneratorExecutionContext context)
        {
            var semanticModel = comp.GetSemanticModel(method.SyntaxTree);
            var methodSymbol = ModelExtensions.GetDeclaredSymbol(semanticModel, method) as IMethodSymbol;
            var classSymbol = ModelExtensions.GetDeclaredSymbol(semanticModel, cd) as INamedTypeSymbol;
            if (methodSymbol == null || classSymbol == null) return;

            if (classSymbol.DeclaredAccessibility == Accessibility.Private || classSymbol.IsAbstract)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "TQ001",
                        "Class should be public or internal",
                        "The class '{0}' must be public or internal to be used with [TickerFunction]",
                        "TickerQ.SourceGenerator",
                        DiagnosticSeverity.Error,
                        true),
                    cd.Identifier.GetLocation(),
                    cd.Identifier.Text));
            }

            if (methodSymbol.DeclaredAccessibility == Accessibility.Private ||
                methodSymbol.DeclaredAccessibility == Accessibility.Protected)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "TQ002",
                        "Method should be public",
                        "The method '{0}' in class '{1}' must be public or internal to be used with [TickerFunction]",
                        "TickerQ.SourceGenerator",
                        DiagnosticSeverity.Error,
                        true),
                    method.Identifier.GetLocation(),
                    method.Identifier.Text,
                    cd.Identifier.Text));
            }
        }

        private static IEnumerable<(string, (string, string))> BuildTickerFunctionDelegates(
            IEnumerable<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
            Compilation comp,
            GeneratorExecutionContext context)
        {
            foreach (var (cd, method) in methodPairs)
            {
                ValidateClassAndMethod(cd, method, comp, context);
                var semanticModel = comp.GetSemanticModel(method.SyntaxTree);
                var ms = ModelExtensions.GetDeclaredSymbol(semanticModel, method) as IMethodSymbol;
                if (ms == null) continue;

                var attr = ms.GetAttributes()
                    .FirstOrDefault(ad => ad.AttributeClass?.Name == "TickerFunctionAttribute");
                if (attr == null) continue;

                var vals = attr.GetTickerFunctionAttributeValues();
                var location = attr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                               ?? method.Identifier.GetLocation();

                if (!string.IsNullOrWhiteSpace(vals.cronExpression))
                {
                    var isFromConfig = vals.cronExpression.StartsWith("%") &&
                                       vals.cronExpression.EndsWith("%") &&
                                       vals.cronExpression.Length >= 2;
                    var isValid = isFromConfig || CronValidator.IsValidCronExpression(vals.cronExpression);
                    if (!isValid)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "TQ003",
                                "Invalid cron expression",
                                "The cron expression '{0}' in class '{1}' is invalid",
                                "TickerQ.SourceGenerator",
                                DiagnosticSeverity.Error,
                                true),
                            location,
                            vals.cronExpression,
                            cd.Identifier.Text));
                    }
                }

                yield return BuildSingleDelegate(
                    cd,
                    method,
                    ms,
                    semanticModel,
                    comp,
                    vals.functionName,
                    vals.taskPriority,
                    vals.cronExpression);
            }
        }

        private static (string, (string, string)) BuildSingleDelegate(
            ClassDeclarationSyntax cd,
            MethodDeclarationSyntax method,
            IMethodSymbol ms,
            SemanticModel model,
            Compilation comp,
            string functionName,
            int priority,
            string cron)
        {
            bool usesGen = false;
            string genType = null;
            var args = new List<string>();
            foreach (var p in method.ParameterList.Parameters)
            {
                var t = ModelExtensions.GetTypeInfo(model, p.Type).Type;
                if (t is INamedTypeSymbol nts && nts.Name == "TickerFunctionContext" && nts.IsGenericType)
                {
                    usesGen = true;
                    genType = nts.TypeArguments.FirstOrDefault()?.ToDisplayString();
                    args.Add("genericContext");
                }
                else if (t?.ToDisplayString() == "System.Threading.CancellationToken")
                    args.Add("cancellationToken");
                else
                    args.Add("context");
            }

            var awaitable = IsMethodAwaitable(ms, comp);
            var asyncKw = (usesGen || awaitable) ? "async " : "";
            var cronLit = string.IsNullOrEmpty(cron) ? "string.Empty" : "\"" + cron + "\"";
            var sb = new StringBuilder();
            sb.AppendLine(
                $"      tickerFunctionDelegateDict.TryAdd(\"{functionName}\", ({cronLit}, (TickerTaskPriority){priority}, new TickerFunctionDelegate({asyncKw}(cancellationToken, serviceProvider, context) =>");
            sb.AppendLine("      {");
            if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                sb.AppendLine($"        var service = Create{cd.Identifier.Text}(serviceProvider);");
            if (usesGen)
                sb.AppendLine(
                    $"        var genericContext = await ToGenericContextWithRequest<{genType}>(context, serviceProvider, context.Id, context.Type);");
            var aw = awaitable ? "await " : "";
            var stmt = method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                ? $"        {aw}{GetFullClassName(cd)}.{method.Identifier.Text}({string.Join(", ", args)});"
                : $"        {aw}service.{method.Identifier.Text}({string.Join(", ", args)});";
            sb.AppendLine(stmt);
            if (!awaitable && !usesGen)
                sb.AppendLine("        return Task.CompletedTask;");
            sb.AppendLine("      })));");
            return (sb.ToString(), (genType, functionName));
        }

        private static IEnumerable<string> BuildCtorMethodCalls(
            IEnumerable<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> pairs,
            Compilation comp)
        {
            foreach (var cd in pairs.Select(p => p.ClassDecl).Distinct())
            {
                var mdl = comp.GetSemanticModel(cd.SyntaxTree);
                var code = BuildCtorCall(cd, mdl);
                if (!string.IsNullOrEmpty(code)) yield return code;
            }
        }

        private static string BuildCtorCall(ClassDeclarationSyntax cd, SemanticModel model)
        {
            var ctor = cd.Members.OfType<ConstructorDeclarationSyntax>()
                .FirstOrDefault(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));
            if (ctor == null)
                return
                    $"    private static {GetFullClassName(cd)} Create{cd.Identifier.Text}(IServiceProvider _) => new {GetFullClassName(cd)}();";
            var sb = new StringBuilder();
            sb.AppendLine(
                $"    private static {GetFullClassName(cd)} Create{cd.Identifier.Text}(IServiceProvider serviceProvider)");
            sb.AppendLine("    {");
            var args = new List<string>();
            foreach (var p in ctor.ParameterList.Parameters)
            {
                var nm = FirstLetterToLower(p.Identifier.Text.AsSpan());
                if (nm != "serviceProvider")
                    sb.AppendLine(
                        $"      var {nm} = serviceProvider.GetService<{ModelExtensions.GetSymbolInfo(model, p.Type).Symbol.ToDisplayString()}>();");
                args.Add(nm);
            }

            sb.AppendLine($"      return new {GetFullClassName(cd)}({string.Join(", ", args)});");
            sb.AppendLine("    }");
            return sb.ToString();
        }

        private static string GenerateSource(
            IEnumerable<string> delegates,
            IEnumerable<string> ctors,
            IEnumerable<(string, string)> reqs,
            string asm)
        {
            var sb = new StringBuilder();
            sb.AppendLine("//v3.1");
            sb.AppendLine("using System;");
            sb.AppendLine($"using {asm};");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using TickerQ.Utilities;");
            sb.AppendLine("using TickerQ.Utilities.Models;");
            sb.AppendLine("using TickerQ.Utilities.Enums;");
            sb.AppendLine();
            sb.AppendLine($"namespace {asm}");
            sb.AppendLine("{");
            sb.AppendLine("  public static class TickerQInstanceFactory");
            sb.AppendLine("  {");
            sb.AppendLine("    public static void Initialize()");
            sb.AppendLine("    {");
            sb.AppendLine(
                "      var tickerFunctionDelegateDict = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>();");
            foreach (var d in delegates) sb.AppendLine(d);
            sb.AppendLine("      TickerFunctionProvider.RegisterFunctions(tickerFunctionDelegateDict);");
            sb.AppendLine("      RegisterRequestTypes();");
            sb.AppendLine("    }");
            foreach (var c in ctors) sb.AppendLine(c);
            sb.AppendLine("    private static async Task<TickerFunctionContext<T>> ToGenericContextWithRequest<T>(");
            sb.AppendLine("      TickerFunctionContext context,");
            sb.AppendLine("      IServiceProvider serviceProvider,");
            sb.AppendLine("      Guid tickerId,");
            sb.AppendLine("      TickerType tickerType)");
            sb.AppendLine("    {");
            sb.AppendLine(
                "      var request = await TickerRequestProvider.GetRequestAsync<T>(serviceProvider, tickerId, tickerType);");
            sb.AppendLine("      return new TickerFunctionContext<T>(context, request);");
            sb.AppendLine("    }");
            sb.AppendLine("    private static void RegisterRequestTypes()");
            sb.AppendLine("    {");
            sb.AppendLine("      var requestTypes = new Dictionary<string, (string, Type)>();");
            foreach (var rt in reqs)
            {
                if (!string.IsNullOrEmpty(rt.Item1))
                    sb.AppendLine(
                        $"      requestTypes.TryAdd(\"{rt.Item2}\", (typeof({rt.Item1}).FullName, typeof({rt.Item1})));"
                    );
            }

            sb.AppendLine("      TickerFunctionProvider.RegisterRequestType(requestTypes);");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string GetFullClassName(ClassDeclarationSyntax node)
        {
            var ns = GetNamespace(node);
            return string.IsNullOrEmpty(ns) ? node.Identifier.Text : ns + "." + node.Identifier.Text;
        }

        private static string GetNamespace(SyntaxNode node)
        {
            while (node != null)
            {
                if (node is NamespaceDeclarationSyntax nd)
                    return nd.Name.ToString();
                node = node.Parent;
            }

            return string.Empty;
        }

        private static string FirstLetterToLower(ReadOnlySpan<char> s)
        {
            if (s.Length == 0) return string.Empty;
            return char.ToLower(s[0]) + s.Slice(1).ToString();
        }

        private static bool IsMethodAwaitable(IMethodSymbol ms, Compilation comp)
        {
            if (ms.IsAsync) return true;
            var t = comp.GetTypeByMetadataName("System.Threading.Tasks.Task");
            var tg = comp.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
            var vt = comp.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
            var vtg = comp.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
            var rt = ms.ReturnType;
            if (t != null && SymbolEqualityComparer.Default.Equals(rt, t)) return true;
            if (tg != null && SymbolEqualityComparer.Default.Equals(rt.OriginalDefinition, tg)) return true;
            if (vt != null && SymbolEqualityComparer.Default.Equals(rt, vt)) return true;
            if (vtg != null && SymbolEqualityComparer.Default.Equals(rt.OriginalDefinition, vtg)) return true;
            return rt.GetMembers("GetAwaiter").OfType<IMethodSymbol>().FirstOrDefault()?.ReturnType != null;
        }

        private static string FormatCode(string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            return root.NormalizeWhitespace("  ", "\n", false).ToFullString();
        }
    }
}

#endif