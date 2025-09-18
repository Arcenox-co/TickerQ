using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TickerQ.SourceGenerator.Utilities
{
    /// <summary>
    /// Utility methods for source generation operations.
    /// </summary>
    internal static class SourceGeneratorUtilities
    {
        /// <summary>
        /// Gets the full class name including namespace.
        /// </summary>
        public static string GetFullClassName(ClassDeclarationSyntax classDeclaration)
        {
            var namespaceName = GetNamespace(classDeclaration);
            return string.IsNullOrEmpty(namespaceName) 
                ? classDeclaration.Identifier.Text 
                : $"{namespaceName}.{classDeclaration.Identifier.Text}";
        }

        /// <summary>
        /// Gets the namespace of a class declaration.
        /// </summary>
        public static string GetNamespace(ClassDeclarationSyntax classDeclaration)
        {
            var namespaceDeclaration = classDeclaration.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (namespaceDeclaration != null)
            {
                return namespaceDeclaration.Name.ToString();
            }

            // Check for file-scoped namespace
            var fileScopedNamespace = classDeclaration.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
            return fileScopedNamespace?.Name.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Converts the first letter of a string to lowercase.
        /// </summary>
        public static string FirstLetterToLower(string input)
        {
            if (string.IsNullOrEmpty(input) || char.IsLower(input[0]))
                return input;

            return char.ToLowerInvariant(input[0]) + input.Substring(1);
        }

        /// <summary>
        /// Determines if a method is awaitable (returns Task or Task<T>).
        /// </summary>
        public static bool IsMethodAwaitable(MethodDeclarationSyntax methodDeclaration)
        {
            var returnType = methodDeclaration.ReturnType.ToString();
            return returnType.StartsWith("Task", StringComparison.Ordinal);
        }

        /// <summary>
        /// Formats the generated code using Roslyn's syntax tree formatting.
        /// </summary>
        public static string FormatCode(string code)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();
                var formatted = root.NormalizeWhitespace();
                return formatted.ToFullString();
            }
            catch
            {
                // If formatting fails, return the original code
                return code;
            }
        }

        /// <summary>
        /// Gets the service key from a FromKeyedServicesAttribute.
        /// </summary>
        public static string GetServiceKey(AttributeData keyedServiceAttribute)
        {
            if (keyedServiceAttribute.ConstructorArguments.Length > 0)
            {
                var keyArg = keyedServiceAttribute.ConstructorArguments[0];
                if (keyArg.Value is string stringKey)
                {
                    return $"\"{stringKey}\"";
                }
                else if (keyArg.Value != null)
                {
                    return FormatServiceKeyValue(keyArg.Value, keyArg.Type);
                }
            }
            return null;
        }

        /// <summary>
        /// Formats a service key value for C# code generation.
        /// </summary>
        private static string FormatServiceKeyValue(object value, ITypeSymbol type)
        {
            return value switch
            {
                double d => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture) + "D",
                float f => f.ToString("G", System.Globalization.CultureInfo.InvariantCulture) + "F",
                decimal m => m.ToString("G", System.Globalization.CultureInfo.InvariantCulture) + "M",
                long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
                uint ui => ui.ToString(System.Globalization.CultureInfo.InvariantCulture) + "U",
                ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
                char c => $"'{c}'",
                bool b => b ? "true" : "false",
                _ => value.ToString(),
            };
        }

        /// <summary>
        /// Creates type aliases for complex nested types and returns a dictionary of full type name to alias.
        /// </summary>
        public static Dictionary<string, string> CreateTypeAliases(IEnumerable<string> typeNames, string targetNamespace = null)
        {
            var aliases = new Dictionary<string, string>();
            var usedAliases = new HashSet<string>();

            foreach (var typeName in typeNames.Where(t => !string.IsNullOrEmpty(t)))
            {
                // Create aliases for types that:
                // 1. Have at least one dot (are qualified)
                // 2. Are not in the same namespace as the target (to avoid conflicts)
                if (typeName.Contains('.') && ShouldCreateAlias(typeName, targetNamespace))
                {
                    var alias = GenerateTypeAlias(typeName, usedAliases);
                    aliases[typeName] = alias;
                    usedAliases.Add(alias);
                }
            }

            return aliases;
        }

        /// <summary>
        /// Determines if a type alias should be created for the given type name.
        /// </summary>
        private static bool ShouldCreateAlias(string typeName, string targetNamespace)
        {
            // Create aliases for all qualified types to ensure clean, consistent code
            // This includes both nested types and single-level qualified types
            return true;
        }

        /// <summary>
        /// Generates a unique type alias for a complex type name.
        /// </summary>
        private static string GenerateTypeAlias(string fullTypeName, HashSet<string> usedAliases)
        {
            // Extract the simple type name from the end
            var parts = fullTypeName.Split('.');
            var simpleName = parts[^1]; // Last part

            // If the simple name is unique, use it
            if (!usedAliases.Contains(simpleName))
            {
                return simpleName;
            }

            // If not unique, try using just the parent namespace first (cleaner)
            if (parts.Length > 1)
            {
                var parentName = parts[^2];
                var parentAlias = $"{parentName}Alias";
                if (!usedAliases.Contains(parentAlias))
                {
                    return parentAlias;
                }
                
                // If parent name is also taken, combine parent + simple name
                var combinedName = $"{parentName}{simpleName}";
                if (!usedAliases.Contains(combinedName))
                {
                    return combinedName;
                }
            }

            // If still not unique, add a number suffix
            var counter = 1;
            string candidate;
            do
            {
                candidate = $"{simpleName}{counter}";
                counter++;
            } while (usedAliases.Contains(candidate));

            return candidate;
        }

        /// <summary>
        /// Extracts the namespace from a fully qualified type name.
        /// </summary>
        private static string ExtractNamespaceFromTypeName(string fullTypeName)
        {
            var lastDotIndex = fullTypeName.LastIndexOf('.');
            return lastDotIndex > 0 ? fullTypeName.Substring(0, lastDotIndex) : string.Empty;
        }
    }

    /// <summary>
    /// Extension methods for .NET Standard 2.0 compatibility.
    /// </summary>
    internal static class NetStandardExtensions
    {
        /// <summary>
        /// Deconstruct extension for KeyValuePair to enable tuple deconstruction in .NET Standard 2.0.
        /// </summary>
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value)
        {
            key = kvp.Key;
            value = kvp.Value;
        }
    }
}
