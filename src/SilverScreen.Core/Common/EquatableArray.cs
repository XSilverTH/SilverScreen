using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SilverScreen.Core.Common;

/// <summary>
/// Represents an immutable, heap-backed array with structural (element-wise) value equality.
/// </summary>
/// <remarks>
/// <para>
/// Standard C# arrays (<c>T[]</c>) exhibit reference equality when used inside record types or compared
/// with equality operators. <see cref="EquatableArray{T}"/> provides value-based equality semantics by
/// comparing elements sequentially via <see cref="ReadOnlySpan{T}.SequenceEqual"/>.
/// </para>
/// <para>
/// Designed for Native AOT friendliness: avoids runtime code generation when serialized with System.Text.Json
/// by pairing with statically generated <see cref="JsonSerializerContext"/> metadata and registered element types.
/// Array mutation is prevented through defensive copying on construction and read-only indexer/span access.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of elements in the array. Must implement equality comparison.</typeparam>
[CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
[JsonConverter(typeof(EquatableArrayJsonConverterFactory))]
public readonly struct EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
{
    private readonly T[]? _items;

    public static EquatableArray<T> Empty => default;

    public EquatableArray(T[]? items)
    {
        _items = items is { Length: > 0 } ? (T[])items.Clone() : items;
    }

    public EquatableArray(ReadOnlySpan<T> items)
    {
        _items = [.. items];
    }

    public EquatableArray(IEnumerable<T>? items)
    {
        _items = items switch
        {
            null => null,
            T[] array => array.Length == 0 ? [] : (T[])array.Clone(),
            _ => [.. items]
        };
    }

    public int Count => _items?.Length ?? 0;
    public int Length => _items?.Length ?? 0;
    public bool IsEmpty => Count == 0;

    public T this[int index]
    {
        get
        {
            var items = _items ?? [];
            return items[index];
        }
    }

    public ReadOnlySpan<T> AsSpan()
    {
        return _items.AsSpan();
    }

    public bool Equals(EquatableArray<T> other)
    {
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (_items is null)
            return hash.ToHashCode();

        foreach (var item in _items)
            hash.Add(item);

        return hash.ToHashCode();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
    {
        return !left.Equals(right);
    }

    public IEnumerator<T> GetEnumerator()
    {
        var items = _items ?? [];
        return ((IEnumerable<T>)items).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public static implicit operator EquatableArray<T>(T[]? array)
    {
        return new EquatableArray<T>(array);
    }

    public static implicit operator EquatableArray<T>(List<T>? list)
    {
        return list is null ? default : [.. list];
    }
}

public static class EquatableArray
{
    public static EquatableArray<T> Create<T>(ReadOnlySpan<T> items)
    {
        return new EquatableArray<T>(items);
    }
}

public sealed class EquatableArrayJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType &&
               typeToConvert.GetGenericTypeDefinition() == typeof(EquatableArray<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        if (elementType == typeof(string))
            return new EquatableArrayJsonConverter<string>();
        if (elementType == typeof(int))
            return new EquatableArrayJsonConverter<int>();
        if (elementType == typeof(long))
            return new EquatableArrayJsonConverter<long>();
        if (elementType == typeof(double))
            return new EquatableArrayJsonConverter<double>();
        if (elementType == typeof(float))
            return new EquatableArrayJsonConverter<float>();
        if (elementType == typeof(bool))
            return new EquatableArrayJsonConverter<bool>();
        if (elementType == typeof(short))
            return new EquatableArrayJsonConverter<short>();
        if (elementType == typeof(byte))
            return new EquatableArrayJsonConverter<byte>();
        if (elementType == typeof(decimal))
            return new EquatableArrayJsonConverter<decimal>();

        return null;
    }
}

public sealed class EquatableArrayJsonConverter<T> : JsonConverter<EquatableArray<T>>
{
    private JsonTypeInfo<T>? _elementTypeInfo;

    private JsonTypeInfo<T> GetElementTypeInfo(JsonSerializerOptions options)
    {
        return _elementTypeInfo ??= (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
    }

    public override EquatableArray<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return EquatableArray<T>.Empty;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected StartArray token, got {reader.TokenType}");

        var typeInfo = GetElementTypeInfo(options);
        var list = new List<T>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return new EquatableArray<T>(list.ToArray());

            var element = JsonSerializer.Deserialize(ref reader, typeInfo);
            if (element is not null)
                list.Add(element);
        }

        throw new JsonException("Expected EndArray token, but reached end of input");
    }

    public override void Write(Utf8JsonWriter writer, EquatableArray<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        var typeInfo = GetElementTypeInfo(options);

        foreach (var item in value) JsonSerializer.Serialize(writer, item, typeInfo);

        writer.WriteEndArray();
    }
}