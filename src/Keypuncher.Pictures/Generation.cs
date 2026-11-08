using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Keypuncher.Director;
using Keypuncher.Core;

namespace Keypuncher.Pictures;

/// <summary>What the image model is asked for: one whole thing on a plain white background, so the cut-out has an
/// easy job and the toy gets a picture-book figure rather than a scene.</summary>
public static class GenerationPrompt
{
    public static string For(string name, RealCategory category) => category switch
    {
        RealCategory.ExtinctAnimal => $"a realistic life restoration of one whole {name}, full body, side view, plain white background, museum illustration style, centered",
        RealCategory.Plant => $"a photo of one whole {name} plant, entire plant visible, plain white background, centered",
        RealCategory.Vehicle or RealCategory.Object => $"a photo of one {name}, whole, three-quarter view, plain white background, product photo, centered",
        _ => $"a photo of one whole {name}, full body, side view, standing on plain white background, studio lighting, sharp, centered",
    };
}

/// <summary>A picture the image model made, as PNG bytes, and how it was made.</summary>
public sealed record MadeImage(byte[] Png, GenerationProvenance Provenance);

/// <summary>Makes a picture from a prompt with the local image model; the maker downloads nothing and the toy
/// itself never calls it, since it takes the graphics card for half a minute.</summary>
public interface IImageMaker
{
    /// <summary>Which model this is, for the sidecar: "Z-Image-Turbo".</summary>
    string Name { get; }

    /// <summary>False while the program or its weights are not on disk.</summary>
    bool IsAvailable { get; }

    /// <summary>Makes one picture; null, with the reason, when the model could not.</summary>
    Task<(MadeImage? Image, string? Reason)> MakeAsync(string prompt, long seed, CancellationToken ct = default);
}

/// <summary>Where stable-diffusion.cpp and the Z-Image-Turbo weights live, and the settings the bench found to fit
/// a 6 GB card: 512 px, 8 steps, no guidance, the weights streamed from main memory under a budget.</summary>
public sealed record SdCliSetup(string Exe, string DiffusionModel, string TextEncoder, string Vae)
{
    public const string ModelName = "Z-Image-Turbo";
    public int Steps { get; init; } = 8;
    public float Cfg { get; init; } = 1.0f;
    public int Side { get; init; } = 512;
    /// <summary>GiB of the card the weights may take at once; the rest streams from main memory. Null lets sd-cli decide.</summary>
    public double? MaxVramGiB { get; init; } = 2.75;
    public bool VaeTiling { get; init; }
    public bool VaeOnCpu { get; init; }

    public bool AllPresent => File.Exists(Exe) && File.Exists(DiffusionModel) && File.Exists(TextEncoder) && File.Exists(Vae);

    /// <summary>What is missing, by name, for the grown-ups' page and the console.</summary>
    public IEnumerable<string> Missing()
    {
        if (!File.Exists(Exe)) yield return Path.GetFileName(Exe);
        if (!File.Exists(DiffusionModel)) yield return Path.GetFileName(DiffusionModel);
        if (!File.Exists(TextEncoder)) yield return Path.GetFileName(TextEncoder);
        if (!File.Exists(Vae)) yield return Path.GetFileName(Vae);
    }

    /// <summary>Looks for the pieces under a folder: <c>sd-cli.exe</c> (or <c>sd-cli</c>) in it or its <c>sd/</c>,
    /// and in it or its <c>models/</c> a <c>z_image_turbo*.gguf</c>, a <c>Qwen3*.gguf</c> and <c>ae.safetensors</c>.
    /// Pieces not found keep their expected path, so <see cref="Missing"/> can name them.</summary>
    public static SdCliSetup Find(string folder)
    {
        string First(string[] patterns, string fallback)
        {
            foreach (var dir in new[] { folder, Path.Combine(folder, "sd"), Path.Combine(folder, "models") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var pattern in patterns)
                    if (Directory.EnumerateFiles(dir, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault() is { } found) return found;
            }
            return fallback;
        }
        return new SdCliSetup(
            First(["sd-cli.exe", "sd-cli"], Path.Combine(folder, "sd", "sd-cli.exe")),
            First(["z_image_turbo*.gguf"], Path.Combine(folder, "models", "z_image_turbo-Q4_K.gguf")),
            First(["Qwen3*.gguf"], Path.Combine(folder, "models", "Qwen3-4B-Instruct-2507-Q4_K_M.gguf")),
            First(["ae.safetensors", "ae.sft"], Path.Combine(folder, "models", "ae.safetensors")));
    }

    /// <summary>The command line for one picture, as sd-cli wants it; the budget is written with a dot whatever the locale.</summary>
    public IReadOnlyList<string> Arguments(string prompt, long seed, string outPath)
    {
        var args = new List<string>
        {
            "--diffusion-model", DiffusionModel, "--llm", TextEncoder, "--vae", Vae,
            "-p", prompt, "--cfg-scale", Cfg.ToString("0.0", CultureInfo.InvariantCulture), "--steps", Steps.ToString(CultureInfo.InvariantCulture),
            "-W", Side.ToString(CultureInfo.InvariantCulture), "-H", Side.ToString(CultureInfo.InvariantCulture),
            "--offload-to-cpu", "--diffusion-fa", "--seed", seed.ToString(CultureInfo.InvariantCulture), "-o", outPath, "-v",
        };
        if (MaxVramGiB is { } budget) { args.Add("--max-vram"); args.Add(budget.ToString("0.##", CultureInfo.InvariantCulture)); }
        if (VaeTiling) args.Add("--vae-tiling");
        if (VaeOnCpu) args.Add("--vae-on-cpu");
        return args;
    }

    /// <summary>The setup sized to the card's free memory right now: the budget is what is free less room for the
    /// model's working memory, and below 2.5 GiB the decoder is tiled and kept on the CPU.</summary>
    public SdCliSetup FittedTo(int freeMiB)
    {
        var budget = Vram.Budget(freeMiB);
        return this with { MaxVramGiB = budget, VaeTiling = budget < 2.5, VaeOnCpu = budget < 2.5 };
    }
}

/// <summary>The card's free memory, as nvidia-smi reports it, and the share of it the image model may hold.</summary>
public static partial class Vram
{
    /// <summary>Room the model needs beyond its weights (working memory and a margin), in MiB.</summary>
    public const int Reserve = 1600;

