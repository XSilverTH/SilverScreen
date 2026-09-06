using System.Text.Json;
using SilverScreen.Core.Common;

namespace SilverScreen.Tests.Common;

public sealed class EquatableArrayTests
{
    [Fact]
    public void Factory_CanConvert_ReturnsTrueForEquatableArray()
    {
        var factory = new EquatableArrayJsonConverterFactory();
        Assert.True(factory.CanConvert(typeof(EquatableArray<string>)));
        Assert.True(factory.CanConvert(typeof(EquatableArray<int>)));
        Assert.False(factory.CanConvert(typeof(string[])));
    }

    [Fact]
    public void Factory_CreateConverter_ReturnsSpecializedConverterForPrimitives()
    {
        var factory = new EquatableArrayJsonConverterFactory();
        var options = new JsonSerializerOptions();

        Assert.IsType<EquatableArrayJsonConverter<string>>(factory.CreateConverter(typeof(EquatableArray<string>), options));
        Assert.IsType<EquatableArrayJsonConverter<int>>(factory.CreateConverter(typeof(EquatableArray<int>), options));
        Assert.IsType<EquatableArrayJsonConverter<long>>(factory.CreateConverter(typeof(EquatableArray<long>), options));
        Assert.IsType<EquatableArrayJsonConverter<double>>(factory.CreateConverter(typeof(EquatableArray<double>), options));
        Assert.IsType<EquatableArrayJsonConverter<float>>(factory.CreateConverter(typeof(EquatableArray<float>), options));
        Assert.IsType<EquatableArrayJsonConverter<bool>>(factory.CreateConverter(typeof(EquatableArray<bool>), options));
        Assert.IsType<EquatableArrayJsonConverter<short>>(factory.CreateConverter(typeof(EquatableArray<short>), options));
        Assert.IsType<EquatableArrayJsonConverter<byte>>(factory.CreateConverter(typeof(EquatableArray<byte>), options));
        Assert.IsType<EquatableArrayJsonConverter<decimal>>(factory.CreateConverter(typeof(EquatableArray<decimal>), options));

        // Unsupported types return null
        Assert.Null(factory.CreateConverter(typeof(EquatableArray<DateTime>), options));
    }

    [Fact]
    public void Serialization_StringEquatableArray_RoundtripsCorrectly()
    {
        EquatableArray<string> array = ["item1", "item2", "item3"];
        var json = JsonSerializer.Serialize(array);
        var deserialized = JsonSerializer.Deserialize<EquatableArray<string>>(json);

        Assert.Equal(array, deserialized);
    }

    [Fact]
    public void Serialization_IntEquatableArray_RoundtripsCorrectly()
    {
        EquatableArray<int> array = [1, 2, 3, 42];
        var json = JsonSerializer.Serialize(array);
        var deserialized = JsonSerializer.Deserialize<EquatableArray<int>>(json);

        Assert.Equal(array, deserialized);
    }

    [Fact]
    public void Serialization_BoolEquatableArray_RoundtripsCorrectly()
    {
        EquatableArray<bool> array = [true, false, true];
        var json = JsonSerializer.Serialize(array);
        var deserialized = JsonSerializer.Deserialize<EquatableArray<bool>>(json);

        Assert.Equal(array, deserialized);
    }

    [Fact]
    public void Equality_ComparesElementWise()
    {
        EquatableArray<string> a = ["a", "b"];
        EquatableArray<string> b = ["a", "b"];
        EquatableArray<string> c = ["a", "c"];

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.NotEqual(a, c);
        Assert.True(a != c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
