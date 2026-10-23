namespace Keypuncher.Audio;

/// <summary>Note names, scales and frequencies for recipe voices ("C#4", "pentatonic").</summary>
public static class Notes
{
    private static readonly Dictionary<string, int[]> Scales = new()
    {
        ["pentatonic"] = [0, 2, 4, 7, 9],
        ["major"] = [0, 2, 4, 5, 7, 9, 11],
        ["lydian"] = [0, 2, 4, 6, 7, 9, 11],
        ["whole-tone"] = [0, 2, 4, 6, 8, 10],
    };

    private static readonly int[] Letters = [9, 11, 0, 2, 4, 5, 7]; // A B C D E F G

    /// <summary>MIDI note number for a name such as "C4" or "F#3"; middle C (C4) is 60. Falls back to 60.</summary>
    public static int Parse(string name)
    {
        if (name.Length is < 2 or > 3 || name[0] is < 'A' or > 'G') return 60;
        var semis = Letters[name[0] - 'A'];
        var sharp = name.Length == 3 && name[1] == '#';
        var octaveChar = name[^1];
        if (octaveChar is < '0' or > '9') return 60;
        return ((octaveChar - '0' + 1) * 12) + semis + (sharp ? 1 : 0);
    }

    public static double Frequency(double midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>The MIDI note <paramref name="degree"/> steps up <paramref name="scale"/> from <paramref name="rootMidi"/>;
    /// degree 0 is the root and each full pass through the scale adds an octave.</summary>
    public static int Degree(string scale, int rootMidi, int degree)
    {
        var steps = Scales.TryGetValue(scale, out var s) ? s : Scales["pentatonic"];
        var octave = (int)Math.Floor(degree / (double)steps.Length);
        var index = degree - (octave * steps.Length);
        return rootMidi + (octave * 12) + steps[index];
    }
}
