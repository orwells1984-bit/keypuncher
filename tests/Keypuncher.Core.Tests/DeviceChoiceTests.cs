using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public class DeviceChoiceTests
{
    private static readonly CaptureDevice[] Cameras =
    [
        new(@"\\?\USB#VID_1111&PID_0001#port1", "USB2.0 Camera"),
        new(@"\\?\USB#VID_2222&PID_0002#port2", "HD Pro Webcam"),
    ];

    [Fact]
    public void Nothing_chosen_finds_nothing() => Assert.Equal(-1, DeviceChoice.Find(Cameras, null, null));

    [Fact]
    public void The_id_finds_the_device() => Assert.Equal(1, DeviceChoice.Find(Cameras, Cameras[1].Id, "something else"));

    [Fact]
    public void A_device_moved_to_another_port_is_found_by_its_name() =>
        Assert.Equal(1, DeviceChoice.Find(Cameras, @"\\?\USB#VID_2222&PID_0002#port9", "HD Pro Webcam"));

    [Fact]
    public void Two_devices_of_the_same_name_are_not_guessed_between()
    {
        CaptureDevice[] twins = [new("a", "USB2.0 Camera"), new("b", "USB2.0 Camera")];
        Assert.Equal(-1, DeviceChoice.Find(twins, "gone", "USB2.0 Camera"));
        Assert.Equal(1, DeviceChoice.Find(twins, "b", "USB2.0 Camera"));
    }

    [Fact]
    public void A_missing_device_finds_nothing() => Assert.Equal(-1, DeviceChoice.Find(Cameras, "gone", "Not Here"));

    [Theory]
    [InlineData("Microphone (2- USB2.0 Camera)", "Microphone (USB2.0 Camera)")]
    [InlineData("2- USB2.0 Camera", "USB2.0 Camera")]
    [InlineData("Microphone (Realtek(R) Audio)", "Microphone (Realtek(R) Audio)")]
    public void Windows_numbering_is_ignored(string name, string bare) => Assert.Equal(bare, DeviceChoice.Bare(name));

    [Fact]
    public void A_renumbered_microphone_is_still_found()
    {
        CaptureDevice[] mics = [new("0", "Microphone (Realtek(R) Audio)"), new("1", "Microphone (2- USB2.0 Camera)")];
        Assert.Equal(1, DeviceChoice.Find(mics, null, "Microphone (USB2.0 Camera)"));
    }
}
