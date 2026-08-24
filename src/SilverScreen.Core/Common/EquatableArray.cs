using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(EquatableArrayJsonConverter<>).MakeGenericType(elementType);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

public sealed class EquatableArrayJsonConverter<T> : JsonConverter<EquatableArray<T>>
{
    public override EquatableArray<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return EquatableArray<T>.Empty;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected StartArray token, got {reader.TokenType}");

        var elementConverter = (JsonConverter<T>?)options.GetConverter(typeof(T));
        var list = new List<T>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return new EquatableArray<T>(list.ToArray());

            var element = elementConverter is not null
                ? elementConverter.Read(ref reader, typeof(T), options)
                : JsonSerializer.Deserialize<T>(ref reader, options);
            if (element is not null)
                list.Add(element);
        }

        throw new JsonException("Expected EndArray token, but reached end of input");
    }

    public override void Write(Utf8JsonWriter writer, EquatableArray<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        var elementConverter = (JsonConverter<T>?)options.GetConverter(typeof(T));

        foreach (var item in value)
            if (elementConverter is not null)
                elementConverter.Write(writer, item, options);
            else
                JsonSerializer.Serialize(writer, item, options);

        writer.WriteEndArray();
    }
}