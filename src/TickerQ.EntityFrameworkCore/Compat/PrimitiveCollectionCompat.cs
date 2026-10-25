using System;
using Microsoft.EntityFrameworkCore;
#if !NET10_0_OR_GREATER || NET11_0_OR_GREATER
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
#endif

namespace TickerQ.EntityFrameworkCore.Compat;

/// <summary>
/// EF Core maps <c>int[]</c> (RetryIntervals) as a primitive collection stored as JSON text. Oracle's
/// MySql.EntityFrameworkCore reads that JSON back only in its 10.x releases: 8.x and 9.x ask the reader for an
/// int[] directly, and the 11.0 preview fails to convert it. On those lines TickerQ converts the column itself.
/// The stored text is the same <c>[1,2,3]</c> JSON, so data and schema are unchanged. Revisit when
/// MySql.EntityFrameworkCore 11.0 ships.
/// </summary>
internal static class PrimitiveCollectionCompat
{
#if NET10_0_OR_GREATER && !NET11_0_OR_GREATER
    public static void Apply(ModelBuilder modelBuilder, string providerName, Func<Type, bool> isTickerQEntity) { }
#else
    public static void Apply(ModelBuilder modelBuilder, string providerName, Func<Type, bool> isTickerQEntity)
    {
        if (!string.Equals(providerName, "MySql.EntityFrameworkCore", StringComparison.Ordinal))
            return;
        // Only keyed entity types: value types such as CronChainNode can be in the model provisionally and
        // are dropped later once their owning property gets its own converter.
        foreach (var entity in modelBuilder.Model.GetEntityTypes()
                     .Where(e => isTickerQEntity(e.ClrType) && e.FindPrimaryKey() != null).ToArray())
        foreach (var name in entity.GetDeclaredProperties().Where(p => p.ClrType == typeof(int[])).Select(p => p.Name).ToArray())
        {
            // Clearing the element type turns the primitive collection into a scalar column; EF Core 11 replaces
            // the property object while doing so, so look it up again before configuring it.
            entity.FindDeclaredProperty(name)!.SetElementType(null);
            var property = entity.FindDeclaredProperty(name)!;
            property.SetValueConverter(Converter);
            property.SetValueComparer(Comparer);
        }
    }

    private static readonly ValueConverter<int[], string> Converter = new(
        value => Serialize(value),
        json => Deserialize(json));

    private static readonly ValueComparer<int[]> Comparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        value => value == null ? 0 : value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        value => value == null ? null : value.ToArray());

    private static string Serialize(int[] value) => JsonSerializer.Serialize(value, IntArrayJsonContext.Default.Int32Array);

    private static int[] Deserialize(string json) => JsonSerializer.Deserialize(json, IntArrayJsonContext.Default.Int32Array);
#endif
}

#if !NET10_0_OR_GREATER || NET11_0_OR_GREATER
[System.Text.Json.Serialization.JsonSerializable(typeof(int[]))]
internal sealed partial class IntArrayJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
#endif
