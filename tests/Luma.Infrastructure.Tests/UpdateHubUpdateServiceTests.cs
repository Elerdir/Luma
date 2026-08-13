using System.Net;
using System.Text;
using Luma.Application.Updates;
using Luma.Infrastructure.Updates;

namespace Luma.Infrastructure.Tests;

/// <summary>
/// Exercises the update adapter against a real loopback HTTP server. The vendored SDK
/// builds its own HttpClient, so there is no handler to substitute — a listener on
/// localhost is the seam that is actually available.
/// </summary>
public sealed class UpdateHubUpdateServiceTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _baseUrl;
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "luma-tests", Guid.NewGuid().ToString("n"));

    private Func<HttpListenerRequest, (int Status, string Body)> _respond =
        _ => (404, "");

    public UpdateHubUpdateServiceTests()
    {
        var port = GetFreePort();
        _baseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add($"{_baseUrl}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    private static int GetFreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return; // listener closed
            }

            var (status, body) = _respond(context.Request);
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private UpdateHubUpdateService Service(string serverUrl, string version = "1.0.0")
    {
        var options = new UpdateOptions { ServerUrl = serverUrl, AppSlug = "luma" };
        return new UpdateHubUpdateService(
            new FakeSettingsStore<UpdateOptions>(options), version);
    }

    private sealed class FakeSettingsStore<T>(T value) : Luma.Application.Abstractions.ISettingsStore<T>
        where T : class, new()
    {
        public Task<T> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task SaveAsync(T settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task An_unconfigured_server_reports_no_update_without_asking_anyone()
    {
        var asked = false;
        _respond = _ => { asked = true; return (200, "{}"); };

        var result = await Service(serverUrl: "").CheckAsync();

        result.ShouldBeNull();
        asked.ShouldBeFalse();
    }

    /// <summary>
    /// A release as UpdateHub actually serves one: the artifact lives on the server's own
    /// origin and carries the checksum the server computed when it was uploaded.
    /// </summary>
    private string OfferedRelease(string? downloadUrl = null, string? sha256 = "abc123") =>
        $$"""
        {
          "has_update": true,
          "version": "1.5.0",
          "release_notes": "Fixes",
          "download_url": "{{downloadUrl ?? $"{_baseUrl}/api/downloads/1"}}",
          {{(sha256 is null ? "" : $"\"sha256\": \"{sha256}\",")}}
          "is_mandatory": true,
          "channel": "stable"
        }
        """;

    [Fact]
    public async Task A_newer_release_is_reported()
    {
        _respond = _ => (200, OfferedRelease());

        var result = await Service(_baseUrl).CheckAsync();

        result.ShouldNotBeNull();
        result.Version.ShouldBe("1.5.0");
        result.ReleaseNotes.ShouldBe("Fixes");
        result.Sha256.ShouldBe("abc123");
        result.IsMandatory.ShouldBeTrue();
    }

    // ---- What the player refuses to be offered ----
    //
    // Whatever comes back from here ends up being executed, so each of these is a hard
    // gate rather than a warning. They fail the same way every other update problem
    // does: silently, because nobody opened a video player to hear about it.

    [Fact]
    public async Task A_release_with_no_checksum_is_not_offered()
    {
        _respond = _ => (200, OfferedRelease(sha256: null));

        (await Service(_baseUrl).CheckAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task A_download_pointing_elsewhere_is_not_offered()
    {
        _respond = _ => (200, OfferedRelease(downloadUrl: "https://example.invalid/luma.msi"));

        (await Service(_baseUrl).CheckAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task A_server_reached_over_plain_http_is_not_used()
    {
        var asked = false;
        _respond = _ => { asked = true; return (200, OfferedRelease()); };

        // Not loopback, so plain HTTP is refused before anything is sent.
        var result = await Service("http://updates.example.invalid").CheckAsync();

        result.ShouldBeNull();
        asked.ShouldBeFalse();
    }

    [Fact]
    public async Task Being_up_to_date_reports_nothing()
    {
        _respond = _ => (200, """{"has_update": false, "version": "1.0.0", "channel": "stable"}""");

        var result = await Service(_baseUrl).CheckAsync();

        result.ShouldBeNull();
    }

    [Fact]
    public async Task A_release_with_no_artifact_for_this_platform_is_not_offered()
    {
        // has_update is true but there is nothing to download, so there is nothing
        // useful to show the user.
        _respond = _ => (200, """{"has_update": true, "version": "2.0.0", "channel": "stable"}""");

        var result = await Service(_baseUrl).CheckAsync();

        result.ShouldBeNull();
    }

    [Fact]
    public async Task A_server_error_is_swallowed()
    {
        _respond = _ => (500, "boom");

        var result = await Service(_baseUrl).CheckAsync();

        result.ShouldBeNull();
    }

    [Fact]
    public async Task An_unreachable_server_is_swallowed()
    {
        // Nothing is listening on this port.
        var result = await Service("http://127.0.0.1:1").CheckAsync();

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Malformed_json_is_swallowed()
    {
        _respond = _ => (200, "not json at all");

        var result = await Service(_baseUrl).CheckAsync();

        result.ShouldBeNull();
    }

    [Fact]
    public async Task The_current_version_channel_and_slug_reach_the_server()
    {
        string? query = null;
        string? path = null;
        _respond = request =>
        {
            path = request.Url?.AbsolutePath;
            query = request.Url?.Query;
            return (200, """{"has_update": false, "version": "1.0.0", "channel": "stable"}""");
        };

        var options = new UpdateOptions { ServerUrl = _baseUrl, AppSlug = "luma", Channel = "beta" };
        await new UpdateHubUpdateService(new FakeSettingsStore<UpdateOptions>(options), "1.2.3")
            .CheckAsync();

        path.ShouldBe("/api/apps/luma/update");
        query.ShouldNotBeNull();
        query.ShouldContain("version=1.2.3");
        query.ShouldContain("channel=beta");
    }

    public void Dispose()
    {
        _listener.Close();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
