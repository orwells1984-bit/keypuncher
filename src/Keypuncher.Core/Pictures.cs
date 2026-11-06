using System.Globalization;
using System.Text;

namespace Keypuncher.Core;

/// <summary>How realistically a thing with a picture is drawn, from its stock shape up to its photo.</summary>
public enum PictureStage
{
    /// <summary>The recipe's stock body, as if there were no picture.</summary>
    Shape,
    /// <summary>The picture's real outline, filled with the thing's palette colour and outlined like every shape.</summary>
    Silhouette,
    /// <summary>The picture reduced to a few flat colours, outlined: a picture-book drawing of the real thing.</summary>
    Flat,
    /// <summary>The picture itself, as a cut-out with a pale rim and the outline round it.</summary>
    Photo,
}

/// <summary>The file name a real thing's picture and records are kept under, shared by the renderer, the photo
/// fetcher and the batch tool.</summary>
public static class Slugs
{
    /// <summary>Lower case, words joined by hyphens, only letters and digits kept, so "Sable antelope" and
    /// "sable  antelope!" share one picture.</summary>
    public static string For(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLower(CultureInfo.InvariantCulture).Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
