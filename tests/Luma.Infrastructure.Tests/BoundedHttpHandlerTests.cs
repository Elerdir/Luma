using System.Net;
using System.Net.Http;
using Luma.Infrastructure.Updates;

namespace Luma.Infrastructure.Tests;

/// <summary>
/// The limits that stand between a misbehaving update server and the disk. Every rule in
/// UpdateSafety can only be asked once the file has arrived; these are the ones that
/// decide whether it ever finishes arriving.
/// </summary>
public sealed class BoundedHttpHandlerTests
{
    private static HttpClient ClientOver(HttpContent content, long maxBytes, TimeSpan stall) =>
        new(new BoundedHttpHandler(maxBytes, stall, new StubHandler(content)));

    [Fact]
    public async Task A_body_within_the_limit_arrives_whole()
    {
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);

        using var client = ClientOver(new ByteArrayContent(payload), 8192, TimeSpan.FromSeconds(5));

        var received = await client.GetByteArrayAsync("https://updates.example.com/a");

        received.ShouldBe(payload);
    }

    /// <summary>
    /// A body that announces its size is refused before a byte of it is written — there
    /// is no reason to spend the disk to find out what was already declared.
    /// </summary>
    [Fact]
    public async Task A_declared_size_over_the_limit_is_refused_up_front()
    {
        using var client = ClientOver(new ByteArrayContent(new byte[5000]), 1024, TimeSpan.FromSeconds(5));

        await Should.ThrowAsync<HttpRequestException>(
            () => client.GetByteArrayAsync("https://updates.example.com/a"));
    }

    /// <summary>
    /// And one that declares nothing — which is the case the limit is really for, since a
    /// server that means to fill the disk has no reason to announce it.
    /// </summary>
    [Fact]
    public async Task An_undeclared_body_is_cut_off_at_the_limit()
    {
        using var client = ClientOver(new StreamContent(new EndlessStream()), 64 * 1024, TimeSpan.FromSeconds(5));

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => client.GetByteArrayAsync("https://updates.example.com/a"));

        thrown.InnerException.ShouldBeOfType<IOException>();
    }

    /// <summary>
    /// Silence is the other way a download never ends. HttpClient's own timeout stops at
    /// the headers when the body is streamed, so this is the only thing watching.
    /// </summary>
    [Fact]
    public async Task A_body_that_goes_quiet_is_given_up_on()
    {
        using var client = ClientOver(
            new StreamContent(new SilentStream()), 1024 * 1024, TimeSpan.FromMilliseconds(150));

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => client.GetByteArrayAsync("https://updates.example.com/a"));

        thrown.InnerException.ShouldBeOfType<IOException>();
    }

    /// <summary>
    /// The download URL is checked against the configured server; following a redirect
    /// would make that a statement about a string rather than about where the bytes came
    /// from. A redirect is surfaced as itself instead.
    /// </summary>
    [Fact]
    public void The_handler_does_not_follow_redirects()
    {
        var handler = new BoundedHttpHandler(1024, TimeSpan.FromSeconds(1));

        handler.InnerHandler.ShouldBeOfType<HttpClientHandler>()
            .AllowAutoRedirect.ShouldBeFalse();
    }

    private sealed class StubHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    /// <summary>A body that never ends and never pauses.</summary>
    private sealed class EndlessStream : ReadOnlyStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            buffer.Span.Fill(0x2A);
            return ValueTask.FromResult(buffer.Length);
        }
    }

    /// <summary>A body that opens and then says nothing.</summary>
    private sealed class SilentStream : ReadOnlyStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
    }

    private abstract class ReadOnlyStream : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
