namespace Luma.Infrastructure.Updates;

/// <summary>
/// Puts a limit on what a reply from the update server is allowed to be.
///
/// The rules in <see cref="UpdateSafety"/> all answer "is this the right file, from the
/// right place, going forwards?" — and none of them can be asked until the file is on
/// disk. A server that simply never stops sending is therefore unopposed: the copy runs
/// until the disk is full, with the banner still reading "Downloading…".
///
/// The timeout on <see cref="HttpClient"/> does not cover it. Both calls here read
/// headers first and stream the body afterwards, and that timeout stops at the headers.
///
/// So the limits sit at the transport, where they apply to every response without the
/// callers having to remember them — and, just as usefully, outside the vendored SDK,
/// which is a verbatim copy that has to stay one (see UpdateHubSdk/README.md).
/// </summary>
/// <param name="inner">
/// The handler underneath. Supplied only by tests, which have no use for a socket; left
/// alone it is an <see cref="HttpClientHandler"/> that does not follow redirects. The
/// download URL is checked against the configured server, and a reply free to redirect
/// anywhere makes that check a statement about a string rather than about where the
/// bytes came from — and UpdateHub serves artifacts from its own origin, so there is
/// nothing legitimate to follow.
/// </param>
internal sealed class BoundedHttpHandler(
    long maxBytes, TimeSpan stallTimeout, HttpMessageHandler? inner = null)
    : DelegatingHandler(inner ?? new HttpClientHandler { AllowAutoRedirect = false })
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Content-Length is a claim, not a promise, so it is worth acting on early and
        // worth not trusting afterwards: a body that announces more than the limit is
        // refused before a byte of it is written, and one that announces less is still
        // read through the counting stream below.
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new HttpRequestException(
                $"The update server offered {response.Content.Headers.ContentLength} bytes, " +
                $"more than the {maxBytes} this client will accept.");

        var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var bounded = new StreamContent(new BoundedStream(body, maxBytes, stallTimeout));

        // Carried over, or progress reporting loses the total it divides by.
        foreach (var header in response.Content.Headers)
            bounded.Headers.TryAddWithoutValidation(header.Key, header.Value);

        response.Content = bounded;
        return response;
    }

    /// <summary>
    /// A read-only stream that stops at a byte count, and stops waiting after a while.
    /// Both failures are thrown rather than reported as the end of the stream: a partial
    /// file that looks complete would fail the hash check, which is a confusing way to
    /// learn that the server misbehaved.
    /// </summary>
    private sealed class BoundedStream(Stream inner, long maxBytes, TimeSpan stallTimeout) : Stream
    {
        private long _read;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(stallTimeout);

            int read;
            try
            {
                read = await inner.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException(
                    $"The update server sent nothing for {stallTimeout.TotalSeconds:0} seconds.");
            }

            _read += read;

            if (_read > maxBytes)
                throw new IOException(
                    $"The update server sent more than the {maxBytes} bytes this client will accept.");

            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
