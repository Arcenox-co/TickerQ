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
using TickerQ.SourceGenerator.Generators;
using TickerQ.SourceGenerator.Utilities;
using TickerQ.SourceGenerator.Validation;

namespace TickerQ.SourceGenerator
{
    [Generator]
    public sealed class TickerQIncrementalSourceGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// Initializes the incremental source generator.
        /// </summary>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var tickerMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: (ctx, _) => GetTickerMethodIfAny(ctx)
                )
                .Where(pair => pair != null)
                .Select((pair, _) => pair.Value);

            var compilationAndMethods = context.CompilationProvider
                .Combine(tickerMethods.Collect());

            context.RegisterSourceOutput(compilationAndMethods, (productionContext, source) =>
            {
                var (compilation, methodPairs) = source;

                if (compilation.Assembly.Name == "TickerQ")
                    return;

                // Generate constructor calls (no need for class conflict detection since we always use full names)
                var constructorCalls = BuildConstructorMethodCalls(methodPairs, compilation, compilation.Assembly.Name).ToList();
                
                // Generate delegates and detect type conflicts for generic types
                var initialDelegatesWithMetadata = BuildTickerFunctionDelegates(methodPairs, compilation, productionContext, compilation.Assembly.Name).ToList();
                var typeNames = initialDelegatesWithMetadata.Select(d => d.Item2.GenericTypeName).Where(t => !string.IsNullOrEmpty(t)).ToList();
                var typeNameConflicts = DetectTypeNameConflicts(typeNames);
                
                // Regenerate delegates with type conflict information for generic types
                var delegatesWithMetadata = BuildTickerFunctionDelegates(methodPairs, compilation, productionContext, compilation.Assembly.Name, null, typeNameConflicts).ToList();
                
                // Collect namespaces from the source types
                var sourceNamespaces = NamespaceCollector.CollectNamespacesFromSourceTypes(methodPairs, compilation);
                
                // Extract data once to avoid multiple enumeration
                var delegateCodes = delegatesWithMetadata.Select(x => x.DelegateCode).ToList();
                var requestTypes = delegatesWithMetadata.Select(x => x.Item2).ToList();
                
                var generatedCode = GenerateSourceWithFullNamespaces(
                    delegateCodes,
                    constructorCalls,
                    requestTypes,
                    compilation.Assembly.Name,
                    typeNameConflicts
                );

                productionContext.AddSource(
                    SourceGeneratorConstants.GeneratedFileName,
                    SourceText.From(SourceGeneratorUtilities.FormatCode(generatedCode), Encoding.UTF8)
                );
            });
        }

        /// <summary>
        /// Extracts ticker method information from the syntax context if it has a TickerFunction attribute.
        /// </summary>
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

        /// <summary>
        /// Builds ticker function delegates for all discovered methods with TickerFunction attributes.
        /// </summary>
        private static IEnumerable<(string DelegateCode, (string GenericTypeName, string FunctionName))> BuildTickerFunctionDelegates(
            ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
            Compilation compilation,
            SourceProductionContext context,
            string assemblyName = null,
            HashSet<string> classNameConflicts = null,
            HashSet<string> typeNameConflicts = null)
        {
            var usedFunctionNames = new HashSet<string>();
            var validatedClasses = new HashSet<ClassDeclarationSyntax>();
            
            foreach (var pair in methodPairs)
            {
                var classDeclaration = pair.ClassDecl;
                var methodDeclaration = pair.MethodDecl;
                
                TickerFunctionValidator.ValidateClassAndMethod(classDeclaration, methodDeclaration, compilation, context);

                var semanticModel = compilation.GetSemanticModel(methodDeclaration.SyntaxTree);

                // Validate multiple constructors once per class
                if (validatedClasses.Add(classDeclaration))
                {
                    ConstructorValidator.ValidateMultipleConstructors(classDeclaration, semanticModel, context);
                    TickerFunctionValidator.ValidateNotNestedClass(classDeclaration, context);
                }
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);

                var tickerAttributeData = methodSymbol?.GetAttributes()
                    .FirstOrDefault(ad => ad.AttributeClass?.Name == SourceGeneratorConstants.TickerFunctionAttributeName);
                if (tickerAttributeData == null) continue;

                var attributeValues = tickerAttributeData.GetTickerFunctionAttributeValues();
                var attributeLocation = tickerAttributeData.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                                        ?? methodDeclaration.Identifier.GetLocation();

                // Validate all attribute values
                AttributeValidator.ValidateTickerFunctionAttribute(
                    attributeValues,
                    classDeclaration,
                    methodDeclaration,
                    methodSymbol,
                    classDeclaration.Identifier.Text,
                    attributeLocation,
                    usedFunctionNames,
                    context);

                // Validate method parameters
                TickerFunctionValidator.ValidateMethodParameters(methodDeclaration, methodSymbol, context);

                yield return BuildSingleDelegate(
                    classDeclaration,
                    methodDeclaration,
                    semanticModel,
                    attributeValues.functionName,
                    attributeValues.taskPriority,
                    attributeValues.cronExpression,
                    assemblyName ?? compilation.Assembly.Name,
                    classNameConflicts,
                    typeNameConflicts
                );
            }
        }

        /// <summary>
        /// Builds a single delegate for a ticker function method.
        /// </summary>
        private static (string DelegateCode, (string GenericTypeName, string FunctionName)) BuildSingleDelegate(
            ClassDeclarationSyntax classDeclaration,
            MethodDeclarationSyntax methodDeclaration,
            SemanticModel semanticModel,
            string functionName,
            int functionPriority,
            string cronExpression,
            string assemblyName,
            HashSet<string> classNameConflicts = null,
            HashSet<string> typeNameConflicts = null)
        {
            var methodInfo = DelegateGenerator.AnalyzeMethodParameters(methodDeclaration, semanticModel);
            var isAwaitable = SourceGeneratorUtilities.IsMethodAwaitable(methodDeclaration);
            
            var delegateCode = DelegateGenerator.GenerateDelegateCode(
                classDeclaration, 
                methodDeclaration, 
                methodInfo, 
                isAwaitable, 
                functionName, 
                functionPriority, 
                cronExpression,
                assemblyName,
                classNameConflicts,
                typeNameConflicts);

            return (delegateCode, (methodInfo.GenericTypeName, functionName));
        }

        /// <summary>
        /// Generates the complete source code for the factory class using full namespaces.
        /// This approach eliminates all using statement complexity and ensures reliable compilation.
        /// </summary>
        private static string GenerateSourceWithFullNamespaces(
            IReadOnlyList<string> delegates,
            IReadOnlyList<string> ctorCalls,
            IReadOnlyList<(string GenericTypeName, string FunctionName)> requestTypes,
            string assemblyName,
            HashSet<string> typeNameConflicts = null)
        {
            var sb = new StringBuilder(SourceGeneratorConstants.InitialStringBuilderCapacity);
            
            // Check if ToGenericContextWithRequest is used (if any request types exist)
            bool includeBaseUtilities = requestTypes.Any(rt => !string.IsNullOrEmpty(rt.GenericTypeName));
            
            GenerateFileHeaderWithTickerQUsings(sb, includeBaseUtilities, assemblyName);
            GenerateClassDeclarationWithFullNamespaces(sb, assemblyName);
            GenerateInitializeMethodWithFullNamespaces(sb, delegates);
            GenerateConstructorMethods(sb, ctorCalls); // Constructor methods already handle their own namespacing
            
            // Only generate helper method if it's needed
            if (includeBaseUtilities)
            {
                GenerateHelperMethodsWithFullNamespaces(sb);
            }
            
            GenerateRequestTypeRegistrationWithFullNamespaces(sb, requestTypes, typeNameConflicts);
            GenerateClassFooter(sb);
            
            return sb.ToString();
        }

        /// <summary>
        /// Generates the complete source code for the factory class (legacy method with using statements).
        /// </summary>
        private static string GenerateSource(
            IReadOnlyList<string> delegates,
            IReadOnlyList<string> ctorCalls,
            IReadOnlyList<(string GenericTypeName, string FunctionName)> requestTypes,
            string assemblyName,
            HashSet<string> additionalNamespaces = null,
            HashSet<string> typeNameConflicts = null)
        {
            var sb = new StringBuilder(SourceGeneratorConstants.InitialStringBuilderCapacity);
            
            // No type aliases needed - we'll use full names when conflicts exist
            
            // Collect all required namespaces from the generated content
            var requiredNamespaces = NamespaceCollector.CollectRequiredNamespaces(delegates, ctorCalls, requestTypes, assemblyName, additionalNamespaces);
            
            GenerateFileHeader(sb, requiredNamespaces);
            GenerateClassDeclaration(sb, assemblyName);
            GenerateInitializeMethod(sb, delegates);
            GenerateConstructorMethods(sb, ctorCalls);
            GenerateHelperMethods(sb);
            GenerateRequestTypeRegistration(sb, requestTypes, typeNameConflicts);
            GenerateClassFooter(sb);
            
            return sb.ToString();
        }


        /// <summary>
        /// Extracts the class name from a constructor call method string.
        /// </summary>
        private static string ExtractClassNameFromConstructorCall(string constructorCall)
        {
            if (string.IsNullOrEmpty(constructorCall))
                return string.Empty;
                
            // Look for pattern: "private static ClassName Create..."
            var lines = constructorCall.Split('\n');
            var methodLine = lines.FirstOrDefault(l => l.Contains("private static") && l.Contains("Create"));
            if (methodLine == null)
                return string.Empty;
                
            // Extract the return type (class name) between "private static " and " Create"
            var startIndex = methodLine.IndexOf("private static ") + "private static ".Length;
            var endIndex = methodLine.IndexOf(" Create");
            if (startIndex >= 0 && endIndex > startIndex)
            {
                return methodLine.Substring(startIndex, endIndex - startIndex).Trim();
            }
            
            return string.Empty;
        }

        /// <summary>
        /// Detects class name conflicts and returns a set of simple class names that have conflicts.
        /// </summary>
        private static HashSet<string> DetectClassNameConflicts(List<string> fullClassNames)
        {
            var simpleNameCounts = new Dictionary<string, int>();
            
            // Count occurrences of each simple class name
            foreach (var fullClassName in fullClassNames)
            {
                var simpleName = fullClassName.Contains('.') ? 
                    fullClassName.Substring(fullClassName.LastIndexOf('.') + 1) : 
                    fullClassName;
                    
                simpleNameCounts[simpleName] = simpleNameCounts.TryGetValue(simpleName, out var count) ? count + 1 : 1;
            }
            
            // Return simple names that have conflicts (count > 1)
            return new HashSet<string>(simpleNameCounts.Where(kv => kv.Value > 1).Select(kv => kv.Key));
        }

        /// <summary>
        /// Detects type name conflicts and returns a set of simple type names that have conflicts.
        /// </summary>
        private static HashSet<string> DetectTypeNameConflicts(List<string> fullTypeNames)
        {
            var simpleNameCounts = new Dictionary<string, int>();
            
            // Count occurrences of each simple type name
            foreach (var fullTypeName in fullTypeNames)
            {
                if (string.IsNullOrEmpty(fullTypeName))
                    continue;
                    
                var simpleName = fullTypeName.Contains('.') ? 
                    fullTypeName[(fullTypeName.LastIndexOf('.') + 1)..] : 
                    fullTypeName;
                    
                simpleNameCounts[simpleName] = simpleNameCounts.TryGetValue(simpleName, out var count) ? count + 1 : 1;
            }
            
            // Return simple names that have conflicts (count > 1)
            return [..simpleNameCounts.Where(kv => kv.Value > 1).Select(kv => kv.Key)];
        }


        /// <summary>
        /// Generates the file header with TickerQ and common .NET using statements.
        /// </summary>
        private static void GenerateFileHeaderWithTickerQUsings(StringBuilder sb, bool includeBaseUtilities = false, string assemblyName = null)
        {
            sb.AppendLine("//TickerQ readonly auto-generated file.");
            sb.AppendLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
            
            // Include common .NET using statements
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            
            // Include TickerQ using statements
            sb.AppendLine("using TickerQ.Utilities;");
            sb.AppendLine("using TickerQ.Utilities.Enums;");
            
            // Include Base utilities if ToGenericContextWithRequest is used
            if (includeBaseUtilities)
            {
                sb.AppendLine("using TickerQ.Utilities.Base;");
            }
            
            // Include root namespace (assembly name) as using statement
            if (!string.IsNullOrEmpty(assemblyName))
            {
                sb.AppendLine($"using {assemblyName};");
            }
            
            sb.AppendLine();
        }

        /// <summary>
        /// Generates the file header with using statements (legacy method).
        /// </summary>
        private static void GenerateFileHeader(StringBuilder sb, HashSet<string> requiredNamespaces)
        {
            sb.AppendLine("//TickerQ readonly auto-generated file.");
            sb.AppendLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
            
            // Sort namespaces for consistent output
            var sortedNamespaces = requiredNamespaces.OrderBy(ns => ns, StringComparer.Ordinal);
            foreach (var ns in sortedNamespaces)
            {
                sb.AppendLine($"using {ns};");
            }
            
            sb.AppendLine();
        }

        #region Code Generation Methods

        /// <summary>
        /// Generates the class declaration.
        /// </summary>
        /// <summary>
        /// Generates the class declaration with full namespaces.
        /// </summary>
        private static void GenerateClassDeclarationWithFullNamespaces(StringBuilder sb, string assemblyName)
        {
            sb.AppendLine($"namespace {assemblyName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static class TickerQInstanceFactoryExtensions_{assemblyName}");
            sb.AppendLine("    {");
        }

        private static void GenerateClassDeclaration(StringBuilder sb, string assemblyName)
        {
            sb.AppendLine($"namespace {assemblyName}");
            sb.AppendLine("{");
            sb.AppendLine($"  public static class TickerQInstanceFactoryExtensions_{assemblyName}");
            sb.AppendLine("  {");
        }

        /// <summary>
        /// Generates the Initialize method with delegate registrations.
        /// </summary>
        /// <summary>
        /// Generates the Initialize method with delegate registrations using full namespaces.
        /// </summary>
        private static void GenerateInitializeMethodWithFullNamespaces(StringBuilder sb, IEnumerable<string> delegates)
        {
            var delegateList = delegates.ToList();
            var delegateCount = delegateList.Count;
            
            sb.AppendLine("        [System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("        public static void Initialize()");
            sb.AppendLine("        {");
            
            if (delegateCount > 0)
            {
                sb.AppendLine($"            var tickerFunctionDelegateDict = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>({delegateCount});");
                
                foreach (var delegateCode in delegateList)
                {
                    sb.Append(delegateCode);
                }
                
                sb.AppendLine($"            TickerFunctionProvider.RegisterFunctions(tickerFunctionDelegateDict, {delegateCount});");
            }
            
            sb.AppendLine("            RegisterRequestTypes();");
            sb.AppendLine("        }");
        }

        private static void GenerateInitializeMethod(StringBuilder sb, IEnumerable<string> delegates)
        {
            var delegateList = delegates.ToList();
            var delegateCount = delegateList.Count;
            
            sb.AppendLine("[System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("    public static void Initialize()");
            sb.AppendLine("    {");
            
            if (delegateCount > 0)
            {
                sb.AppendLine($"      var tickerFunctionDelegateDict = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>({delegateCount});");
                
                foreach (var delegateCode in delegateList)
                {
                    sb.Append(delegateCode);
                }
                
                sb.AppendLine($"      TickerFunctionProvider.RegisterFunctions(tickerFunctionDelegateDict, {delegateCount});");
            }
            
            sb.AppendLine("      RegisterRequestTypes();");
            sb.AppendLine("    }");
        }

        /// <summary>
        /// Generates constructor methods for dependency injection.
        /// </summary>
        private static void GenerateConstructorMethods(StringBuilder sb, IEnumerable<string> ctorCalls)
        {
            foreach (var ctorCall in ctorCalls)
            {
                sb.AppendLine(ctorCall);
            }
        }

        /// <summary>
        /// Generates helper methods for generic context handling using full namespaces.
        /// </summary>
        private static void GenerateHelperMethodsWithFullNamespaces(StringBuilder sb)
        {
            sb.AppendLine("        private static async Task<TickerFunctionContext<T>> ToGenericContextWithRequest<T>(");
            sb.AppendLine("            TickerFunctionContext context,");
            sb.AppendLine("            CancellationToken cancellationToken)");
            sb.AppendLine("        {");
            sb.AppendLine("            var request = await TickerRequestProvider.GetRequestAsync<T>(context, cancellationToken);");
            sb.AppendLine("            return new TickerFunctionContext<T>(context, request);");
            sb.AppendLine("        }");
        }

        /// <summary>
        /// Generates helper methods for generic context handling (legacy method).
        /// </summary>
        private static void GenerateHelperMethods(StringBuilder sb)
        {
            sb.AppendLine("    private static async Task<TickerFunctionContext<T>> ToGenericContextWithRequest<T>(");
            sb.AppendLine("      TickerFunctionContext context,");
            sb.AppendLine("      CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine("      var request = await TickerRequestProvider.GetRequestAsync<T>(context, cancellationToken);");
            sb.AppendLine("      return new TickerFunctionContext<T>(context, request);");
            sb.AppendLine("    }");
        }

        /// <summary>
        /// Generates the request type registration method.
        /// </summary>
        /// <summary>
        /// Generates the request type registration method using full namespaces.
        /// </summary>
        private static void GenerateRequestTypeRegistrationWithFullNamespaces(
            StringBuilder sb, 
            IEnumerable<(string GenericTypeName, string FunctionName)> requestTypes,
            HashSet<string> typeNameConflicts = null)
        {
            var requestTypesList = requestTypes.ToList();
            var requestTypesWithGeneric = requestTypesList.Where(rt => !string.IsNullOrEmpty(rt.GenericTypeName)).ToList();
            var requestTypesCount = requestTypesWithGeneric.Count;
            
            sb.AppendLine("        private static void RegisterRequestTypes()");
            sb.AppendLine("        {");
            
            if (requestTypesCount > 0)
            {
                sb.AppendLine($"            var requestTypes = new Dictionary<string, (string, Type)>({requestTypesCount});");
                
                foreach (var (genericTypeName, functionName) in requestTypesWithGeneric)
                {
                    // Use the simple type name if no conflicts exist, otherwise use full name
                    var typeName = GetTypeNameForGeneration(genericTypeName, typeNameConflicts);
                    
                    sb.AppendLine($"            requestTypes.TryAdd(\"{functionName}\", (typeof({typeName}).FullName, typeof({typeName})));");
                }
                
                sb.AppendLine($"            TickerFunctionProvider.RegisterRequestType(requestTypes, {requestTypesCount});");
            }
            
            sb.AppendLine("        }");
        }

        private static void GenerateRequestTypeRegistration(
            StringBuilder sb, 
            IEnumerable<(string GenericTypeName, string FunctionName)> requestTypes,
            HashSet<string> typeNameConflicts = null)
        {
            var requestTypesList = requestTypes.ToList();
            var requestTypesWithGeneric = requestTypesList.Where(rt => !string.IsNullOrEmpty(rt.GenericTypeName)).ToList();
            var requestTypesCount = requestTypesWithGeneric.Count;
            
            sb.AppendLine("    private static void RegisterRequestTypes()");
            sb.AppendLine("    {");
            
            if (requestTypesCount > 0)
            {
                sb.AppendLine($"      var requestTypes = new Dictionary<string, (string, Type)>({requestTypesCount});");
                
                foreach (var (genericTypeName, functionName) in requestTypesWithGeneric)
                {
                    // Use the simple type name if no conflicts exist, otherwise use full name
                    var typeName = GetTypeNameForGeneration(genericTypeName, typeNameConflicts);
                    
                    sb.AppendLine($"      requestTypes.TryAdd(\"{functionName}\", (typeof({typeName}).FullName, typeof({typeName})));");
                }
                
                sb.AppendLine($"      TickerFunctionProvider.RegisterRequestType(requestTypes, {requestTypesCount});");
            }
            
            sb.AppendLine("    }");
        }

        /// <summary>
        /// Gets the appropriate type name for code generation - simple name if no conflicts, full name if conflicts exist.
        /// </summary>
        private static string GetTypeNameForGeneration(string fullTypeName, HashSet<string> typeNameConflicts)
        {
            if (typeNameConflicts == null || typeNameConflicts.Count == 0)
                return fullTypeName;
                
            var simpleName = fullTypeName.Contains('.') ? 
                fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1) : 
                fullTypeName;
                
            // Use full name if there's a conflict with the simple name
            return typeNameConflicts.Contains(simpleName) ? fullTypeName : simpleName;
        }

        /// <summary>
        /// Generates the class closing braces.
        /// </summary>
        private static void GenerateClassFooter(StringBuilder sb)
        {
            sb.AppendLine("  }");
            sb.AppendLine("}");
        }
        
        #endregion
        
        #region Constructor Building Methods
        
        /// <summary>
        /// Builds constructor method calls for dependency injection.
        /// </summary>
        private static IEnumerable<string> BuildConstructorMethodCalls(
            IEnumerable<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
            Compilation compilation,
            string assemblyName)
        {
            var distinctClasses = methodPairs.Select(p => p.ClassDecl).Distinct();
            foreach (var classDeclaration in distinctClasses)
            {
                var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var constructorCall = BuildConstructorCall(classDeclaration, semanticModel, assemblyName);
                if (!string.IsNullOrEmpty(constructorCall))
                    yield return constructorCall;
            }
        }

        /// <summary>
        /// Builds a constructor call method for a specific class.
        /// Prioritizes constructors with [TickerQConstructor] attribute, then first public constructor.
        /// Skips static classes as they cannot be instantiated.
        /// </summary>
        private static string BuildConstructorCall(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel, string assemblyName)
        {
            // Check if class is static - static classes cannot be instantiated
            var isStaticClass = classDeclaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword));
            if (isStaticClass)
                return null; // Skip constructor generation for static classes
            var constructors = classDeclaration.Members.OfType<ConstructorDeclarationSyntax>().ToList();
            
            // First, look for a constructor with TickerQConstructor attribute using semantic analysis
            var tickerQConstructor = constructors.FirstOrDefault(c =>
            {
                var constructorSymbol = semanticModel.GetDeclaredSymbol(c);
                if (constructorSymbol == null) return false;
                
                return constructorSymbol.GetAttributes().Any(attr =>
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
            });

            // If no TickerQConstructor attribute found, use first public constructor
            var publicConstructor = tickerQConstructor ?? constructors
                .FirstOrDefault(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));

            var isPrimaryConstructor = classDeclaration.ParameterList?.Parameters.Count > 0;
            var parameters = isPrimaryConstructor ? classDeclaration.ParameterList.Parameters :
                publicConstructor?.ParameterList.Parameters ?? default;

            var sb = new StringBuilder(512); // Constructor methods are typically smaller
            
            // Use simple class name if in root namespace (due to using statement), otherwise use full name
            var fullClassName = SourceGeneratorUtilities.GetFullClassName(classDeclaration);
            var classNamespace = SourceGeneratorUtilities.GetNamespace(classDeclaration);
            var simpleClassName = classDeclaration.Identifier.Text;
            
            // Use simple name if in the same namespace as assembly (root namespace)
            var useSimpleName = classNamespace == assemblyName;
            var displayClassName = useSimpleName ? simpleClassName : fullClassName;
            var methodName = $"Create{fullClassName.Replace(".", "")}";
            
            sb.AppendLine($"    private static {displayClassName} {methodName}(IServiceProvider serviceProvider)");
            sb.AppendLine("    {");

            var arguments = new List<string>();
            foreach (var parameter in parameters)
            {
                var parameterName = SourceGeneratorUtilities.FirstLetterToLower(parameter.Identifier.Text);
                if (parameterName != "serviceProvider")
                {
                    if (parameter.Type != null)
                    {
                        // Get parameter symbol - handle both regular and primary constructors
                        var parameterSymbol = isPrimaryConstructor 
                            ? GetPrimaryConstructorParameterSymbol(classDeclaration, parameter, semanticModel)
                            : semanticModel.GetDeclaredSymbol(parameter);
                            
                        var serviceResolution = GenerateServiceResolution(parameter, parameterSymbol, semanticModel, parameterName);
                        sb.AppendLine(serviceResolution);
                    }
                }
                arguments.Add(parameterName);
            }

            sb.AppendLine($"        return new {displayClassName}({string.Join(", ", arguments)});");
            sb.AppendLine("    }");
            return sb.ToString();
        }

        /// <summary>
        /// Generates the appropriate service resolution code for a constructor parameter.
        /// </summary>
        private static string GenerateServiceResolution(
            ParameterSyntax parameter, 
            IParameterSymbol parameterSymbol, 
            SemanticModel semanticModel, 
            string parameterName)
        {
            var typeSymbol = ModelExtensions.GetSymbolInfo(semanticModel, parameter.Type).Symbol;
            var typeName = typeSymbol?.ToDisplayString() ?? parameter.Type.ToString();

            // Check for FromKeyedServicesAttribute (with various possible names)
            var keyedServiceAttribute = parameterSymbol?.GetAttributes()
                .FirstOrDefault(attr => 
                {
                    var name = attr.AttributeClass?.Name;
                    var fullName = attr.AttributeClass?.ToDisplayString();
                    return name == "FromKeyedServicesAttribute" || 
                           name == "FromKeyedServices" ||
                           fullName == "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute" ||
                           fullName?.EndsWith("FromKeyedServicesAttribute") == true;
                });

            if (keyedServiceAttribute != null)
            {
                var serviceKey = SourceGeneratorUtilities.GetServiceKey(keyedServiceAttribute);
                if (serviceKey != null)
                {
                    return $"        var {parameterName} = serviceProvider.GetKeyedService<{typeName}>({serviceKey});";
                }
            }

            // Default to regular service resolution
            return $"        var {parameterName} = serviceProvider.GetService<{typeName}>();";
        }

        /// <summary>
        /// Gets the parameter symbol for primary constructor parameters.
        /// </summary>
        private static IParameterSymbol GetPrimaryConstructorParameterSymbol(
            ClassDeclarationSyntax classDeclaration, 
            ParameterSyntax parameter, 
            SemanticModel semanticModel)
        {
            var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
            if (classSymbol?.Constructors.Length > 0)
            {
                var primaryConstructor = classSymbol.Constructors.FirstOrDefault(c => c.Parameters.Length > 0);
                if (primaryConstructor != null)
                {
                    var parameterName = parameter.Identifier.Text;
                    return primaryConstructor.Parameters.FirstOrDefault(p => p.Name == parameterName);
                }
            }
            return null;
        }
        
        #endregion

        // All utility methods moved to SourceGeneratorUtilities class
    }
}
