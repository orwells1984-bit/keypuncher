using System.Text.RegularExpressions;

namespace Keypuncher.Core;

/// <summary>A camera or microphone as Windows lists it: an id that names the very device (for a camera, the USB port
/// it is plugged into is part of it) and the name people see.</summary>
public sealed record CaptureDevice(string Id, string Name);

/// <summary>Finds the camera or microphone a grown-up chose among the ones Windows has now. Ids change when a device
/// moves to another USB port, and Windows numbers a second device of the same kind into its name
/// ("Microphone (2- USB2.0 Camera)"), so a choice is kept by both and found by the id first, then by the name when only
/// one device has it.</summary>
public static partial class DeviceChoice
{
    /// <returns>The index of the chosen device in <paramref name="devices"/>, or -1 when nothing was chosen or the
    /// chosen device is not there (the caller then uses its default).</returns>
    public static int Find(IReadOnlyList<CaptureDevice> devices, string? id, string? name)
    {
        if (id is { Length: > 0 })
            for (var i = 0; i < devices.Count; i++)
                if (string.Equals(devices[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
        if (name is not { Length: > 0 }) return -1;
        foreach (var match in new Func<string, string>[] { n => n.Trim(), Bare })
        {
            var wanted = match(name);
            var found = Enumerable.Range(0, devices.Count).Where(i => string.Equals(match(devices[i].Name), wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 1) return found[0];
            // Two of the same name: which is which cannot be told by name, so neither is guessed.
            if (found.Count > 1) return -1;
        }
        return -1;
    }

    /// <summary>A device name without the number Windows puts in front of a second device of the same kind, inside
    /// the brackets or at the start: "Microphone (2- USB2.0 Camera)" becomes "Microphone (USB2.0 Camera)".</summary>
    public static string Bare(string name) => Numbered().Replace(name.Trim(), "$1");

    [GeneratedRegex(@"(^|\()\d+- ")]
    private static partial Regex Numbered();
}