    /// <summary>Free memory in MiB, or null when nvidia-smi is not there or says nothing useful.</summary>
    public static int? FreeMiB()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.free --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000)) { process.Kill(); return null; }
            return Parse(output);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Log.Debug("gpu", "nvidia-smi could not be asked for the card's free memory", e);
            return null;
        }
    }

    /// <summary>The first number in nvidia-smi's answer ("4592" or "4592 MiB"), or null.</summary>
    public static int? Parse(string? output) =>
        output is not null && Number().Match(output) is { Success: true } m && int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>The weights budget in GiB for this much free memory: what is left after <see cref="Reserve"/>, in
    /// quarter steps, between 1 and 3.75.</summary>
    public static double Budget(int freeMiB)
    {
        var gib = (freeMiB - Reserve) / 1024.0;
        return Math.Clamp(Math.Floor(gib * 4) / 4, 1.0, 3.75);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}

/// <summary>Runs stable-diffusion.cpp's <c>sd-cli</c> once per picture. Each run loads the weights afresh (half a
/// minute on a 6 GB card), so this is for the nightly batch, never for play. A run that fails for want of memory is
/// tried once more with a smaller budget.</summary>
public sealed class SdCli(SdCliSetup setup, Func<int?>? freeVram = null, Action<string>? note = null) : IImageMaker
{
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

    public string Name => SdCliSetup.ModelName;

    public bool IsAvailable => setup.AllPresent;

    public SdCliSetup Setup => setup;

    public async Task<(MadeImage? Image, string? Reason)> MakeAsync(string prompt, long seed, CancellationToken ct = default)
    {
        if (!IsAvailable) return (null, $"image model missing: {string.Join(", ", setup.Missing())}");
        var fitted = (freeVram ?? Vram.FreeMiB)() is { } free ? setup.FittedTo(free) : setup;
        var outPath = Path.Combine(Path.GetTempPath(), $"keypuncher-gen-{Environment.ProcessId}-{seed}.png");
        try
        {
            var (ok, log) = await RunAsync(fitted, prompt, seed, outPath, ct);
            if (!ok && OutOfMemory(log) && fitted.MaxVramGiB is { } budget && budget > 1.0)
            {
                var smaller = fitted with { MaxVramGiB = Math.Max(1.0, budget - 0.5), VaeTiling = true, VaeOnCpu = true };
                note?.Invoke($"out of memory at {budget:0.##} GiB; trying {smaller.MaxVramGiB:0.##}");
                (ok, log) = await RunAsync(smaller, prompt, seed, outPath, ct);
                fitted = smaller;
            }
            if (!ok || !File.Exists(outPath)) return (null, OutOfMemory(log) ? "out of memory on the card" : $"sd-cli failed: {LastLine(log)}");
            var png = await File.ReadAllBytesAsync(outPath, ct);
            return (new MadeImage(png, new GenerationProvenance(Name, Path.GetFileName(fitted.DiffusionModel), prompt, seed, fitted.Steps, fitted.Cfg)), null);
        }
        finally
        {
            try { File.Delete(outPath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn("pictures", $"could not delete {outPath}", e); }
        }
    }

    private static async Task<(bool Ok, string Log)> RunAsync(SdCliSetup s, string prompt, long seed, string outPath, CancellationToken ct)
    {
        var info = new ProcessStartInfo(s.Exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(s.Exe) ?? Environment.CurrentDirectory,
        };
        foreach (var arg in s.Arguments(prompt, seed, outPath)) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Limit);
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            Log.Warn("pictures", $"sd-cli was stopped {(ct.IsCancellationRequested ? "as asked" : "after five minutes")}");
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException e) { Log.Debug("pictures", "sd-cli had already ended", e); }
            return (false, "took longer than five minutes");
        }
        var log = await output + "\n" + await errors;
        return (process.ExitCode == 0, log);
    }

    /// <summary>Whether sd-cli's log says the card ran out of memory, in any of the ways it puts it.</summary>
    public static bool OutOfMemory(string log) =>
        log.Contains("out of memory", StringComparison.OrdinalIgnoreCase) || log.Contains("cannot make enough memory", StringComparison.OrdinalIgnoreCase)
        || log.Contains("workspace capacity", StringComparison.OrdinalIgnoreCase) || log.Contains("cudaMalloc", StringComparison.OrdinalIgnoreCase)
        || log.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase);

    private static string LastLine(string log) =>
        log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(l => l.Contains("[E]") || l.Contains("error", StringComparison.OrdinalIgnoreCase))
        ?? log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "no output";
}
