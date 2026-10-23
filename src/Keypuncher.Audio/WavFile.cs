namespace Keypuncher.Audio;

/// <summary>Reads and writes the WAV files that hold recordings: mono, 16-bit PCM. Reading also accepts
/// float, 24-bit and multichannel files (downmixed), so clips from other tools work.</summary>
public static class WavFile
{
    public static void Write(Stream stream, ReadOnlySpan<float> mono, int sampleRate)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        var dataBytes = mono.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);       // PCM
        w.Write((short)1);       // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2); // bytes per second
        w.Write((short)2);       // block align
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in mono)
            w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
    }

    public static (float[] Mono, int SampleRate) Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF file");
        r.ReadInt32();
        if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAVE file");

        int format = 0, channels = 0, sampleRate = 0, bits = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(r.ReadChars(4));
            var size = r.ReadInt32();
            var next = stream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                format = r.ReadInt16();
                channels = r.ReadInt16();
                sampleRate = r.ReadInt32();
                r.ReadInt32();
                r.ReadInt16();
                bits = r.ReadInt16();
                if (format == 0xFFFE && size >= 40)
                {
                    r.ReadBytes(8);
                    format = r.ReadInt16(); // the sub-format GUID starts with the real format tag
                }
            }
            else if (id == "data")
            {
                if (channels == 0) throw new InvalidDataException("data before fmt");
                var bytes = r.ReadBytes((int)Math.Min(size, stream.Length - stream.Position));
                return (Decode(bytes, format, channels, bits), sampleRate);
            }
            stream.Position = Math.Min(next, stream.Length);
        }
        throw new InvalidDataException("no data chunk");
    }

    private static float[] Decode(byte[] bytes, int format, int channels, int bits)
    {
        var bytesPerSample = bits / 8;
        var frames = bytes.Length / (bytesPerSample * channels);
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var o = ((f * channels) + c) * bytesPerSample;
                sum += (format, bits) switch
                {
                    (1, 16) => BitConverter.ToInt16(bytes, o) / 32768f,
                    (1, 24) => ((bytes[o] << 8) | (bytes[o + 1] << 16) | (bytes[o + 2] << 24)) / 2147483648f,
                    (1, 32) => BitConverter.ToInt32(bytes, o) / 2147483648f,
                    (1, 8) => (bytes[o] - 128) / 128f,
                    (3, 32) => BitConverter.ToSingle(bytes, o),
                    _ => throw new InvalidDataException($"unsupported WAV format {format}/{bits}-bit"),
                };
            }
            mono[f] = sum / channels;
        }
        return mono;
    }
}
