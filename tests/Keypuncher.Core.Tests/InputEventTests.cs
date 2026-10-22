using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public class InputEventTests
{
    [Fact]
    public void Records_are_value_equal()
    {
        var t = DateTimeOffset.UnixEpoch;
        Assert.Equal(new InputEvent(t, 30, "a", 0.1, 0.5, 1), new InputEvent(t, 30, "a", 0.1, 0.5, 1));
    }
}
