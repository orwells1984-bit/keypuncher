using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace Keypuncher.Pictures;

/// <summary>How to feed one background-removal model and read its answer.</summary>
/// <param name="Name">What the sidecar calls it: "isnet", "birefnet-lite".</param>
/// <param name="FileName">The file in the models folder.</param>
/// <param name="InputName">The graph's input: a float [1, 3, 1024, 1024] in RGB order.</param>
/// <param name="Mean">Taken from each channel after dividing by 255.</param>
/// <param name="Std">Each channel is then divided by this.</param>
/// <param name="Sigmoid">True when the output is logits that still need a sigmoid; false when the graph already made a
/// 0..1 matte, which is then stretched to the full range.</param>
public sealed record ModelSpec(string Name, string FileName, string InputName, float[] Mean, float[] Std, bool Sigmoid = false);

/// <summary>
/// A cut-out made by a background-removal model on the CPU: the photo is letterboxed onto a mid-grey square the size
/// the model wants, the model says for every pixel how much of it is the thing, and <see cref="CutOutPost"/> turns
/// that into the picture. The model is read from disk the first time it is needed, which takes a second or two, and
/// runs one photo at a time; <see cref="IsAvailable"/> is false until a grown-up has downloaded it.
/// </summary>
public sealed class OnnxCutOut : ICutOut, IDisposable
{
    /// <summary>The square the models take.</summary>
    public const int Side = 1024;

    /// <summary>The quick general-purpose model, under a second a photo.</summary>
    public static readonly ModelSpec ISNet = new("isnet", "isnet-general-use.onnx", "input_image", [0.5f, 0.5f, 0.5f], [1f, 1f, 1f]);

    /// <summary>The slower, finer model tried when the quick one's matte fails its gate.</summary>
    public static readonly ModelSpec BiRefNetLite = new("birefnet-lite", "birefnet-general-lite.onnx", "input_image",
        [0.485f, 0.456f, 0.406f], [0.229f, 0.224f, 0.225f], Sigmoid: true);

    private static readonly SKColor Grey = new(128, 128, 128);
    private readonly ModelSpec _spec;
    private readonly Lock _lock = new();
    private InferenceSession? _session;
    private float[]? _input;
    private volatile bool _disposed;

    public OnnxCutOut(ModelSpec spec, string modelsFolder)
    {
        _spec = spec;
        ModelPath = Path.Combine(modelsFolder, spec.FileName);
    }

    public string Name => _spec.Name;

    /// <summary>Where the model file is looked for.</summary>
    public string ModelPath { get; }

    /// <summary>False while the model file is not on disk, and once this has been closed.</summary>
    public bool IsAvailable => !_disposed && File.Exists(ModelPath);

    /// <summary>Threads for one run: half the machine less one, so the toy and the language model keep breathing.</summary>
    public static int Threads => Math.Max(1, (Environment.ProcessorCount / 2) - 1);

    public CutOutResult Cut(SKBitmap photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable) return new CutOutResult(null, Name, 0, "model missing");
        if (photo.Width == 0 || photo.Height == 0) return new CutOutResult(null, Name, 0, "too small");

