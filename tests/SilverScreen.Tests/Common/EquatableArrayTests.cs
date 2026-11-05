using System.Text.Json;
using SilverScreen.Core.Common;

namespace SilverScreen.Tests.Common;

public sealed class EquatableArrayTests
{
    [Fact]
    public void Serialization_StringEquatableArray_RoundtripsCorrectly()
    {
        EquatableArray<string> array = ["item1", "item2", "item3"];
        var json = JsonSerializer.Serialize(array);
        var deserialized = JsonSerializer.Deserialize<EquatableArray<string>>(json);

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