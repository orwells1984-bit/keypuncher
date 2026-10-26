using System.Diagnostics;

namespace Keypuncher.Rendering;

/// <summary>
/// Milliseconds a frame spends on each part of drawing, averaged over the frames since the last <see cref="Reset"/>:
/// the backdrop, the history layer, the things and effects, and the letters, words and overlays on top.
/// </summary>
public sealed class RenderTimings
{
    private long _backdrop, _history, _things, _text;

    public int Frames { get; private set; }

    public double Backdrop => Average(_backdrop);
    public double History => Average(_history);
    public double Things => Average(_things);
    public double Text => Average(_text);
    public double Total => Backdrop + History + Things + Text;

    public void Reset() => (_backdrop, _history, _things, _text, Frames) = (0, 0, 0, 0, 0);

    public override string ToString() =>
        $"{Total:0.0} ms a frame: backdrop {Backdrop:0.0}, history {History:0.0}, things {Things:0.0}, text {Text:0.0}";

    internal void Add(long start, long backdrop, long history, long things, long end)
    {
        _backdrop += backdrop - start;
        _history += history - backdrop;
        _things += things - history;
        _text += end - things;
        Frames++;
    }

    private double Average(long ticks) => Frames == 0 ? 0 : ticks * 1000.0 / Stopwatch.Frequency / Frames;
}
