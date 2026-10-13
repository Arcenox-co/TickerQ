using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace TickerQ.SourceGenerator.Generation.Schema
{
    /// <summary>
    /// Produces one deterministic example JSON value that matches the emitted schema shape.
    /// Optional properties are omitted when no safe value can be constructed. If a required value
    /// cannot be constructed without lying about the contract, generation is reported as unsupported.
    /// </summary>
    internal sealed class ExampleWriter
    {
        private const string DataAnnotations = "System.ComponentModel.DataAnnotations.";
        private const int MaxExampleStringLength = 1024;
        private const int MaxExampleCollectionItems = 256;
        private const int MaxExampleJsonLength = 16384;

        private readonly HashSet<ISymbol> _path = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        public string Build(ITypeSymbol type)
        {
            var example = Value(type);
            if (example.Length > MaxExampleJsonLength)
                throw new UnsupportedSchemaException(
                    $"deterministic example exceeds the {MaxExampleJsonLength}-character example budget");
            return example;
        }

        private string Value(ITypeSymbol type)
        {
            type = UnwrapNullable(type, out _);

            switch (type.SpecialType)
            {
                case SpecialType.System_String:
                    return SchemaJson.String("string");
                case SpecialType.System_Char:
                    return SchemaJson.String("a");
                case SpecialType.System_Boolean:
                    return "false";
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    return "0";
                case SpecialType.System_DateTime:
                    return SchemaJson.String("2000-01-01T00:00:00Z");
            }

            if (type.TypeKind == TypeKind.Enum)
                return EnumExample((INamedTypeSymbol)type);

            switch (type.ToDisplayString(FullName))
            {
                case "System.Guid":
                    return SchemaJson.String("00000000-0000-0000-0000-000000000000");
                case "System.DateTimeOffset":
                    return SchemaJson.String("2000-01-01T00:00:00Z");
                case "System.DateOnly":
                    return SchemaJson.String("2000-01-01");
                case "System.TimeOnly":
                    return SchemaJson.String("00:00:00");
                case "System.TimeSpan":
                    return SchemaJson.String("00:00:00");
                case "System.Uri":
                    return SchemaJson.String("https://example.com");
            }

            if (type is IArrayTypeSymbol byteArray && byteArray.ElementType.SpecialType == SpecialType.System_Byte)
                return SchemaJson.String(string.Empty);

            if (type is IArrayTypeSymbol array)
                return ArrayExample(array.ElementType, 1);

            if (JsonSchemaEmitter.TryGetDictionaryValueType(type, out _))
                return "{}";

            if (JsonSchemaEmitter.TryGetEnumerableElementType(type, out var element))
                return ArrayExample(element, 1);

            if (type is INamedTypeSymbol obj && JsonSchemaEmitter.IsSchemaObject(type))
                return ObjectExample(obj);

            return "null";
        }

        private string ObjectExample(INamedTypeSymbol type)
        {
            if (!_path.Add(type))
                throw new UnsupportedSchemaException(
                    $"required non-nullable recursive value of type '{DisplayName(type)}' has no finite JSON example");

            try
            {
                var members = new List<KeyValuePair<string, string>>();
                foreach (var member in JsonSchemaEmitter.GetSerializableMembers(type))
                {
                    if (JsonSchemaEmitter.IsJsonExtensionData(member.Symbol)) continue;

                    var required = JsonSchemaEmitter.IsRequired(member.Symbol);
                    try
                    {
                        if (TryMemberExample(member.Symbol, member.Type, required, out var value))
                            members.Add(new KeyValuePair<string, string>(JsonSchemaEmitter.WireName(member.Symbol), value));
                    }
                    catch (UnsupportedSchemaException)
                    {
                        // Omitting an optional member is always schema-valid and is preferable to
                        // suppressing an otherwise useful contract.
                        if (!required) continue;
                        throw;
                    }
                }

                return SchemaJson.Object(members);
            }
            finally
            {
                _path.Remove(type);
            }
        }

        private bool TryMemberExample(ISymbol member, ITypeSymbol memberType, bool required, out string value)
        {
            var type = UnwrapNullable(memberType, out var nullable);

            if (type is INamedTypeSymbol objectType
                && JsonSchemaEmitter.IsSchemaObject(type)
                && _path.Contains(objectType))
            {
                if (!required)
                {
                    value = null;
                    return false;
                }
                if (nullable)
                {
                    value = "null";
                    return true;
                }

                throw CannotSatisfy(member, "required non-nullable recursive member has no finite JSON example");
            }

            if (IsStringSchema(type))
                return TryStringExample(member, type, required, out value);

            if ((IsNumeric(type)
                    || (type.TypeKind == TypeKind.Enum && !JsonSchemaEmitter.IsStringEnum((INamedTypeSymbol)type)))
                && TryRangeMinimum(member, type, out var minimum))
            {
                value = minimum;
                return true;
            }

            if (JsonSchemaEmitter.TryGetDictionaryValueType(type, out var dictionaryValueType))
                return TryDictionaryExample(member, dictionaryValueType, required, out value);

            if (TryGetCollectionElement(type, out var elementType))
                return TryArrayExample(member, elementType, required, out value);

            value = Value(memberType);
            return true;
        }

        private bool TryStringExample(ISymbol member, ITypeSymbol type, bool required, out string value)
        {
            if (HasAttribute(member, "RegularExpressionAttribute"))
                return CannotConstructOrOmit(member, required,
                    "regular expression constraints cannot be deterministically satisfied", out value);

            GetStringLengthBounds(member, out var minimum, out var maximum);
            if (minimum > maximum)
                return CannotConstructOrOmit(member, required,
                    $"minimum string length {minimum} exceeds maximum string length {maximum}", out value);
            if (minimum > MaxExampleStringLength)
                return CannotConstructOrOmit(member, required,
                    $"minimum string length {minimum} exceeds the {MaxExampleStringLength}-character example budget", out value);

            var format = BuiltInFormat(type);
            foreach (var attr in member.GetAttributes())
            {
                switch (AnnotationName(attr))
                {
                    case "UrlAttribute":
                        format = "uri";
                        break;
                    case "EmailAddressAttribute":
                        format = "email";
                        break;
                }
            }

            string candidate;
            switch (format)
            {
                case "email":
                    if (!TryEmail(minimum, maximum, out candidate))
                        return CannotConstructOrOmit(member, required,
                            "email format and length constraints have no deterministic valid example", out value);
                    break;
                case "uri":
                    if (!TryUri(minimum, maximum, out candidate))
                        return CannotConstructOrOmit(member, required,
                            "URI format and length constraints have no deterministic valid example", out value);
                    break;
                case "uuid":
                    candidate = "00000000-0000-0000-0000-000000000000";
                    break;
                case "date-time":
                    candidate = "2000-01-01T00:00:00Z";
                    break;
                case "date":
                    candidate = "2000-01-01";
                    break;
                case "time":
                    candidate = "00:00:00";
                    break;
                case "duration":
                    candidate = "00:00:00";
                    break;
                default:
                    candidate = type.SpecialType == SpecialType.System_Char ? "a" : "string";
                    if (candidate.Length < minimum)
                        candidate = new string('x', minimum);
                    if (candidate.Length > maximum)
                        candidate = new string('x', maximum);
                    value = SchemaJson.String(candidate);
                    return true;
            }

            if (candidate.Length < minimum || candidate.Length > maximum)
                return CannotConstructOrOmit(member, required,
                    $"{format} format cannot satisfy string length range {minimum}..{maximum}", out value);

            value = SchemaJson.String(candidate);
            return true;
        }

        private bool TryArrayExample(ISymbol member, ITypeSymbol elementType, bool required, out string value)
        {
            GetArrayLengthBounds(member, out var minimum, out var maximum);
            if (minimum > maximum)
                return CannotConstructOrOmit(member, required,
                    $"minimum array length {minimum} exceeds maximum array length {maximum}", out value);

            var count = Math.Max(1, minimum);
            if (maximum == 0) count = 0;
            if (count > MaxExampleCollectionItems)
                return CannotConstructOrOmit(member, required,
                    $"minimum array length {minimum} exceeds the {MaxExampleCollectionItems}-item example budget", out value);

            var unwrappedElement = UnwrapNullable(elementType, out var nullableElement);
            if (unwrappedElement is INamedTypeSymbol objectElement
                && JsonSchemaEmitter.IsSchemaObject(unwrappedElement)
                && _path.Contains(objectElement))
            {
                if (count == 0)
                {
                    value = "[]";
                    return true;
                }
                if (!nullableElement)
                    return CannotConstructOrOmit(member, required,
                        "recursive array requires a non-null element and has no finite JSON example", out value);

                value = ArrayOf("null", count);
                return true;
            }

            value = ArrayExample(elementType, count);
            return true;
        }

        private bool TryDictionaryExample(ISymbol member, ITypeSymbol valueType, bool required, out string value)
        {
            GetArrayLengthBounds(member, out var minimum, out var maximum);
            if (minimum > maximum)
                return CannotConstructOrOmit(member, required,
                    $"minimum dictionary size {minimum} exceeds maximum dictionary size {maximum}", out value);

            var count = Math.Max(1, minimum);
            if (maximum == 0) count = 0;
            if (count > MaxExampleCollectionItems)
                return CannotConstructOrOmit(member, required,
                    $"minimum dictionary size {minimum} exceeds the {MaxExampleCollectionItems}-item example budget", out value);
            if (count == 0)
            {
                value = "{}";
                return true;
            }

            var unwrappedValue = UnwrapNullable(valueType, out var nullableValue);
            string item;
            if (unwrappedValue is INamedTypeSymbol objectValue
                && JsonSchemaEmitter.IsSchemaObject(unwrappedValue)
                && _path.Contains(objectValue))
            {
                if (!nullableValue)
                    return CannotConstructOrOmit(member, required,
                        "recursive dictionary requires a non-null value and has no finite JSON example", out value);
                item = "null";
            }
            else
            {
                item = Value(valueType);
            }

            var entries = new List<KeyValuePair<string, string>>();
            for (var i = 0; i < count; i++)
                entries.Add(new KeyValuePair<string, string>(
                    i == 0 ? "key" : "key" + i.ToString(CultureInfo.InvariantCulture), item));
            value = SchemaJson.Object(entries);
            return true;
        }

        private string ArrayExample(ITypeSymbol elementType, int count)
        {
            if (count == 0) return "[]";
            return ArrayOf(Value(elementType), count);
        }

        private static string ArrayOf(string item, int count)
            => "[" + string.Join(",", Enumerable.Repeat(item, count)) + "]";

        private static bool TryEmail(int minimum, int maximum, out string value)
        {
            const string preferred = "user@example.com";
            if (preferred.Length >= minimum && preferred.Length <= maximum)
            {
                value = preferred;
                return true;
            }

            const string suffix = "@b.co";
            var target = Math.Max(minimum, suffix.Length + 1);
            // Keep the local part within the RFC limit. This deliberately refuses exotic examples.
            if (target > suffix.Length + 64 || target > maximum)
            {
                value = null;
                return false;
            }

            value = new string('a', target - suffix.Length) + suffix;
            return true;
        }

        private static bool TryUri(int minimum, int maximum, out string value)
        {
            const string preferred = "https://example.com";
            if (preferred.Length >= minimum && preferred.Length <= maximum)
            {
                value = preferred;
                return true;
            }

            var target = Math.Max(minimum, 3);
            if (target > maximum)
            {
                value = null;
                return false;
            }

            value = "x:" + new string('a', target - 2);
            return true;
        }

        private static void GetStringLengthBounds(ISymbol member, out int minimum, out int maximum)
        {
            minimum = 0;
            maximum = int.MaxValue;
            foreach (var attr in member.GetAttributes())
            {
                switch (AnnotationName(attr))
                {
                    case "MinLengthAttribute":
                        if (FirstInt(attr).HasValue) minimum = FirstInt(attr).Value;
                        break;
                    case "MaxLengthAttribute":
                        if (FirstInt(attr).HasValue) maximum = FirstInt(attr).Value;
                        break;
                    case "StringLengthAttribute":
                        if (FirstInt(attr).HasValue) maximum = FirstInt(attr).Value;
                        var namedMinimum = NamedInt(attr, "MinimumLength");
                        if (namedMinimum.HasValue && namedMinimum.Value > 0) minimum = namedMinimum.Value;
                        break;
                }
            }
        }

        private static void GetArrayLengthBounds(ISymbol member, out int minimum, out int maximum)
        {
            minimum = 0;
            maximum = int.MaxValue;
            foreach (var attr in member.GetAttributes())
            {
                switch (AnnotationName(attr))
                {
                    case "MinLengthAttribute":
                        if (FirstInt(attr).HasValue) minimum = FirstInt(attr).Value;
                        break;
                    case "MaxLengthAttribute":
                        if (FirstInt(attr).HasValue) maximum = FirstInt(attr).Value;
                        break;
                }
            }
        }

        private static bool TryRangeMinimum(ISymbol member, ITypeSymbol memberType, out string minimum)
        {
            minimum = null;
            string maximum = null;
            AttributeData range = null;
            foreach (var attr in member.GetAttributes())
            {
                if (AnnotationName(attr) != "RangeAttribute" || attr.ConstructorArguments.Length < 2)
                    continue;

                range = attr;
                if (attr.ConstructorArguments.Length == 3
                    && attr.ConstructorArguments[0].Value is ITypeSymbol operandType)
                {
                    minimum = InvariantNumberLiteral(operandType, attr.ConstructorArguments[1].Value as string);
                    maximum = InvariantNumberLiteral(operandType, attr.ConstructorArguments[2].Value as string);
                }
                else
                {
                    minimum = NumberLiteral(attr.ConstructorArguments[0].Value);
                    maximum = NumberLiteral(attr.ConstructorArguments[1].Value);
                }
            }

            if (minimum != null && maximum != null
                && TryFloatingRangeWitness(member, memberType, range, minimum, maximum, out var floatingWitness))
            {
                minimum = floatingWitness;
            }
            else if (minimum != null && maximum != null
                && decimal.TryParse(minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimumNumber)
                && decimal.TryParse(maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var maximumNumber))
            {
                var boundedType = memberType is INamedTypeSymbol
                    { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying }
                    ? underlying.SpecialType
                    : memberType.SpecialType;
                if (JsonSchemaEmitter.TryGetIntegralBounds(
                    boundedType, out var typeMinimum, out var typeMaximum))
                {
                    minimumNumber = Math.Max(minimumNumber, typeMinimum);
                    maximumNumber = Math.Min(maximumNumber, typeMaximum);
                }
                if (minimumNumber > maximumNumber)
                    throw CannotSatisfy(member, $"range minimum {minimum} exceeds maximum {maximum}");

                var minimumExclusive = range != null && NamedBool(range, "MinimumIsExclusive") == true;
                var maximumExclusive = range != null && NamedBool(range, "MaximumIsExclusive") == true;
                if (minimumNumber == maximumNumber && (minimumExclusive || maximumExclusive))
                    throw CannotSatisfy(member, "exclusive range has no values");

                var integral = IsIntegral(memberType);
                if (integral)
                {
                    var candidate = minimumExclusive
                        ? decimal.Floor(minimumNumber) + 1m
                        : decimal.Ceiling(minimumNumber);
                    if (candidate < minimumNumber
                        || (minimumExclusive && candidate <= minimumNumber)
                        || (maximumExclusive ? candidate >= maximumNumber : candidate > maximumNumber))
                        throw CannotSatisfy(member, "range has no representable integral deterministic example");
                    minimum = candidate.ToString(CultureInfo.InvariantCulture);
                }
                else if (minimumExclusive)
                {
                    var candidate = minimumNumber + ((maximumNumber - minimumNumber) / 2m);
                    if (candidate <= minimumNumber
                        || (maximumExclusive ? candidate >= maximumNumber : candidate > maximumNumber))
                        throw CannotSatisfy(member, "exclusive range has no representable deterministic example");
                    minimum = candidate.ToString(CultureInfo.InvariantCulture);
                }
            }

            return minimum != null;
        }

        private static bool TryFloatingRangeWitness(
            ISymbol member,
            ITypeSymbol memberType,
            AttributeData range,
            string minimum,
            string maximum,
            out string witness)
        {
            witness = null;
            var minimumExclusive = range != null && NamedBool(range, "MinimumIsExclusive") == true;
            var maximumExclusive = range != null && NamedBool(range, "MaximumIsExclusive") == true;

            if (memberType.SpecialType == SpecialType.System_Double)
            {
                if (!double.TryParse(minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                    || !double.TryParse(maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                    return false;
                if (min > max) throw CannotSatisfy(member, $"range minimum {minimum} exceeds maximum {maximum}");
                var candidate = minimumExclusive ? NextUp(min) : min;
                if (double.IsInfinity(candidate)
                    || candidate < min
                    || (maximumExclusive ? candidate >= max : candidate > max))
                    throw CannotSatisfy(member, "exclusive range has no representable deterministic example");
                witness = candidate.ToString("R", CultureInfo.InvariantCulture);
                return true;
            }

            if (memberType.SpecialType == SpecialType.System_Single)
            {
                if (!float.TryParse(minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                    || !float.TryParse(maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                    return false;
                if (min > max) throw CannotSatisfy(member, $"range minimum {minimum} exceeds maximum {maximum}");
                var candidate = minimumExclusive ? NextUp(min) : min;
                if (float.IsInfinity(candidate)
                    || candidate < min
                    || (maximumExclusive ? candidate >= max : candidate > max))
                    throw CannotSatisfy(member, "exclusive range has no representable deterministic example");
                witness = candidate.ToString("R", CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        private static double NextUp(double value)
        {
            if (double.IsNaN(value) || value == double.PositiveInfinity) return value;
            if (value == 0d) return double.Epsilon;
            var bits = BitConverter.DoubleToInt64Bits(value);
            return BitConverter.Int64BitsToDouble(bits + (value > 0d ? 1L : -1L));
        }

        private static float NextUp(float value)
        {
            if (float.IsNaN(value) || value == float.PositiveInfinity) return value;
            if (value == 0f) return float.Epsilon;
            var bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            return BitConverter.ToSingle(BitConverter.GetBytes(bits + (value > 0f ? 1 : -1)), 0);
        }

        private static string InvariantNumberLiteral(ITypeSymbol type, string value)
        {
            const NumberStyles integer = NumberStyles.Integer;
            const NumberStyles real = NumberStyles.Float;
            switch (type.SpecialType)
            {
                case SpecialType.System_SByte:
                    return sbyte.TryParse(value, integer, CultureInfo.InvariantCulture, out var i8) ? i8.ToString(CultureInfo.InvariantCulture) : null;
                case SpecialType.System_Byte:
                    return byte.TryParse(value, integer, CultureInfo.InvariantCulture, out var u8) ? u8.ToString(CultureInfo.InvariantCulture) : null;
                case SpecialType.System_Int16:
                    return short.TryParse(value, integer, CultureInfo.InvariantCulture, out var i16) ? i16.ToString(CultureInfo.InvariantCulture) : null;
                case SpecialType.System_UInt16:
                    return ushort.TryParse(value, integer, CultureInfo.InvariantCulture, out var u16) ? u16.ToString(CultureInfo.InvariantCulture) : null;
                case SpecialType.System_Int32:
                    return int.TryParse(value, integer, CultureInfo.InvariantCulture, out var i32) ? i32.ToString(CultureInfo.InvariantCulture) : null;
                case SpecialType.System_UInt32:
                    return uint.TryParse(value, integer, CultureInfo.InvariantCulture, out var u32) ? u32.ToString(CultureInfo.InvariantCulture) : null;
                case SpecialType.System_Int64:
                    return long.TryParse(value, integer, CultureInfo.InvariantCulture, out var i64) ? i64.ToString(CultureInfo.InvariantCulture) : null;
                case SpecialType.System_UInt64:
                    return ulong.TryParse(value, integer, CultureInfo.InvariantCulture, out var u64) ? u64.ToString(CultureInfo.InvariantCulture) : null;
                case SpecialType.System_Decimal:
                    return decimal.TryParse(value, real, CultureInfo.InvariantCulture, out var dec) ? dec.ToString("G29", CultureInfo.InvariantCulture) : null;
                case SpecialType.System_Single:
                    return float.TryParse(value, real, CultureInfo.InvariantCulture, out var single)
                        && !float.IsNaN(single) && !float.IsInfinity(single) ? single.ToString("R", CultureInfo.InvariantCulture) : null;
                case SpecialType.System_Double:
                    return double.TryParse(value, real, CultureInfo.InvariantCulture, out var dbl)
                        && !double.IsNaN(dbl) && !double.IsInfinity(dbl) ? dbl.ToString("R", CultureInfo.InvariantCulture) : null;
                default:
                    return null;
            }
        }

        private static string NumberLiteral(object value)
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

        private static bool CannotConstructOrOmit(
            ISymbol member, bool required, string reason, out string value)
        {
            value = null;
            if (!required) return false;
            throw CannotSatisfy(member, reason);
        }

        private static UnsupportedSchemaException CannotSatisfy(ISymbol member, string reason)
            => new UnsupportedSchemaException(
                $"cannot generate an example for required member '{DisplayName(member.ContainingType)}.{member.Name}': {reason}");


        private static bool HasAttribute(ISymbol member, string shortName)
            => member.GetAttributes().Any(attr => AnnotationName(attr) == shortName);

        private static string AnnotationName(AttributeData attr)
        {
            var name = attr.AttributeClass?.ToDisplayString();
            return name != null && name.StartsWith(DataAnnotations, StringComparison.Ordinal)
                ? name.Substring(DataAnnotations.Length)
                : null;
        }

        private static bool IsNumeric(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsIntegral(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType })
                type = underlyingType;

            switch (type.SpecialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    return true;
                default:
                    return false;
            }
        }


        private static bool IsStringSchema(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_String
                || type.SpecialType == SpecialType.System_Char
                || type.SpecialType == SpecialType.System_DateTime)
                return true;

            switch (type.ToDisplayString(FullName))
            {
                case "System.Guid":
                case "System.DateTimeOffset":
                case "System.DateOnly":
                case "System.TimeOnly":
                case "System.TimeSpan":
                case "System.Uri":
                    return true;
                default:
                    return false;
            }
        }

        private static string BuiltInFormat(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_DateTime) return "date-time";
            switch (type.ToDisplayString(FullName))
            {
                case "System.Guid": return "uuid";
                case "System.DateTimeOffset": return "date-time";
                case "System.DateOnly": return "date";
                case "System.TimeOnly": return "time";
                case "System.TimeSpan": return "duration";
                case "System.Uri": return "uri";
                default: return null;
            }
        }


        private static bool TryGetCollectionElement(ITypeSymbol type, out ITypeSymbol elementType)
        {
            if (type is IArrayTypeSymbol array
                && array.ElementType.SpecialType != SpecialType.System_Byte)
            {
                elementType = array.ElementType;
                return true;
            }
            return JsonSchemaEmitter.TryGetEnumerableElementType(type, out elementType);
        }

        private static ITypeSymbol UnwrapNullable(ITypeSymbol type, out bool nullable)
        {
            if (type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                nullable = true;
                return named.TypeArguments[0];
            }

            nullable = type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;
            return type;
        }

        private static int? FirstInt(AttributeData attr)
            => attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value != null
                ? (int?)Convert.ToInt32(attr.ConstructorArguments[0].Value, CultureInfo.InvariantCulture)
                : null;

        private static int? NamedInt(AttributeData attr, string name)
        {
            foreach (var argument in attr.NamedArguments)
                if (argument.Key == name && argument.Value.Value != null)
                    return Convert.ToInt32(argument.Value.Value, CultureInfo.InvariantCulture);
            return null;
        }

        private static bool? NamedBool(AttributeData attr, string name)
        {
            foreach (var argument in attr.NamedArguments)
                if (argument.Key == name && argument.Value.Value != null)
                    return Convert.ToBoolean(argument.Value.Value, CultureInfo.InvariantCulture);
            return null;
        }

        private static string EnumExample(INamedTypeSymbol enumType)
        {
            var stringEnum = JsonSchemaEmitter.IsStringEnum(enumType);
            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (!member.IsConst || member.ConstantValue == null) continue;
                return stringEnum
                    ? SchemaJson.String(JsonSchemaEmitter.EnumMemberName(member))
                    : JsonSchemaEmitter.EnumValueLiteral(member);
            }
            return stringEnum ? "\"\"" : "0";
        }

        private static string DisplayName(ITypeSymbol type) => type.ToDisplayString(FullName);

        private static readonly SymbolDisplayFormat FullName = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.None);
    }
}
