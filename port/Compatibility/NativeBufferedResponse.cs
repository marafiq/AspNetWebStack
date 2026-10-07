using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.Web;
using MvcNet10Host;

namespace AspNetWebStack.Native;

// Legacy response surface: public construction retains bounded text behavior.
// The native application opts into bounded native file spooling before publication.
public sealed class NativeBufferedResponse : HttpResponseBase
{
    private readonly BufferedResponse _inner;
    private readonly NativeRequestLifetime _lifetime;
    private readonly int _fileLimit, _fileThreshold;
    private readonly string _tempDirectory;
    private FileBufferingWriteStream _file;
    private Stream _outputStream;
    private bool _completed, _skipCustomErrors;
    internal string DownloadDisposition { get; private set; }
    internal bool IsBinary => _file != null;
    public NativeBufferedResponse(CancellationToken cancellationToken = default) { _inner = new BufferedResponse(cancellationToken); }
    internal NativeBufferedResponse(CancellationToken cancellationToken, NativeRequestLifetime lifetime,
        int fileLimit, int fileThreshold, string tempDirectory)
    {
        _lifetime = lifetime; _fileLimit = fileLimit; _fileThreshold = fileThreshold; _tempDirectory = tempDirectory;
        _inner = new BufferedResponse(cancellationToken, lifetime.CheckIdentity, () =>
        { if (_file != null) Fail(new PlatformNotSupportedException("A binary file response cannot mix text writes.")); });
    }
    public string Location { get; private set; }
    public byte[] GetBytes()
    {
        if (_file != null) throw new PlatformNotSupportedException("The host drains binary file responses asynchronously.");
        return _inner.GetBytes();
    }
    public void Complete() { _inner.Complete(); _completed = true; }
    internal long BufferedLength => _file?.Length ?? _inner.GetBytes().LongLength;
    internal async Task DrainAsync(Stream destination, CancellationToken cancellationToken)
    {
        _lifetime.CheckPublication();
        if (_file != null) await _file.DrainBufferAsync(destination, cancellationToken);
        else throw new InvalidOperationException("Only the file buffer uses this drain path.");
    }
    internal ValueTask DisposeFileAsync() => _file?.DisposeAsync() ?? default;
    private void CheckWrite()
    {
        _inner.Output.Flush(); // Existing owner, completion and abort checks.
        ObjectDisposedException.ThrowIf(_completed, this);
    }
    private void Fail(Exception error) { _lifetime?.MarkFileFailure(error); throw error; }
    public override Stream OutputStream
    {
        get
        {
            if (_lifetime == null) return base.OutputStream;
            CheckWrite();
            if (_file == null)
            {
                if (_inner.GetBytes().Length != 0) Fail(new PlatformNotSupportedException("A binary file response cannot follow text writes."));
                _file = new FileBufferingWriteStream(_fileThreshold, _fileLimit, () => _tempDirectory);
                _outputStream = new FileWriter(this);
            }
            return _outputStream;
        }
    }
    public override void AddHeader(string name, string value)
    {
        if (_lifetime == null) { base.AddHeader(name, value); return; }
        CheckWrite();
        if (!String.Equals(name, "Content-Disposition", StringComparison.OrdinalIgnoreCase) || DownloadDisposition != null ||
            !ContentDispositionHeaderValue.TryParse(value, out _) || Array.Exists(value.ToCharArray(), c => c < ' ' || c == 127))
            Fail(new PlatformNotSupportedException("Only one valid buffered Content-Disposition header is supported."));
        DownloadDisposition = value;
    }
    public override void TransmitFile(string filename)
    {
        if (_lifetime == null) { base.TransmitFile(filename); return; }
        CheckWrite();
        if (String.IsNullOrEmpty(filename) || !Path.IsPathFullyQualified(filename))
            Fail(new PlatformNotSupportedException("File paths must be absolute physical paths for the native platform."));
        using var source = File.OpenRead(filename);
        source.CopyTo(OutputStream);
    }
    private sealed class FileWriter(NativeBufferedResponse response) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !response._completed;
        public override long Length { get { response.CheckWrite(); return response._file.Length; } }
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count)
        {
            response.CheckWrite();
            try { response._file.Write(buffer, offset, count); }
            catch (Exception error) { response.Fail(error); }
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            // The native byte[] overload owns limits/spooling; Stream's base span
            // implementation supplies its pooled bridge without another buffer engine.
            base.Write(buffer);
        }
        public override void Flush() => response.CheckWrite();
        protected override void Dispose(bool disposing)
        { if (disposing) response.Fail(new PlatformNotSupportedException("The host owns the borrowed file response stream.")); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    public override void Clear()
    {
        CheckWrite();
        if (_file != null) Fail(new PlatformNotSupportedException("Clearing a binary response is unsupported."));
        _inner.Clear();
    }
    public override void ClearContent() => Clear();
    public override bool TrySkipIisCustomErrors
    {
        get { CheckWrite(); return _skipCustomErrors; }
        set { CheckWrite(); _skipCustomErrors = value; }
    }
    public override int StatusCode { get => _inner.StatusCode; set => _inner.StatusCode = value; }
    public override string ContentType { get => _inner.ContentType; set => _inner.ContentType = value; }
    public override Encoding ContentEncoding { get => _inner.ContentEncoding; set => _inner.ContentEncoding = value; }
    public override TextWriter Output { get => _inner.Output; set => _inner.Output = value; }
    public override void Write(string text) => _inner.Write(text);
    public override void Redirect(string url, bool endResponse) => SetRedirect(url, endResponse, 302);
    public override void RedirectPermanent(string url, bool endResponse) => SetRedirect(url, endResponse, 301);
    private void SetRedirect(string url, bool endResponse, int status)
    {
        _inner.Output.Flush();
        if (endResponse || String.IsNullOrEmpty(url) || url.StartsWith("//") || url.Contains('\\') ||
            Array.Exists(url.ToCharArray(), Char.IsControl))
            throw new PlatformNotSupportedException("Use buffered local paths or absolute HTTP/HTTPS redirects without response termination.");
        if (!url.StartsWith('/'))
        {
            // Native Uri owns URI grammar. Preserve the caller's escaped ASCII
            // text: AbsoluteUri would normalize ports, casing and escaping.
            if (Array.Exists(url.ToCharArray(), c => c <= ' ' || c >= 127) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                String.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0)
                throw new PlatformNotSupportedException("Absolute redirects require an escaped ASCII HTTP/HTTPS URI with an authority and no user information.");
        }
        // NativeMvcApplication assigns Location through the actual native header
        // collection before state commit. Uri admission is not header validation.
        Location = url; StatusCode = status;
    }
}
