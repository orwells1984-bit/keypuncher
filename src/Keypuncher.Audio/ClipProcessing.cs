namespace Keypuncher.Audio;

/// <summary>Cleans up a fresh recording so that 280 clips can be made in one sitting without editing:
/// trims the silence before and after, fades the ends, and brings the level up to a common peak.</summary>
public static class ClipProcessing
{
    /// <summary>Linear resampling; plenty for speech.</summary>
    public static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate || input.Length == 0) return input;
        var length = (int)((long)input.Length * toRate / fromRate);
        var output = new float[length];
        var step = fromRate / (double)toRate;
        for (var i = 0; i < length; i++)
        {
            var pos = i * step;
            var i0 = (int)pos;
            var i1 = Math.Min(i0 + 1, input.Length - 1);
            var frac = (float)(pos - i0);
            output[i] = (input[i0] * (1 - frac)) + (input[i1] * frac);
        }
        return output;
    }

    /// <summary>Returns the trimmed, faded and normalised clip, or an empty array if it was all silence.</summary>
    /// <param name="thresholdDb">Level, relative to the loudest 10 ms, below which a window counts as silence.</param>
    public static float[] TrimAndNormalize(float[] samples, int sampleRate, double thresholdDb = -35, double targetPeakDb = -3, double maxGainDb = 24)
    {
        var window = Math.Max(1, sampleRate / 100);
        var windows = samples.Length / window;
        if (windows == 0) return [];

        var rms = new double[windows];
        for (var w = 0; w < windows; w++)
        {
            double sum = 0;
            for (var i = w * window; i < (w + 1) * window; i++) sum += samples[i] * samples[i];
            rms[w] = Math.Sqrt(sum / window);
        }
        var loudest = rms.Max();
        if (loudest < 1e-4) return [];

        var threshold = loudest * Math.Pow(10, thresholdDb / 20);
        var first = Array.FindIndex(rms, x => x >= threshold);
        var last = Array.FindLastIndex(rms, x => x >= threshold);

        var pad = sampleRate / 20;
        var start = Math.Max(0, (first * window) - pad);
        var end = Math.Min(samples.Length, ((last + 1) * window) + pad);
        var clip = samples[start..end];

        var peak = clip.Max(Math.Abs);
        var gain = (float)Math.Min(Math.Pow(10, targetPeakDb / 20) / peak, Math.Pow(10, maxGainDb / 20));
        var fade = Math.Min(sampleRate / 100, clip.Length / 2);
        for (var i = 0; i < clip.Length; i++)
        {
            var f = i < fade ? i / (float)fade : i >= clip.Length - fade ? (clip.Length - 1 - i) / (float)fade : 1f;
            clip[i] *= gain * f;
        }
        return clip;
    }
}