        var matte = new byte[photo.Width * photo.Height];
        lock (_lock)
        {
            // Dispose never waits for a cut under way: a cut that arrives after it must not open the model again, and
            // the cut it found running closes the model itself when it is done.
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                _session ??= Open();
                var input = _input ??= new float[3 * Side * Side];
                var (left, top, width, height) = Letterbox(photo, input);
                var shape = new long[] { 1, 3, Side, Side };
                using var tensor = OrtValue.CreateTensorValueFromMemory(OrtMemoryInfo.DefaultInstance, input.AsMemory(), shape);
                using var run = new RunOptions();
                using var outputs = _session.Run(run, new Dictionary<string, OrtValue> { [InputName(_session)] = tensor }, [_session.OutputNames[0]]);
                var output = outputs[0].GetTensorDataAsSpan<float>();
                ReadMatte(output, left, top, width, height, photo.Width, photo.Height, matte);
            }
            finally { if (_disposed) Close(); }
        }
        var picture = CutOutPost.Finish(photo, matte, out var coverage, out var reason);
        return new CutOutResult(picture, Name, coverage, reason);
    }

    /// <summary>Closes the model without waiting: a cut under way closes it as it finishes instead, so the toy's exit
    /// never hangs on a model run.</summary>
    public void Dispose()
    {
        _disposed = true;
        if (!_lock.TryEnter()) return;
        try { Close(); }
        finally { _lock.Exit(); }
    }

    private void Close()
    {
        _session?.Dispose();
        _session = null;
        _input = null;
    }

    private InferenceSession Open()
    {
        using var options = new SessionOptions { IntraOpNumThreads = Threads, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        return new InferenceSession(ModelPath, options);
    }

    /// <summary>The spec's input name when the graph has it, else the graph's first input, so an export under another
    /// name still runs.</summary>
    private string InputName(InferenceSession session) =>
        session.InputNames.Contains(_spec.InputName) ? _spec.InputName : session.InputNames[0];

    /// <summary>Draws the photo scaled to fit, centred on grey, and writes it into the input planes; returns where the
    /// photo landed on the square.</summary>
    private (int Left, int Top, int Width, int Height) Letterbox(SKBitmap photo, float[] input)
    {
        var k = Side / (float)Math.Max(photo.Width, photo.Height);
        var width = Math.Max(1, (int)Math.Round(photo.Width * k));
        var height = Math.Max(1, (int)Math.Round(photo.Height * k));
        var left = (Side - width) / 2;
        var top = (Side - height) / 2;
        using var square = new SKBitmap(new SKImageInfo(Side, Side, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(square))
        using (var image = SKImage.FromBitmap(photo))
        {
            canvas.Clear(Grey);
            canvas.DrawImage(image, new SKRect(left, top, left + width, top + height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        var pixels = square.GetPixelSpan();
        const int plane = Side * Side;
        var (mean, std) = (_spec.Mean, _spec.Std);
        for (var y = 0; y < Side; y++)
        {
            var row = pixels.Slice(y * square.RowBytes, Side * 4);
            var at = y * Side;
            for (var x = 0; x < Side; x++, at++)
            {
                var p = x * 4;
                input[at] = ((row[p] / 255f) - mean[0]) / std[0];
                input[plane + at] = ((row[p + 1] / 255f) - mean[1]) / std[1];
                input[(2 * plane) + at] = ((row[p + 2] / 255f) - mean[2]) / std[2];
            }
        }
        return (left, top, width, height);
    }

    /// <summary>Takes the model's square answer back to the photo: a sigmoid or a stretch to 0..1, then the part under
    /// the photo resampled to the photo's size.</summary>
    private void ReadMatte(ReadOnlySpan<float> output, int left, int top, int width, int height, int photoWidth, int photoHeight, byte[] matte)
    {
        float scale = 1, shift = 0;
        if (!_spec.Sigmoid)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in output) { if (v < min) min = v; if (v > max) max = v; }
            if (max > min) { scale = 1 / (max - min); shift = min; }
        }
        // Bilinear from the photo's part of the square; the square is at least as fine as the photo when the photo is
        // smaller than the square, and a little coarser otherwise, which the toy's 512 px picture never shows.
        var kx = width / (float)photoWidth;
        var ky = height / (float)photoHeight;
        for (var y = 0; y < photoHeight; y++)
        {
            var sy = Math.Clamp(top + ((y + 0.5f) * ky) - 0.5f, top, top + height - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, top + height - 1);
            var fy = sy - y0;
            for (var x = 0; x < photoWidth; x++)
            {
                var sx = Math.Clamp(left + ((x + 0.5f) * kx) - 0.5f, left, left + width - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, left + width - 1);
                var fx = sx - x0;
                var v = (Value(output, x0, y0) * (1 - fx) * (1 - fy)) + (Value(output, x1, y0) * fx * (1 - fy))
                      + (Value(output, x0, y1) * (1 - fx) * fy) + (Value(output, x1, y1) * fx * fy);
                var m = _spec.Sigmoid ? 1 / (1 + MathF.Exp(-v)) : (v - shift) * scale;
                matte[(y * photoWidth) + x] = (byte)Math.Clamp((int)MathF.Round(m * 255), 0, 255);
            }
        }
    }

    private static float Value(ReadOnlySpan<float> output, int x, int y) => output[(y * Side) + x];
}
