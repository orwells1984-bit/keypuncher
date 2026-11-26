using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public class KeyGateTests
{
    private const int A = 0x41, G = 0x47, F11 = 0x7A, LCtrl = 0xA2;

    [Theory]
    [InlineData(SystemKeys.LeftWin)]
    [InlineData(SystemKeys.RightWin)]
    [InlineData(SystemKeys.Apps)]
    [InlineData(SystemKeys.PrintScreen)]
    [InlineData(SystemKeys.Sleep)]
    [InlineData(0xAD)] // volume mute
    [InlineData(0xB3)] // play/pause
    [InlineData(0xAC)] // browser home
    [InlineData(0xB4)] // launch mail
    public void Keys_windows_acts_on_by_themselves_are_system(int vk) =>
        Assert.True(SystemKeys.IsSystem(vk, ctrl: false, shift: false, alt: false, win: false));

    [Theory]
    [InlineData(A)]
    [InlineData(F11)]
    [InlineData(SystemKeys.Escape)]
    [InlineData(SystemKeys.Tab)]
    [InlineData(SystemKeys.Space)]
    [InlineData(SystemKeys.LeftShift)]
    [InlineData(SystemKeys.LeftAlt)]
    [InlineData(LCtrl)]
    public void Plain_typing_is_not_system(int vk) =>
        Assert.False(SystemKeys.IsSystem(vk, ctrl: false, shift: false, alt: false, win: false));

    [Theory]
    [InlineData(SystemKeys.Tab)]
    [InlineData(SystemKeys.Escape)]
    [InlineData(SystemKeys.F4)]
    [InlineData(SystemKeys.Space)]
    [InlineData(SystemKeys.LeftShift)]
    public void Alt_shortcuts_are_system(int vk) =>
        Assert.True(SystemKeys.IsSystem(vk, ctrl: false, shift: false, alt: true, win: false));

    [Fact]
    public void AltGr_typing_is_not_system()
    {
        // AltGr is Ctrl+Alt to Windows: AltGr+Q is @ on the Icelandic layout.
        Assert.False(SystemKeys.IsSystem(0x51, ctrl: true, shift: false, alt: true, win: false));
        Assert.False(SystemKeys.IsSystem(SystemKeys.Space, ctrl: true, shift: false, alt: true, win: false));
    }

    [Fact]
    public void Ctrl_Esc_and_Ctrl_Shift_Esc_are_system()
    {
        Assert.True(SystemKeys.IsSystem(SystemKeys.Escape, ctrl: true, shift: false, alt: false, win: false));
        Assert.True(SystemKeys.IsSystem(SystemKeys.Escape, ctrl: true, shift: true, alt: false, win: false));
    }

    [Fact]
    public void Shift_then_Alt_is_system() =>
        Assert.True(SystemKeys.IsSystem(SystemKeys.LeftAlt, ctrl: false, shift: true, alt: false, win: false));

    [Fact]
    public void Anything_with_Win_held_is_system()
    {
        Assert.True(SystemKeys.IsSystem(G, false, false, false, win: true));
        Assert.True(SystemKeys.IsSystem(F11, false, false, false, win: true));
    }

    [Fact]
    public void Win_G_and_Win_F11_never_reach_windows_outside_play()
    {
        var gate = new KeyGate();
        Assert.True(gate.Keep(SystemKeys.LeftWin, down: true, false, false, false, KeyBlock.SystemOnly));
        Assert.True(gate.Keep(G, down: true, false, false, false, KeyBlock.SystemOnly));
        Assert.True(gate.Keep(G, down: false, false, false, false, KeyBlock.SystemOnly));
        Assert.True(gate.Keep(F11, down: true, false, false, false, KeyBlock.SystemOnly));
        Assert.True(gate.Keep(F11, down: false, false, false, false, KeyBlock.SystemOnly));
        Assert.True(gate.Keep(SystemKeys.LeftWin, down: false, false, false, false, KeyBlock.SystemOnly));
        // With Win let go, G types again.
        Assert.False(gate.Keep(G, down: true, false, false, false, KeyBlock.SystemOnly));
    }

    [Fact]
    public void Play_keeps_everything()
    {
        var gate = new KeyGate();
        Assert.True(gate.Keep(A, down: true, false, false, false, KeyBlock.All));
        Assert.True(gate.Keep(A, down: false, false, false, false, KeyBlock.All));
    }

    [Fact]
    public void A_release_follows_its_press_when_the_mode_changes()
    {
        var gate = new KeyGate();
        // Ctrl pressed on the grown-ups' page (Windows sees it), released in play: Windows must see the release.
        Assert.False(gate.Keep(LCtrl, down: true, false, false, false, KeyBlock.SystemOnly));
        Assert.False(gate.Keep(LCtrl, down: false, true, false, false, KeyBlock.All));
        // Pressed in play (kept), released on the grown-ups' page: kept too, so Windows sees neither.
        Assert.True(gate.Keep(A, down: true, false, false, false, KeyBlock.All));
        Assert.True(gate.Keep(A, down: false, false, false, false, KeyBlock.SystemOnly));
    }

    [Fact]
    public void Auto_repeat_follows_the_first_press()
    {
        var gate = new KeyGate();
        Assert.False(gate.Keep(SystemKeys.LeftShift, down: true, false, false, false, KeyBlock.SystemOnly));
        Assert.False(gate.Keep(SystemKeys.LeftShift, down: true, false, true, false, KeyBlock.All));
        Assert.False(gate.Keep(SystemKeys.LeftShift, down: false, false, true, false, KeyBlock.All));
    }

    [Fact]
    public void Shift_released_while_Alt_is_held_still_reaches_windows()
    {
        var gate = new KeyGate();
        Assert.False(gate.Keep(SystemKeys.LeftShift, down: true, false, false, false, KeyBlock.SystemOnly));
        // Alt with Shift held is the input-language shortcut: kept.
        Assert.True(gate.Keep(SystemKeys.LeftAlt, down: true, false, true, false, KeyBlock.SystemOnly));
        // Shift's release goes to Windows, which saw its press, or Shift would stay held.
        Assert.False(gate.Keep(SystemKeys.LeftShift, down: false, false, true, true, KeyBlock.SystemOnly));
        Assert.True(gate.Keep(SystemKeys.LeftAlt, down: false, false, false, true, KeyBlock.SystemOnly));
    }

    [Fact]
    public void A_release_never_seen_pressed_goes_to_windows()
    {
        var gate = new KeyGate();
        Assert.False(gate.Keep(LCtrl, down: false, false, false, false, KeyBlock.All));
    }

    [Fact]
    public void None_lets_everything_through()
    {
        var gate = new KeyGate();
        Assert.False(gate.Keep(SystemKeys.LeftWin, down: true, false, false, false, KeyBlock.None));
        Assert.False(gate.Keep(SystemKeys.LeftWin, down: false, false, false, false, KeyBlock.None));
    }
}
