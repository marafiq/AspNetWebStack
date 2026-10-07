using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace MvcNet10Host;

// One dispatch owns the buffer and borrows its writer to the view. The default
// ownership remains thread-bound; the native application supplies an awaited guard.
// Nothing reaches the native response until execution and controller release finish.
internal sealed class BufferedResponse : HttpResponseBase
{
    internal const int CharacterLimit = 65536;
    private readonly StringBuilder _body = new();
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly CancellationToken _requestAborted;
    private readonly Action _checkOwner, _beforeTextWrite;
    private readonly ResponseWriter _writer;
    private bool _completed;
    private BufferedResponse _capture;

    internal BufferedResponse(CancellationToken requestAborted = default, Action checkOwner = null, Action beforeTextWrite = null)
    {
        _requestAborted = requestAborted; _checkOwner = checkOwner; _beforeTextWrite = beforeTextWrite;
        _writer = new ResponseWriter(this);
    }

    public override int StatusCode { get; set; } = 200;
    public override string StatusDescription { get => null; set => throw new PlatformNotSupportedException("U02 does not implement legacy reason phrases."); }
    public override string ContentType { get; set; } = "text/html";
    public override Encoding ContentEncoding { get; set; } = Encoding.UTF8;
    public override TextWriter Output
    {
        get { CheckAccess(); return _capture?.Output ?? _writer; }
        set => throw new PlatformNotSupportedException("U07 does not replace the response writer.");
    }

    private void CheckAccess()
    {
        if (_checkOwner != null) _checkOwner();
        else if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new PlatformNotSupportedException("U07 response writes require the synchronous request thread.");
        if (_completed) throw new ObjectDisposedException(nameof(BufferedResponse));
        _requestAborted.ThrowIfCancellationRequested();
    }

    private void Append(ReadOnlySpan<char> value)
    {
        CheckAccess(); _beforeTextWrite?.Invoke();
        if (_capture != null) { _capture.Append(value); return; }
        if (value.Length > CharacterLimit - _body.Length)
            throw new PlatformNotSupportedException("U02 response buffer limit exceeded.");
        _body.Append(value);
    }

    public override void Clear() { CheckAccess(); if (_capture != null) _capture.Clear(); else _body.Clear(); }
    public override void ClearContent() => Clear();

    public override void Write(string value) => Append(value.AsSpan());
    internal byte[] GetBytes() { CheckAccess(); return ContentEncoding.GetBytes(_body.ToString()); }
    internal BufferedResponse BeginCapture()
    {
        CheckAccess();
        if (_capture != null) throw new InvalidOperationException("A response capture is already active.");
        return _capture = new BufferedResponse(_requestAborted, _checkOwner);
    }
    internal string EndCapture(BufferedResponse capture)
    {
        if (!ReferenceEquals(_capture, capture)) throw new InvalidOperationException("The child capture changed.");
        _capture = null;
        capture.Complete();
        return capture._body.ToString();
    }
    internal void Complete()
    {
        // Host-owned completion must work even when cancellation is already set.
        if (_checkOwner != null) _checkOwner();
        else if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Response completion requires its owner thread.");
        _capture?.Complete(); _capture = null;
        _completed = true;
    }

    private sealed class ResponseWriter : TextWriter
    {
        private readonly BufferedResponse _response;
        internal ResponseWriter(BufferedResponse response) { _response = response; }
        public override Encoding Encoding => _response.ContentEncoding;
        public override void Write(char value) => _response.Append(new ReadOnlySpan<char>(in value));
        public override void Write(string value) => _response.Write(value);
        public override void Write(ReadOnlySpan<char> value) => _response.Append(value);
        public override void Write(char[] buffer)
        {
            _response.CheckAccess();
            if (buffer != null) Write(buffer, 0, buffer.Length);
        }
        public override void Write(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (index > buffer.Length - count) throw new ArgumentException("The character range is outside the buffer.");
            _response.Append(buffer.AsSpan(index, count));
        }
        public override void Write(object value) { _response.CheckAccess(); base.Write(value); }
        public override void Write(StringBuilder value) { _response.CheckAccess(); base.Write(value); }
        public override void Flush() => _response.CheckAccess(); // Memory-only; never publishes the response.
        protected override void Dispose(bool disposing)
        {
            if (disposing) throw new PlatformNotSupportedException("The host owns the borrowed response writer; views must not close it.");
        }

        // These operations complete synchronously against the same buffer. No task
        // is queued to another thread and no asynchronous MVC rendering is implied.
        private Task BufferedAsync(Action action, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _response.CheckAccess();
                action();
                return Task.CompletedTask;
            }
            catch (OperationCanceledException error) when (error.CancellationToken.IsCancellationRequested)
            { return Task.FromCanceled(error.CancellationToken); }
            catch (Exception error) { return Task.FromException(error); }
        }
        public override Task WriteAsync(char value) => BufferedAsync(() => Write(value));
        public override Task WriteAsync(string value) => BufferedAsync(() => Write(value));
        public override Task WriteAsync(char[] buffer, int index, int count) => BufferedAsync(() => Write(buffer, index, count));
        public override Task WriteAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken = default) => BufferedAsync(() => Write(value.Span), cancellationToken);
        public override Task WriteAsync(StringBuilder value, CancellationToken cancellationToken = default) => BufferedAsync(() => Write(value), cancellationToken);
        public override Task WriteLineAsync() => BufferedAsync(WriteLine);
        public override Task WriteLineAsync(char value) => BufferedAsync(() => WriteLine(value));
        public override Task WriteLineAsync(string value) => BufferedAsync(() => WriteLine(value));
        public override Task WriteLineAsync(char[] buffer, int index, int count) => BufferedAsync(() => WriteLine(buffer, index, count));
        public override Task WriteLineAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken = default) => BufferedAsync(() => WriteLine(value.Span), cancellationToken);
        public override Task WriteLineAsync(StringBuilder value, CancellationToken cancellationToken = default) => BufferedAsync(() => WriteLine(value), cancellationToken);
        public override Task FlushAsync() => BufferedAsync(Flush);
        public override Task FlushAsync(CancellationToken cancellationToken) => BufferedAsync(Flush, cancellationToken);
    }
}
