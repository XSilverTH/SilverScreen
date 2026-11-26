using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilverScreen.Core.Common;

/// <summary>
///     Represents an immutable, heap-backed array with structural (element-wise) value equality.
/// </summary>
/// <remarks>
///     <para>
///         Standard C# arrays (<c>T[]</c>) exhibit reference equality when used inside record types or compared
///         with equality operators. <see cref="EquatableArray{T}" /> provides value-based equality semantics by
///         comparing elements sequentially via
///         <see cref="MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})" />.
///     </para>
///     <para>
///         Designed for Native AOT friendliness: avoids runtime code generation when serialized with System.Text.Json
///         by pairing with statically generated <see cref="JsonSerializerContext" /> metadata and registered element
///         types.
///         Array mutation is prevented through defensive copying on construction and read-only indexer/span access.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of elements in the array. Must implement equality comparison.</typeparam>
[CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
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
        return [.. array!];
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

/// <summary>
///     Native AOT-compatible JSON converter for <see cref="EquatableArray{T}" /> of <see cref="string" />.
///     Reads and writes JSON string arrays directly via <see cref="Utf8JsonReader" /> and <see cref="Utf8JsonWriter" />
///     without runtime reflection or <see cref="JsonSerializerOptions.GetTypeInfo(Type)" /> lookups.
/// </summary>
public sealed class EquatableArrayStringJsonConverter : JsonConverter<EquatableArray<string>>
{
    public override EquatableArray<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return EquatableArray<string>.Empty;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected StartArray token, got {reader.TokenType}");

        var list = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return list.Count == 0 ? EquatableArray<string>.Empty : new EquatableArray<string>(list.ToArray());

            if (reader.TokenType == JsonTokenType.Null)
                continue;

            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected String token, got {reader.TokenType}");

            var element = reader.GetString();
            if (element is not null)
                list.Add(element);
        }

        throw new JsonException("Expected EndArray token, but reached end of input");
    }

    public override void Write(Utf8JsonWriter writer, EquatableArray<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value.AsSpan())
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}