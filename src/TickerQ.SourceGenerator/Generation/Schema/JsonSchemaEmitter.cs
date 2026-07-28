using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace TickerQ.SourceGenerator.Generation.Schema
{
    /// <summary>
    /// Reflection-free, AOT-safe compile-time emitter that maps a CLR request type (as a Roslyn
    /// <see cref="ITypeSymbol"/>) to a JSON Schema 2020-12 document plus one deterministic default
    /// example. The wire shape modelled is that of <c>System.Text.Json</c> under this repository's
    /// default request options (see <c>TickerHelper.RequestJsonSerializerOptions</c>): PascalCase
    /// property names unless overridden by <c>[JsonPropertyName]</c>, numeric enums unless a string
    /// enum converter is declared, and unmapped members ignored unless disallowed.
    /// </summary>
    /// <remarks>
    /// When the wire shape cannot be inferred (custom converters, polymorphism, unsupported types) the
    /// emitter throws <see cref="UnsupportedSchemaException"/>; callers translate that to a diagnostic
    /// and emit a descriptor with no schema. A misleading schema is never produced.
    /// </remarks>
    internal sealed class JsonSchemaEmitter
    {
        private const string Ns = "System.Text.Json.Serialization.";
        private const string Da = "System.ComponentModel.DataAnnotations.";

        private static readonly SymbolDisplayFormat NameFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.None);

        // Ordered $defs registry (insertion order preserved for stable output; keys sorted at write time).
        private readonly List<KeyValuePair<string, string>> _defs = new List<KeyValuePair<string, string>>();
        private readonly Dictionary<ISymbol, string> _defNames = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        private readonly HashSet<string> _usedNames = new HashSet<string>(StringComparer.Ordinal);

        public static SchemaEmissionResult Emit(ITypeSymbol requestType)
        {
            try
            {
                var emitter = new JsonSchemaEmitter();
                var schema = emitter.EmitRoot(requestType);
                var example = new ExampleWriter().Build(requestType);
                return SchemaEmissionResult.ForSupported(schema, example);
            }
            catch (UnsupportedSchemaException ex)
            {
                return SchemaEmissionResult.ForUnsupported(ex.Reason, ex.ConverterType);
            }
        }

        private string EmitRoot(ITypeSymbol requestType)
        {
            var type = requestType;
            // The root request payload must itself be a serializable object; anything else cannot be an
            // object-rooted schema and is reported as unsupported.
            if (!(type is INamedTypeSymbol named) || !IsSchemaObject(type))
                throw new UnsupportedSchemaException(
                    $"request type '{DisplayName(type)}' is not a serializable object (an object-rooted schema is required)");

            RejectConverterAndPolymorphism(named);

            var members = BuildObjectMembers(named);
            members.Insert(0, Kv("$schema", SchemaJson.String("https://json-schema.org/draft/2020-12/schema")));
            if (_defs.Count > 0)
                members.Add(Kv("$defs", SchemaJson.Object(_defs)));

            return SchemaJson.Object(members);
        }

        // ---- object walking -------------------------------------------------

        private List<KeyValuePair<string, string>> BuildObjectMembers(INamedTypeSymbol type)
        {
            var members = new List<KeyValuePair<string, string>>();
            var properties = new List<KeyValuePair<string, string>>();
            var required = new List<string>();
            var wireNames = new HashSet<string>(StringComparer.Ordinal);
            ISymbol extensionData = null;

            foreach (var member in GetSerializableMembers(type))
            {
                if (IsJsonExtensionData(member.Symbol))
                {
                    if (extensionData != null)
                        throw new UnsupportedSchemaException(
                            $"type '{DisplayName(type)}' declares multiple [JsonExtensionData] members");
                    ValidateExtensionDataMember(type, member);
                    extensionData = member.Symbol;
                    continue;
                }

                RejectNonStrictNumberHandling(member.Symbol,
                    $"member '{DisplayName(type)}.{member.Symbol.Name}'");

                if (TryGetCustomConverter(member.Symbol, out var propertyConverter))
                    throw new UnsupportedSchemaException(
                        $"member '{DisplayName(type)}.{member.Symbol.Name}' uses a custom JSON converter that changes the wire shape",
                        propertyConverter);

                var wireName = WireName(member.Symbol);
                if (!wireNames.Add(wireName))
                    throw new UnsupportedSchemaException(
                        $"type '{DisplayName(type)}' contains duplicate JSON property name '{wireName}'");

                var node = EmitType(member.Type);
                ApplyValidation(node, member.Symbol, member.Type);
                ApplyIntegralBounds(node, member.Type);
                properties.Add(Kv(wireName, SchemaJson.Object(node)));
                if (IsRequired(member.Symbol))
                    required.Add(wireName);
            }

            members.Add(Kv("type", SchemaJson.String("object")));
            members.Add(Kv("properties", SchemaJson.Object(properties)));

            if (required.Count > 0)
            {
                required.Sort(StringComparer.Ordinal);
                members.Add(Kv("required", SchemaJson.Array(required.Select(SchemaJson.String))));
            }

            members.Add(Kv("additionalProperties",
                extensionData != null || AllowsUnmappedMembers(type) ? "true" : "false"));
            return members;
        }

        private string RefFor(INamedTypeSymbol type)
        {
            if (_defNames.TryGetValue(type, out var existing))
                return existing;

            // Register the name before building the body so a self/cyclic reference resolves to a $ref.
            var name = UniqueName(type);
            _defNames[type] = "#/$defs/" + name;

            RejectConverterAndPolymorphism(type);
            var body = SchemaJson.Object(BuildObjectMembers(type));
            _defs.Add(Kv(name, body));
            return "#/$defs/" + name;
        }

        // ---- type dispatch --------------------------------------------------

        private List<KeyValuePair<string, string>> EmitType(ITypeSymbol type)
        {
            var nullable = false;

            // Nullable<T> value types.
            if (type is INamedTypeSymbol nt && nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                type = nt.TypeArguments[0];
                nullable = true;
            }
            else if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
            {
                nullable = true;
            }

            // A converter on a scalar/object changes the wire shape and cannot be inferred (enums are
            // handled explicitly below and may legitimately carry a string-enum converter).
            if (type.TypeKind != TypeKind.Enum && TryGetCustomConverter(type, out var converter))
                throw new UnsupportedSchemaException(
                    $"type '{DisplayName(type)}' uses a custom JSON converter that changes the wire shape",
                    converter);

            switch (type.SpecialType)
            {
                case SpecialType.System_String:
                    return Scalar("string", nullable);
                case SpecialType.System_Char:
                    var character = Scalar("string", nullable);
                    character.Add(Kv("minLength", "1"));
                    character.Add(Kv("maxLength", "1"));
                    return character;
                case SpecialType.System_Boolean:
                    return Scalar("boolean", nullable);
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    var integer = Scalar("integer", nullable);
                    ApplyIntegralBounds(integer, type);
                    return integer;
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    return Scalar("number", nullable);
                case SpecialType.System_DateTime:
                    // DateTimeKind.Unspecified serializes without an RFC 3339 offset, so the
                    // date-time format assertion would reject valid System.Text.Json output.
                    return Scalar("string", nullable);
                case SpecialType.System_Object:
                    throw new UnsupportedSchemaException("System.Object payloads have no inferable wire shape");
            }

            if (type.TypeKind == TypeKind.Enum)
                return EnumNode((INamedTypeSymbol)type, nullable);

            switch (DisplayName(type))
            {
                case "System.Guid":
                    return Scalar("string", nullable, "uuid");
                case "System.DateTimeOffset":
                    return Scalar("string", nullable, "date-time");
                case "System.DateOnly":
                    return Scalar("string", nullable, "date");
                case "System.TimeOnly":
                    // STJ writes HH:mm:ss[.fffffff] without the offset required by RFC 3339 time.
                    return Scalar("string", nullable);
                case "System.TimeSpan":
                    // STJ writes the constant TimeSpan format (for example 00:00:01), not ISO 8601.
                    return Scalar("string", nullable);
                case "System.Uri":
                    return Scalar("string", nullable, "uri-reference");
            }

            // byte[] serializes as a base64 string.
            if (type is IArrayTypeSymbol byteArray && byteArray.ElementType.SpecialType == SpecialType.System_Byte)
            {
                var node = Scalar("string", nullable);
                node.Add(Kv("contentEncoding", SchemaJson.String("base64")));
                return node;
            }

            if (type is IArrayTypeSymbol array)
                return ArrayNode(array.ElementType, nullable);

            if (TryGetDictionaryValueType(type, out var valueType))
                return DictionaryNode(valueType, nullable);

            if (TryGetEnumerableElementType(type, out var elementType))
                return ArrayNode(elementType, nullable);

            if (type.TypeKind == TypeKind.Interface || type.IsAbstract)
                throw new UnsupportedSchemaException(
                    $"type '{DisplayName(type)}' is abstract/an interface; polymorphic contracts are not supported in contract version 1");

            if (type is INamedTypeSymbol objectType && IsSchemaObject(type))
                return RefNode(objectType, nullable);

            throw new UnsupportedSchemaException($"type '{DisplayName(type)}' has no inferable wire shape");
        }

        private List<KeyValuePair<string, string>> Scalar(string jsonType, bool nullable, string format = null)
        {
            var node = new List<KeyValuePair<string, string>>();
            node.Add(Kv("type", TypeKeyword(jsonType, nullable)));
            if (format != null)
                node.Add(Kv("format", SchemaJson.String(format)));
            return node;
        }

        private List<KeyValuePair<string, string>> ArrayNode(ITypeSymbol elementType, bool nullable)
        {
            var node = new List<KeyValuePair<string, string>>();
            node.Add(Kv("type", TypeKeyword("array", nullable)));
            node.Add(Kv("items", SchemaJson.Object(EmitType(elementType))));
            return node;
        }

        private List<KeyValuePair<string, string>> DictionaryNode(ITypeSymbol valueType, bool nullable)
        {
            var node = new List<KeyValuePair<string, string>>();
            node.Add(Kv("type", TypeKeyword("object", nullable)));
            node.Add(Kv("additionalProperties", SchemaJson.Object(EmitType(valueType))));
            return node;
        }

        private List<KeyValuePair<string, string>> RefNode(INamedTypeSymbol type, bool nullable)
        {
            var pointer = RefFor(type);
            var node = new List<KeyValuePair<string, string>>();
            if (!nullable)
            {
                node.Add(Kv("$ref", SchemaJson.String(pointer)));
                return node;
            }

            // A $ref cannot carry "type":"null"; union it explicitly.
            var reference = "{" + SchemaJson.String("$ref") + ":" + SchemaJson.String(pointer) + "}";
            var nullNode = "{" + SchemaJson.String("type") + ":" + SchemaJson.String("null") + "}";
            node.Add(Kv("anyOf", SchemaJson.Array(new[] { reference, nullNode })));
            return node;
        }

        private List<KeyValuePair<string, string>> EnumNode(INamedTypeSymbol enumType, bool nullable)
        {
            var stringEnum = IsStringEnum(enumType);
            if (!stringEnum && TryGetCustomConverter(enumType, out var converter))
                throw new UnsupportedSchemaException(
                    $"enum type '{DisplayName(enumType)}' uses a custom JSON converter that changes the wire shape",
                    converter);

            // Numeric enums are open integer contracts in System.Text.Json: undeclared underlying
            // values round-trip and therefore must not be constrained to the declared constants.
            if (!stringEnum)
            {
                var node = Scalar("integer", nullable);
                ApplyIntegralBounds(node, enumType);
                return node;
            }

            // JsonStringEnumConverter reads names case-insensitively and, for [Flags], accepts
            // comma-separated combinations, while still rejecting arbitrary strings. Draft 2020-12
            // has no portable way to express that exact grammar. A closed enum would be too narrow;
            // an unrestricted string would be too broad. Suppress rather than publish a false contract.
            throw new UnsupportedSchemaException(
                $"string enum type '{DisplayName(enumType)}' uses JsonStringEnumConverter semantics that cannot be represented exactly in contract version 1");
        }

        private static string TypeKeyword(string jsonType, bool nullable)
            => nullable
                ? SchemaJson.Array(new[] { SchemaJson.String(jsonType), SchemaJson.String("null") })
                : SchemaJson.String(jsonType);

        // ---- validation attributes -----------------------------------------

        private void ApplyValidation(List<KeyValuePair<string, string>> node, ISymbol member, ITypeSymbol memberType)
        {
            foreach (var attr in member.GetAttributes())
            {
                var name = attr.AttributeClass?.ToDisplayString();
                if (name == null || !name.StartsWith(Da, StringComparison.Ordinal)) continue;

                switch (name.Substring(Da.Length))
                {
                    case "MinLengthAttribute":
                        AddLength(node, memberType, "min", FirstInt(attr));
                        break;
                    case "MaxLengthAttribute":
                        AddLength(node, memberType, "max", FirstInt(attr));
                        break;
                    case "StringLengthAttribute":
                        var max = FirstInt(attr);
                        if (max.HasValue) SetInt(node, "maxLength", max.Value);
                        var min = NamedInt(attr, "MinimumLength");
                        if (min.HasValue && min.Value > 0) SetInt(node, "minLength", min.Value);
                        break;
                    case "RangeAttribute":
                        ApplyRange(node, attr);
                        break;
                    case "RegularExpressionAttribute":
                        var pattern = FirstString(attr);
                        if (pattern != null)
                        {
                            if (!IsSafeJsonSchemaPattern(pattern))
                                throw new UnsupportedSchemaException(
                                    $"regular expression on member '{DisplayName(member.ContainingType)}.{member.Name}' uses syntax outside the proven .NET/JSON Schema common subset");
                            SetRaw(node, "pattern", SchemaJson.String(
                                "^(?:" + pattern.Substring(1, pattern.Length - 2) + @")(?![\s\S])"));
                        }
                        break;
                    case "UrlAttribute":
                        SetRaw(node, "format", SchemaJson.String("uri"));
                        break;
                    case "EmailAddressAttribute":
                        SetRaw(node, "format", SchemaJson.String("email"));
                        break;
                }
            }
        }

        private static bool IsSafeJsonSchemaPattern(string pattern)
        {
            // DataAnnotations requires the full value to match, whereas JSON Schema `pattern`
            // searches for a matching substring. Requiring explicit anchors preserves that semantic.
            if (pattern.Length < 2 || pattern[0] != '^' || pattern[pattern.Length - 1] != '$')
                return false;

            try
            {
                // This catches malformed grouping/classes/quantifiers before a bad pattern reaches
                // a Draft validator. The scanner below then excludes .NET-only semantics that this
                // parser would otherwise accept.
                _ = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.ECMAScript);
            }
            catch (ArgumentException)
            {
                return false;
            }

            for (var i = 0; i < pattern.Length; i++)
            {
                var ch = pattern[i];
                if (char.IsControl(ch) || char.IsSurrogate(ch)) return false;
                if (ch == '.') return false;
                if (ch == '(' && i + 1 < pattern.Length && pattern[i + 1] == '?') return false;
                if (ch == '-' && i + 1 < pattern.Length && pattern[i + 1] == '[') return false;
                if (ch == '^' && i != 0) return false;
                if (ch == '$' && i != pattern.Length - 1) return false;
                if (ch != '\\') continue;

                if (++i >= pattern.Length) return false;
                const string portableEscapedLiterals = @"\\.^$|?*+()[]{}-/";
                if (portableEscapedLiterals.IndexOf(pattern[i]) < 0) return false;
            }

            return true;
        }

        private static void AddLength(List<KeyValuePair<string, string>> node, ITypeSymbol memberType,
            string prefix, int? value)
        {
            if (!value.HasValue) return;

            if (memberType.SpecialType == SpecialType.System_String)
            {
                SetInt(node, prefix + "Length", value.Value);
                return;
            }

            // byte[] is encoded as a base64 string. Its CLR byte count is not the encoded string
            // length, so minLength/maxLength would state a false wire constraint.
            if (memberType is IArrayTypeSymbol bytes
                && bytes.ElementType.SpecialType == SpecialType.System_Byte)
                return;

            if (TryGetDictionaryValueType(memberType, out _))
            {
                SetInt(node, prefix + "Properties", value.Value);
                return;
            }

            if (memberType is IArrayTypeSymbol || TryGetEnumerableElementType(memberType, out _))
                SetInt(node, prefix + "Items", value.Value);
        }

        private void ApplyRange(List<KeyValuePair<string, string>> node, AttributeData attr)
        {
            string min;
            string max;
            if (attr.ConstructorArguments.Length == 3
                && attr.ConstructorArguments[0].Value is ITypeSymbol operandType
                && attr.ConstructorArguments[1].Value is string minText
                && attr.ConstructorArguments[2].Value is string maxText)
            {
                if (!TryParseInvariantNumber(operandType, minText, out min)
                    || !TryParseInvariantNumber(operandType, maxText, out max))
                    throw new UnsupportedSchemaException(
                        $"[Range] bounds for '{DisplayName(operandType)}' are not supported invariant JSON numbers");
            }
            else if (attr.ConstructorArguments.Length == 2)
            {
                min = ToNumberLiteral(attr.ConstructorArguments[0].Value);
                max = ToNumberLiteral(attr.ConstructorArguments[1].Value);
                if (min == null || max == null)
                    throw new UnsupportedSchemaException("[Range] bounds are not supported JSON numbers");
            }
            else
            {
                throw new UnsupportedSchemaException("[Range] constructor shape is not supported");
            }

            SetRaw(node, NamedBool(attr, "MinimumIsExclusive") == true ? "exclusiveMinimum" : "minimum", min);
            SetRaw(node, NamedBool(attr, "MaximumIsExclusive") == true ? "exclusiveMaximum" : "maximum", max);
        }

        // ---- serializable member model -------------------------------------

        internal sealed class SerializableMember
        {
            public SerializableMember(ISymbol symbol, ITypeSymbol type)
            {
                Symbol = symbol;
                Type = type;
            }

            public ISymbol Symbol { get; }
            public ITypeSymbol Type { get; }
        }

        internal static IEnumerable<SerializableMember> GetSerializableMembers(INamedTypeSymbol type)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var members = new List<SerializableMember>();
            for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                foreach (var symbol in current.GetMembers())
                {
                    ITypeSymbol memberType;
                    if (symbol is IPropertySymbol prop)
                    {
                        if (prop.IsStatic || prop.IsIndexer) continue;
                        var explicitlyIncluded = HasAttribute(prop, Ns + "JsonIncludeAttribute");
                        if (!IsAccessibleFromGeneratedContext(prop.DeclaredAccessibility))
                        {
                            if (explicitlyIncluded)
                                throw InaccessibleJsonInclude(type, prop);
                            continue;
                        }
                        if (explicitlyIncluded
                            && ((prop.GetMethod != null && !IsAccessibleFromGeneratedContext(prop.GetMethod.DeclaredAccessibility))
                                || (prop.SetMethod != null && !IsAccessibleFromGeneratedContext(prop.SetMethod.DeclaredAccessibility))))
                            throw InaccessibleJsonInclude(type, prop);
                        var hasPublicAccessor = prop.GetMethod?.DeclaredAccessibility == Accessibility.Public
                            || prop.SetMethod?.DeclaredAccessibility == Accessibility.Public;
                        if (!hasPublicAccessor
                            && !(explicitlyIncluded && (prop.GetMethod != null || prop.SetMethod != null)))
                            continue;
                        memberType = prop.Type;
                    }
                    else if (symbol is IFieldSymbol field)
                    {
                        // IncludeFields is a runtime-global option and cannot be inferred here. Only the
                        // explicit per-member [JsonInclude] contract is safe to model.
                        if (field.IsStatic) continue;
                        var explicitlyIncluded = HasAttribute(field, Ns + "JsonIncludeAttribute");
                        if (!explicitlyIncluded) continue;
                        if (!IsAccessibleFromGeneratedContext(field.DeclaredAccessibility))
                            throw InaccessibleJsonInclude(type, field);
                        memberType = field.Type;
                    }
                    else
                    {
                        continue;
                    }

                    if (IsJsonIgnored(symbol)) continue;
                    if (!seen.Add(symbol.Name)) continue;
                    members.Add(new SerializableMember(symbol, memberType));
                }
            }

            members.Sort((left, right) =>
            {
                var byWireName = string.CompareOrdinal(WireName(left.Symbol), WireName(right.Symbol));
                if (byWireName != 0) return byWireName;
                var byClrName = string.CompareOrdinal(left.Symbol.Name, right.Symbol.Name);
                if (byClrName != 0) return byClrName;
                return string.CompareOrdinal(
                    left.Symbol.ContainingType?.ToDisplayString(NameFormat),
                    right.Symbol.ContainingType?.ToDisplayString(NameFormat));
            });
            return members;
        }

        private static bool IsAccessibleFromGeneratedContext(Accessibility accessibility)
            => accessibility == Accessibility.Public
                || accessibility == Accessibility.Internal
                || accessibility == Accessibility.ProtectedOrInternal;

        private static UnsupportedSchemaException InaccessibleJsonInclude(
            INamedTypeSymbol containingType,
            ISymbol member)
            => new UnsupportedSchemaException(
                $"[JsonInclude] member '{DisplayName(containingType)}.{member.Name}' is not accessible to generated AOT serializer metadata");

        private static void ApplyIntegralBounds(
            List<KeyValuePair<string, string>> node,
            ITypeSymbol type)
        {
            if (type is INamedTypeSymbol nullable
                && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                type = nullable.TypeArguments[0];

            var specialType = type.SpecialType;
            if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            {
                if (IsStringEnum(enumType)) return;
                specialType = enumType.EnumUnderlyingType?.SpecialType ?? SpecialType.None;
            }

            if (!TryGetIntegralBounds(specialType, out var minimum, out var maximum))
                return;

            TightenLowerBound(node, minimum);
            TightenUpperBound(node, maximum);
        }

        internal static bool TryGetIntegralBounds(
            SpecialType type,
            out decimal minimum,
            out decimal maximum)
        {
            switch (type)
            {
                case SpecialType.System_SByte: minimum = sbyte.MinValue; maximum = sbyte.MaxValue; return true;
                case SpecialType.System_Byte: minimum = byte.MinValue; maximum = byte.MaxValue; return true;
                case SpecialType.System_Int16: minimum = short.MinValue; maximum = short.MaxValue; return true;
                case SpecialType.System_UInt16: minimum = ushort.MinValue; maximum = ushort.MaxValue; return true;
                case SpecialType.System_Int32: minimum = int.MinValue; maximum = int.MaxValue; return true;
                case SpecialType.System_UInt32: minimum = uint.MinValue; maximum = uint.MaxValue; return true;
                case SpecialType.System_Int64: minimum = long.MinValue; maximum = long.MaxValue; return true;
                case SpecialType.System_UInt64: minimum = ulong.MinValue; maximum = ulong.MaxValue; return true;
                default: minimum = 0m; maximum = 0m; return false;
            }
        }

        private static void TightenLowerBound(List<KeyValuePair<string, string>> node, decimal minimum)
        {
            for (var i = 0; i < node.Count; i++)
            {
                if (node[i].Key != "minimum" && node[i].Key != "exclusiveMinimum") continue;
                if (!decimal.TryParse(node[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var existing))
                    throw new UnsupportedSchemaException("enum range lower bound is outside the supported integral range");
                if (existing >= minimum) return;
                node.RemoveAt(i);
                break;
            }
            SetRaw(node, "minimum", minimum.ToString(CultureInfo.InvariantCulture));
        }

        private static void TightenUpperBound(List<KeyValuePair<string, string>> node, decimal maximum)
        {
            for (var i = 0; i < node.Count; i++)
            {
                if (node[i].Key != "maximum" && node[i].Key != "exclusiveMaximum") continue;
                if (!decimal.TryParse(node[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var existing))
                    throw new UnsupportedSchemaException("enum range upper bound is outside the supported integral range");
                if (existing <= maximum) return;
                node.RemoveAt(i);
                break;
            }
            SetRaw(node, "maximum", maximum.ToString(CultureInfo.InvariantCulture));
        }

        internal static string WireName(IPropertySymbol prop) => WireName((ISymbol)prop);

        internal static string WireName(ISymbol member)
        {
            foreach (var attr in member.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == Ns + "JsonPropertyNameAttribute"
                    && attr.ConstructorArguments.Length == 1
                    && attr.ConstructorArguments[0].Value is string name)
                    return name;
            }
            return member.Name;
        }

        internal static bool IsRequired(ISymbol member)
        {
            if (member is IPropertySymbol prop && prop.IsRequired) return true;
            if (member is IFieldSymbol field && field.IsRequired) return true;
            foreach (var attr in member.GetAttributes())
            {
                var name = attr.AttributeClass?.ToDisplayString();
                if (name == Da + "RequiredAttribute" || name == Ns + "JsonRequiredAttribute")
                    return true;
            }
            return false;
        }

        private static bool IsJsonIgnored(ISymbol member)
        {
            foreach (var attr in member.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != Ns + "JsonIgnoreAttribute") continue;

                // JsonIgnore with no Condition, or Condition == Always (1), removes the member entirely.
                var condition = attr.NamedArguments
                    .Where(n => n.Key == "Condition")
                    .Select(n => (int?)Convert.ToInt32(n.Value.Value, CultureInfo.InvariantCulture))
                    .FirstOrDefault();
                if (!condition.HasValue || condition.Value == 1) return true;
            }
            return false;
        }

        internal static bool IsJsonExtensionData(ISymbol member)
            => member.GetAttributes().Any(attr =>
                attr.AttributeClass?.ToDisplayString() == Ns + "JsonExtensionDataAttribute");

        private static void ValidateExtensionDataMember(INamedTypeSymbol containingType, SerializableMember member)
        {
            var propertyType = DisplayName(member.Type);
            if (propertyType == "System.Text.Json.Nodes.JsonObject") return;

            if (TryGetDictionaryTypes(member.Type, out var keyType, out var valueType)
                && keyType.SpecialType == SpecialType.System_String)
            {
                var valueName = DisplayName(valueType);
                if (valueType.SpecialType == SpecialType.System_Object
                    || valueName == "System.Text.Json.JsonElement")
                    return;
            }

            throw new UnsupportedSchemaException(
                $"[JsonExtensionData] member '{DisplayName(containingType)}.{member.Symbol.Name}' must be JsonObject or a string-keyed dictionary of object/JsonElement");
        }

        private static bool HasAttribute(ISymbol symbol, string attributeName)
            => symbol.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == attributeName);

        private static bool AllowsUnmappedMembers(INamedTypeSymbol type)
        {
            foreach (var attr in type.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != Ns + "JsonUnmappedMemberHandlingAttribute") continue;
                if (attr.ConstructorArguments.Length == 1
                    && Convert.ToInt32(attr.ConstructorArguments[0].Value, CultureInfo.InvariantCulture) == 1)
                    return false; // JsonUnmappedMemberHandling.Disallow
            }
            return true;
        }

        // ---- converter / polymorphism guards -------------------------------

        private static void RejectConverterAndPolymorphism(INamedTypeSymbol type)
        {
            RejectNonStrictNumberHandling(type, $"type '{DisplayName(type)}'");

            if (TryGetCustomConverter(type, out var converter))
                throw new UnsupportedSchemaException(
                    $"type '{DisplayName(type)}' uses a custom JSON converter that changes the wire shape", converter);

            foreach (var attr in type.GetAttributes())
            {
                var name = attr.AttributeClass?.ToDisplayString();
                if (name == Ns + "JsonPolymorphicAttribute" || name == Ns + "JsonDerivedTypeAttribute")
                    throw new UnsupportedSchemaException(
                        $"type '{DisplayName(type)}' declares polymorphism, which is not supported in contract version 1");
            }
        }

        private static void RejectNonStrictNumberHandling(ISymbol symbol, string description)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != Ns + "JsonNumberHandlingAttribute") continue;
                if (attr.ConstructorArguments.Length != 1
                    || attr.ConstructorArguments[0].Value == null
                    || Convert.ToInt32(attr.ConstructorArguments[0].Value, CultureInfo.InvariantCulture) != 0)
                    throw new UnsupportedSchemaException(
                        $"{description} uses non-Strict [JsonNumberHandling], whose numeric wire shape is not modelled");
            }
        }

        private static bool TryGetCustomConverter(ITypeSymbol type, out string converter)
        {
            converter = null;
            foreach (var attr in type.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != Ns + "JsonConverterAttribute") continue;
                if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is ITypeSymbol t)
                    converter = DisplayName(t);
                else
                    converter = "custom converter";
                return true;
            }
            return false;
        }

        private static bool TryGetCustomConverter(ISymbol symbol, out string converter)
        {
            converter = null;
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != Ns + "JsonConverterAttribute") continue;
                if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is ITypeSymbol t)
                    converter = DisplayName(t);
                else
                    converter = "custom converter";
                return true;
            }
            return false;
        }

        internal static bool IsStringEnum(INamedTypeSymbol enumType)
        {
            foreach (var attr in enumType.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != Ns + "JsonConverterAttribute") continue;
                if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is ITypeSymbol t)
                {
                    var n = t.OriginalDefinition.ToDisplayString(NameFormat);
                    if (n == Ns + "JsonStringEnumConverter") return true;
                }
            }
            return false;
        }

        internal static string EnumMemberName(IFieldSymbol member)
        {
            foreach (var attr in member.GetAttributes())
            {
                // .NET 9 [JsonStringEnumMemberName] overrides the wire name.
                if (attr.AttributeClass?.ToDisplayString() == Ns + "JsonStringEnumMemberNameAttribute"
                    && attr.ConstructorArguments.Length == 1
                    && attr.ConstructorArguments[0].Value is string name)
                    return name;
            }
            return member.Name;
        }

        internal static string EnumValueLiteral(IFieldSymbol member)
        {
            if (member.ConstantValue is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            return Convert.ToString(member.ConstantValue, CultureInfo.InvariantCulture);
        }

        // ---- collection detection ------------------------------------------

        internal static bool TryGetDictionaryValueType(ITypeSymbol type, out ITypeSymbol valueType)
        {
            return TryGetDictionaryTypes(type, out _, out valueType);
        }

        private static bool TryGetDictionaryTypes(
            ITypeSymbol type, out ITypeSymbol keyType, out ITypeSymbol valueType)
        {
            keyType = null;
            valueType = null;
            var candidates = InterfacesOf(type);
            foreach (var i in candidates)
            {
                if (!(i is INamedTypeSymbol named) || named.TypeArguments.Length != 2) continue;
                var def = named.ConstructedFrom.ToDisplayString(NameFormat);
                if (def == "System.Collections.Generic.IDictionary"
                    || def == "System.Collections.Generic.IReadOnlyDictionary")
                {
                    keyType = named.TypeArguments[0];
                    valueType = named.TypeArguments[1];
                    return true;
                }
            }
            return false;
        }

        internal static bool TryGetEnumerableElementType(ITypeSymbol type, out ITypeSymbol elementType)
        {
            elementType = null;
            if (type.SpecialType == SpecialType.System_String) return false;

            foreach (var i in InterfacesOf(type))
            {
                if (!(i is INamedTypeSymbol named) || named.TypeArguments.Length != 1) continue;
                if (named.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                    || named.ConstructedFrom.ToDisplayString(NameFormat) == "System.Collections.Generic.IEnumerable")
                {
                    elementType = named.TypeArguments[0];
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<INamedTypeSymbol> InterfacesOf(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol self && self.TypeKind == TypeKind.Interface)
                yield return self;
            foreach (var i in type.AllInterfaces)
                yield return i;
        }

        internal static bool IsSchemaObject(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named)) return false;
            if (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Struct) return false;
            if (named.SpecialType != SpecialType.None) return false;
            if (named.TypeKind == TypeKind.Enum) return false;
            return true;
        }

        // ---- helpers --------------------------------------------------------

        private string UniqueName(INamedTypeSymbol type)
        {
            var baseName = type.Name;
            var name = baseName;
            var i = 2;
            while (!_usedNames.Add(name))
                name = baseName + (i++).ToString(CultureInfo.InvariantCulture);
            return name;
        }

        private static string DisplayName(ITypeSymbol type) => type.ToDisplayString(NameFormat);

        private static KeyValuePair<string, string> Kv(string key, string value)
            => new KeyValuePair<string, string>(key, value);

        private static void SetInt(List<KeyValuePair<string, string>> node, string key, int value)
            => SetRaw(node, key, value.ToString(CultureInfo.InvariantCulture));

        private static void SetRaw(List<KeyValuePair<string, string>> node, string key, string rawJson)
        {
            for (var i = 0; i < node.Count; i++)
            {
                if (node[i].Key == key)
                {
                    node[i] = Kv(key, rawJson);
                    return;
                }
            }
            node.Add(Kv(key, rawJson));
        }

        private static int? FirstInt(AttributeData attr)
            => attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value != null
                ? (int?)Convert.ToInt32(attr.ConstructorArguments[0].Value, CultureInfo.InvariantCulture)
                : null;

        private static string FirstString(AttributeData attr)
            => attr.ConstructorArguments.Length >= 1 ? attr.ConstructorArguments[0].Value as string : null;

        private static int? NamedInt(AttributeData attr, string name)
        {
            foreach (var n in attr.NamedArguments)
                if (n.Key == name && n.Value.Value != null)
                    return Convert.ToInt32(n.Value.Value, CultureInfo.InvariantCulture);
            return null;
        }

        private static bool? NamedBool(AttributeData attr, string name)
        {
            foreach (var n in attr.NamedArguments)
                if (n.Key == name && n.Value.Value != null)
                    return Convert.ToBoolean(n.Value.Value, CultureInfo.InvariantCulture);
            return null;
        }

        private static string ToNumberLiteral(object value)
        {
            if (value is sbyte i8) return i8.ToString(CultureInfo.InvariantCulture);
            if (value is byte u8) return u8.ToString(CultureInfo.InvariantCulture);
            if (value is short i16) return i16.ToString(CultureInfo.InvariantCulture);
            if (value is ushort u16) return u16.ToString(CultureInfo.InvariantCulture);
            if (value is int i32) return i32.ToString(CultureInfo.InvariantCulture);
            if (value is uint u32) return u32.ToString(CultureInfo.InvariantCulture);
            if (value is long i64) return i64.ToString(CultureInfo.InvariantCulture);
            if (value is ulong u64) return u64.ToString(CultureInfo.InvariantCulture);
            if (value is decimal dec) return dec.ToString("G29", CultureInfo.InvariantCulture);
            if (value is float single && !float.IsNaN(single) && !float.IsInfinity(single))
                return single.ToString("R", CultureInfo.InvariantCulture);
            if (value is double dbl && !double.IsNaN(dbl) && !double.IsInfinity(dbl))
                return dbl.ToString("R", CultureInfo.InvariantCulture);
            return null;
        }

        private static bool TryParseInvariantNumber(ITypeSymbol type, string text, out string literal)
        {
            literal = null;
            const NumberStyles integer = NumberStyles.Integer;
            const NumberStyles real = NumberStyles.Float;

            switch (type.SpecialType)
            {
                case SpecialType.System_SByte:
                    if (sbyte.TryParse(text, integer, CultureInfo.InvariantCulture, out var i8))
                        literal = i8.ToString(CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_Byte:
                    if (byte.TryParse(text, integer, CultureInfo.InvariantCulture, out var u8))
                        literal = u8.ToString(CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_Int16:
                    if (short.TryParse(text, integer, CultureInfo.InvariantCulture, out var i16))
                        literal = i16.ToString(CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_UInt16:
                    if (ushort.TryParse(text, integer, CultureInfo.InvariantCulture, out var u16))
                        literal = u16.ToString(CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_Int32:
                    if (int.TryParse(text, integer, CultureInfo.InvariantCulture, out var i32))
                        literal = i32.ToString(CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_UInt32:
                    if (uint.TryParse(text, integer, CultureInfo.InvariantCulture, out var u32))
                        literal = u32.ToString(CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_Int64:
                    if (long.TryParse(text, integer, CultureInfo.InvariantCulture, out var i64))
                        literal = i64.ToString(CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_UInt64:
                    if (ulong.TryParse(text, integer, CultureInfo.InvariantCulture, out var u64))
                        literal = u64.ToString(CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_Decimal:
                    if (decimal.TryParse(text, real, CultureInfo.InvariantCulture, out var dec))
                        literal = dec.ToString("G29", CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_Single:
                    if (float.TryParse(text, real, CultureInfo.InvariantCulture, out var single)
                        && !float.IsNaN(single) && !float.IsInfinity(single))
                        literal = single.ToString("R", CultureInfo.InvariantCulture);
                    break;
                case SpecialType.System_Double:
                    if (double.TryParse(text, real, CultureInfo.InvariantCulture, out var dbl)
                        && !double.IsNaN(dbl) && !double.IsInfinity(dbl))
                        literal = dbl.ToString("R", CultureInfo.InvariantCulture);
                    break;
            }

            return literal != null;
        }
    }
}
