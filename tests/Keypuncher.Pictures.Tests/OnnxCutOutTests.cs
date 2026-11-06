using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.Pictures.Tests;

/// <summary>The real models are 400 MB and not in the repo: the test that runs one only does so when the
/// <c>KEYPUNCHER_CUTOUT_MODELS</c> variable names a folder that has them, and is reported as skipped otherwise.</summary>
public sealed class OnnxCutOutTests
{
    /// <summary>A fact that needs the quick model on disk: skipped, rather than passed without running, when the
    /// variable does not name a folder that has it.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ModelFactAttribute : FactAttribute
    {
        public static string? Folder => Environment.GetEnvironmentVariable("KEYPUNCHER_CUTOUT_MODELS");

        public ModelFactAttribute()
        {
            if (string.IsNullOrEmpty(Folder) || !File.Exists(Path.Combine(Folder, OnnxCutOut.ISNet.FileName)))
                Skip = $"KEYPUNCHER_CUTOUT_MODELS does not name a folder with {OnnxCutOut.ISNet.FileName}";
        }
    }

    [ModelFact]
    public void The_quick_model_cuts_the_sample_animal_out_of_a_white_photo()
    {
        var folder = ModelFactAttribute.Folder!;

        using var photo = SampleOnWhite();
        using var cutOut = new OnnxCutOut(OnnxCutOut.ISNet, folder);
        Assert.True(cutOut.IsAvailable);

        var result = cutOut.Cut(photo);
        using var picture = result.Picture;

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal("isnet", result.Model);
        Assert.InRange(result.Coverage, 0.2, 0.8);
        Assert.NotNull(picture);
        Assert.True(Math.Max(picture.Width, picture.Height) <= ICutOut.MaxSide);
        Assert.Equal(SKAlphaType.Unpremul, picture.AlphaType);
    }

    [Fact]
    public void Without_the_model_file_there_is_no_cut_out_and_no_error()
    {
        var folder = Directory.CreateTempSubdirectory("keypuncher-cutout-tests").FullName;
        try
        {
            using var photo = SampleOnWhite();
            using var cutOut = new OnnxCutOut(OnnxCutOut.BiRefNetLite, folder);
            Assert.False(cutOut.IsAvailable);
            Assert.Equal(Path.Combine(folder, "birefnet-general-lite.onnx"), cutOut.ModelPath);

            var result = cutOut.Cut(photo);

            Assert.False(result.Succeeded);
            Assert.Equal("birefnet-lite", result.Model);
            Assert.Equal("model missing", result.Reason);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Closed_it_is_no_longer_available_and_refuses_a_cut_without_waiting()
    {
        var folder = Directory.CreateTempSubdirectory("keypuncher-cutout-tests").FullName;
        try
        {
            using var photo = SampleOnWhite();
            var cutOut = new OnnxCutOut(OnnxCutOut.ISNet, folder);
            File.WriteAllBytes(cutOut.ModelPath, [0]);
            Assert.True(cutOut.IsAvailable);

            var started = DateTime.UtcNow;
            cutOut.Dispose();
            cutOut.Dispose();
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));

            Assert.False(cutOut.IsAvailable);
            Assert.Throws<ObjectDisposedException>(() => cutOut.Cut(photo));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void The_two_models_are_fed_as_their_makers_say()
    {
        Assert.Equal("isnet-general-use.onnx", OnnxCutOut.ISNet.FileName);
        Assert.Equal("input_image", OnnxCutOut.ISNet.InputName);
        Assert.False(OnnxCutOut.ISNet.Sigmoid);
        Assert.Equal([0.5f, 0.5f, 0.5f], OnnxCutOut.ISNet.Mean);
        Assert.Equal([1f, 1f, 1f], OnnxCutOut.ISNet.Std);
        Assert.Equal("birefnet-general-lite.onnx", OnnxCutOut.BiRefNetLite.FileName);
        Assert.Equal("input_image", OnnxCutOut.BiRefNetLite.InputName);
        Assert.True(OnnxCutOut.BiRefNetLite.Sigmoid);
        Assert.Equal([0.485f, 0.456f, 0.406f], OnnxCutOut.BiRefNetLite.Mean);
        Assert.Equal([0.229f, 0.224f, 0.225f], OnnxCutOut.BiRefNetLite.Std);
        Assert.InRange(OnnxCutOut.Threads, 1, Math.Max(1, Environment.ProcessorCount));
    }

    /// <summary>The drawn sample animal flattened onto white, cropped close so it fills a fair share of the frame.</summary>
    private static SKBitmap SampleOnWhite()
    {
        using var sample = SamplePicture.Draw();
        using var image = SKImage.FromBitmap(sample);
        var photo = new SKBitmap(new SKImageInfo(420, 350, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(photo);
        canvas.Clear(SKColors.White);
        canvas.DrawImage(image, -50, 0, SKSamplingOptions.Default);
        return photo;
    }
}
