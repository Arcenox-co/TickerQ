using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TickerQ.EntityFrameworkCore.Infrastructure.Dashboard
{
    internal static class JsonExampleGenerator
    {
        private static object GenerateExample(Type type) => Generate(type);

        private static object Generate(Type type)
        {
            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                return Generate(underlyingType);
            }

            // Handle primitive types
            if (type.IsPrimitive || type == typeof(string))
            {
                return GetDefaultValue(type);
            }

            // Handle arrays
            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var array = Array.CreateInstance(elementType!, 1);
                array.SetValue(Generate(elementType!), 0);
                return array;
            }

            // Handle generic lists (List<T>)
            if ((type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>)))
            {
                var elementType = type.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(elementType);
                var list = Activator.CreateInstance(listType);
                list!.GetType().GetMethod("Add")!.Invoke(list, new[] { Generate(elementType) });
                return list;
            }

            // Handle complex objects
            if (type.IsClass || type.IsValueType)
            {
                var instance = Activator.CreateInstance(type)!;
                foreach (var property in type.GetProperties())
                {
                    if (property.CanWrite)
                    {
                        var value = Generate(property.PropertyType);
                        property.SetValue(instance, value);
                    }
                }

                return instance;
            }

            return GetDefaultValue(type);
        }

        private static object GetDefaultValue(Type type)
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Boolean => true,
                TypeCode.Byte => (byte)1,
                TypeCode.Char => 'a',
                TypeCode.DateTime => new DateTime(2023, 1, 1),
                TypeCode.DBNull => DBNull.Value,
                TypeCode.Decimal => 123.45m,
                TypeCode.Double => 123.45,
                TypeCode.Empty => null,
                TypeCode.Int16 => (short)1,
                TypeCode.Int32 => 123,
                TypeCode.Int64 => 123L,
                TypeCode.Object => Activator.CreateInstance(type)!,
                TypeCode.SByte => (sbyte)1,
                TypeCode.Single => 123.45f,
                TypeCode.String => "string",
                TypeCode.UInt16 => (ushort)1,
                TypeCode.UInt32 => 123u,
                TypeCode.UInt64 => 123ul,
                _ => Activator.CreateInstance(type)!,
            };
        }

        private static string GenerateExampleJson(Type type)
        {
            return JsonSerializer.Serialize(GenerateExample(type), new JsonSerializerOptions { WriteIndented = true });
        }

        public static bool TryGenerateExampleJson(Type type, out string json)
        {
            try
            {
                json = GenerateExampleJson(type);
                return true;
            }
            catch (Exception)
            {
                json = string.Empty;
                return false;
            }
        }
    }
}