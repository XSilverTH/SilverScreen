using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SilverScreen.Core.Common;

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

    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification =
            "EquatableArray is used with statically known element types registered in JsonSerializerContext.")]
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(EquatableArrayJsonConverter<>).MakeGenericType(elementType);
        return (JsonConverter?)Activator.CreateInstance(converterType);
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