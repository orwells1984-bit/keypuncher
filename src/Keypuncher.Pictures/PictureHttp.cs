using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Keypuncher.Pictures;

/// <summary>
/// How the picture fetcher talks to the web: one polite client that says who it is, accepts gzip, gives up on a
/// request after <see cref="RequestTimeout"/> and never reads more than <see cref="MaxDownload"/> of a picture.
/// A 404 means "there is no such thing" and comes back as null; any other failure throws, so the caller can tell
/// "nothing there" from "no network".
/// </summary>
public static class PictureHttp
{
    public const string UserAgent = "Keypuncher/0.1 (https://github.com/orwells1984-bit/keypuncher) dotnet-HttpClient/10";
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    public const long MaxDownload = 10L * 1024 * 1024;

    /// <summary>A client set up for the Wikimedia and iNaturalist APIs; a test passes its own handler.</summary>
    public static HttpClient NewClient(HttpMessageHandler? handler = null)
    {
        var client = handler is null
            ? new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true })
            : new HttpClient(handler);
        client.Timeout = RequestTimeout;
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        return client;
    }

    /// <summary>GETs a JSON answer. Null when the server says 404; throws <see cref="HttpRequestException"/> for
    /// any other refusal and <see cref="OperationCanceledException"/> when the request times out.</summary>
    public static async Task<JsonDocument?> GetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    /// <summary>Downloads a picture whole. Null when it is not there or is bigger than <see cref="MaxDownload"/>;
    /// throws for a network failure.</summary>
    public static async Task<(byte[] Bytes, string? Mime)?> DownloadAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxDownload) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxDownload) return null;
            buffer.Write(chunk, 0, read);
        }
        return (buffer.ToArray(), response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Whether an exception means the network, not the content, let us down.</summary>
    public static bool IsNetworkTrouble(Exception e, CancellationToken callerToken) =>
        e is HttpRequestException || e is IOException || (e is OperationCanceledException && !callerToken.IsCancellationRequested);
}
