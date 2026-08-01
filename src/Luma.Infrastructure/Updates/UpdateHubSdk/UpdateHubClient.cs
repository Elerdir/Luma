using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace UpdateHub.Client;

/// <summary>
/// Client for checking and downloading application updates from an UpdateHub server.
/// </summary>
/// <example>
/// <code>
/// var client = new UpdateHubClient("https://updates.example.com", "my-app");
/// var result = await client.CheckForUpdateAsync("1.0.0");
/// if (result.HasUpdate)
///     Console.WriteLine($"New version available: {result.LatestVersion}");
/// </code>
/// </example>
public sealed class UpdateHubClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _appSlug;
    private readonly bool _ownsHttpClient;

    /// <summary>Creates a client that manages its own <see cref="HttpClient"/>.</summary>
    /// <param name="serverUrl">Base URL of the UpdateHub server, e.g. https://updates.example.com</param>
    /// <param name="appSlug">The application slug as registered in UpdateHub.</param>
    public UpdateHubClient(string serverUrl, string appSlug)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"),
            Timeout     = TimeSpan.FromSeconds(30)
        };
        _appSlug        = appSlug;
        _ownsHttpClient = true;
    }

    /// <summary>Creates a client using an externally managed <see cref="HttpClient"/> (e.g. from IHttpClientFactory).</summary>
    public UpdateHubClient(HttpClient httpClient, string serverUrl, string appSlug)
    {
        _http           = httpClient;
        _http.BaseAddress ??= new Uri(serverUrl.TrimEnd('/') + "/");
        _appSlug        = appSlug;
        _ownsHttpClient = false;
    }

    /// <summary>
    /// Checks whether a newer version is available. Automatically detects the current OS and architecture.
    /// </summary>
    /// <param name="currentVersion">The version string of the running app, e.g. "1.0.0".</param>
    /// <param name="channel">Update channel: "stable" (default), "beta", or "alpha".</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<UpdateCheckResult> CheckForUpdateAsync(
        string currentVersion,
        string channel = "stable",
        CancellationToken ct = default) =>
        CheckForUpdateAsync(currentVersion, CurrentPlatform, CurrentArch, channel, ct);

    /// <summary>
    /// Checks whether a newer version is available for a specific platform and architecture.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        string currentVersion,
        string platform,
        string arch,
        string channel = "stable",
        CancellationToken ct = default)
    {
        var url = $"api/apps/{Uri.EscapeDataString(_appSlug)}/update" +
                  $"?version={Uri.EscapeDataString(currentVersion)}" +
                  $"&platform={Uri.EscapeDataString(platform)}" +
                  $"&arch={Uri.EscapeDataString(arch)}" +
                  $"&channel={Uri.EscapeDataString(channel)}";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new UpdateHubException($"Could not reach UpdateHub server: {ex.Message}", ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new UpdateCheckResult(currentVersion);

        if (!response.IsSuccessStatusCode)
            throw new UpdateHubException($"UpdateHub returned HTTP {(int)response.StatusCode}");

        var dto = await response.Content.ReadFromJsonAsync<UpdateCheckResponse>(ct)
            ?? throw new UpdateHubException("Server returned an empty response.");

        return new UpdateCheckResult(dto);
    }

    /// <summary>
    /// Downloads an artifact to a local file path.
    /// </summary>
    /// <param name="downloadUrl">The URL from <see cref="UpdateCheckResult.DownloadUrl"/>.</param>
    /// <param name="destinationPath">Where to save the file.</param>
    /// <param name="progress">Reports download progress as a value from 0.0 to 1.0.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved destination path.</returns>
    public async Task<string> DownloadAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(
            downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
            throw new UpdateHubException($"Download failed with HTTP {(int)response.StatusCode}");

        var total = response.Content.Headers.ContentLength ?? -1L;
        var dir   = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var dest   = File.Create(destinationPath);
        await using var source = await response.Content.ReadAsStreamAsync(ct);

        var buffer     = new byte[81920];
        long downloaded = 0;
        int  read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (total > 0) progress?.Report((double)downloaded / total);
        }

        return destinationPath;
    }

    /// <summary>
    /// Verifies that a downloaded file matches the expected SHA-256 hash.
    /// </summary>
    /// <param name="filePath">Path to the downloaded file.</param>
    /// <param name="expectedSha256">The hex string from <see cref="UpdateCheckResult.Sha256"/>.</param>
    public static bool VerifySha256(string filePath, string expectedSha256)
    {
        using var stream = File.OpenRead(filePath);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return hash == expectedSha256.ToLowerInvariant();
    }

    /// <summary>The current OS as an UpdateHub platform string.</summary>
    public static string CurrentPlatform =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS()   ? "macos"   : "linux";

    /// <summary>The current CPU architecture as an UpdateHub arch string.</summary>
    public static string CurrentArch =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64   => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86   => "x86",
            _ => "x64"
        };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
