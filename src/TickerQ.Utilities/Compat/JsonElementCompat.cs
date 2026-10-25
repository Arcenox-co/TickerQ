using System.Text.Json;
#if !NET9_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
#endif

namespace TickerQ.Utilities.Compat;

/// <summary>
/// Structural JSON equality; <c>JsonElement.DeepEquals</c> exists from .NET 9. The fallback matches its
/// rules: objects compare their properties regardless of order, arrays compare in order, strings compare
/// their unescaped values, and numbers compare by value (<c>1</c>, <c>1.0</c>, and <c>1e0</c> are equal).
/// </summary>
internal static class JsonElementCompat
{
#if NET9_0_OR_GREATER
    public static bool DeepEquals(JsonElement left, JsonElement right) => JsonElement.DeepEquals(left, right);
#else
    public static bool DeepEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;
        switch (left.ValueKind)
        {
            case JsonValueKind.String:
                return left.GetString() == right.GetString();
            case JsonValueKind.Number:
                return NumbersEqual(left.GetRawText(), right.GetRawText());
            case JsonValueKind.Array:
                if (left.GetArrayLength() != right.GetArrayLength())
                    return false;
                using (var l = left.EnumerateArray())
                using (var r = right.EnumerateArray())
                    while (l.MoveNext() && r.MoveNext())
                        if (!DeepEquals(l.Current, r.Current))
                            return false;
                return true;
            case JsonValueKind.Object:
                return ObjectsEqual(left, right);
            default:
                // True, False, Null, and Undefined are equal when their kinds are.
                return true;
        }
    }

    private static bool ObjectsEqual(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToList();
        var rightProperties = right.EnumerateObject().ToList();
        if (leftProperties.Count != rightProperties.Count)
            return false;

        // Duplicate names are compared as multisets, in their original relative order.
        var remaining = new Dictionary<string, Queue<JsonElement>>(StringComparer.Ordinal);
        foreach (var property in rightProperties)
        {
            if (!remaining.TryGetValue(property.Name, out var values))
                remaining[property.Name] = values = new Queue<JsonElement>();
            values.Enqueue(property.Value);
        }
        foreach (var property in leftProperties)
        {
            if (!remaining.TryGetValue(property.Name, out var values) || values.Count == 0 ||
                !DeepEquals(property.Value, values.Dequeue()))
                return false;
        }
        return true;
    }

    // Compares two JSON number literals exactly by value: sign, significant digits, and decimal exponent.
    private static bool NumbersEqual(string left, string right)
        => Normalize(left) == Normalize(right);

    private static (bool Negative, string Digits, long Exponent) Normalize(string number)
    {
        var negative = number.StartsWith('-');
        var text = negative ? number[1..] : number;
        long exponent = 0;
        var e = text.IndexOfAny(['e', 'E']);
        if (e >= 0)
        {
            exponent = long.Parse(text[(e + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            text = text[..e];
        }
        var dot = text.IndexOf('.');
        if (dot >= 0)
        {
            exponent -= text.Length - dot - 1;
            text = text.Remove(dot, 1);
        }
        var digits = text.TrimStart('0');
        var trimmed = digits.TrimEnd('0');
        exponent += digits.Length - trimmed.Length;
        if (trimmed.Length == 0)
            return (false, "0", 0); // Every zero is equal, including -0.
        return (negative, trimmed, exponent);
    }
#endif
}
