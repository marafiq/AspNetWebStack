using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace AspNetWebStack.Native
{
    // A single bounded async read precedes the original synchronous MVC pipeline.
    // The native body is borrowed, never replaced, rewound or disposed here.
    public sealed class NativeJsonInput
    {
        private static readonly ConditionalWeakTable<Microsoft.AspNetCore.Http.HttpContext, Ownership> Requests = new();
        private readonly Ownership _ownership;
        private readonly byte[] _bytes;
        private readonly object _value;
        private NativeJsonInput(Ownership ownership, byte[] bytes, object value) { _ownership = ownership; _bytes = bytes; _value = value; }

        public static Task<NativeJsonInput> ReadAsync(Microsoft.AspNetCore.Http.HttpContext core,
            Action<string, string> validateJsonValue, int maximumBodyBytes = 65536, int maximumDepth = 16, int maximumValues = 256)
            => ReadAsync(core, validateJsonValue, false, false, maximumBodyBytes, maximumDepth, maximumValues);

        internal static async Task<NativeJsonInput> ReadAsync(Microsoft.AspNetCore.Http.HttpContext core,
            Action<string, string> validateJsonValue, bool sessionEnabled, bool applicationMethods = false, int maximumBodyBytes = 65536, int maximumDepth = 16, int maximumValues = 256)
        {
            ArgumentNullException.ThrowIfNull(core); ArgumentNullException.ThrowIfNull(validateJsonValue);
            if (maximumBodyBytes < 1 || maximumBodyBytes > 65536) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
            if (maximumDepth < 1 || maximumDepth > 16) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            if (maximumValues < 1 || maximumValues > 256) throw new ArgumentOutOfRangeException(nameof(maximumValues));
            Ownership state;
            lock (Requests)
            {
                // Kestrel may reuse a HttpContext object after completion. Its
                // per-request Items collection changes; completion alone must
                // not permit another attempt from an end-of-request callback.
                if (Requests.TryGetValue(core, out var previous) && previous.Completed &&
                    !ReferenceEquals(previous.RequestItems, core.Items))
                    Requests.Remove(core);
                state = Requests.GetValue(core, request => new Ownership(request));
            }
            try
            {
                if (Interlocked.CompareExchange(ref state.Stage, 1, 0) != 0)
                    throw new InvalidOperationException("JSON preparation is attempted once per native request.");
                // Register before admission, so failed reads/preflight do not
                // poison the next request on a pooled context. Retained inputs
                // keep this permanently completed owner after table replacement.
                core.Response.OnCompleted(static value =>
                {
                    ((Ownership)value).Completed = true;
                    return Task.CompletedTask;
                }, state);
                state.Capture(core, sessionEnabled);
                var request = core.Request;
                if (!(applicationMethods ? NativeRequestMethods.IsMutation(request.Method) : HttpMethods.IsPost(request.Method)))
                    throw new PlatformNotSupportedException("JSON preparation requires an admitted mutation method.");
                if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media) ||
                    !String.Equals(media.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase) ||
                    media.Parameters.Any(parameter => !String.Equals(parameter.Name.Value, "charset", StringComparison.OrdinalIgnoreCase)) ||
                    media.Parameters.Count > 1 ||
                    (media.Charset.HasValue && !String.Equals(media.Charset.Value.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) ||
                    request.Headers.ContainsKey("Content-Encoding"))
                    throw new HttpException(415, "Only uncompressed application/json with optional UTF-8 charset is admitted.");
                if (core.Features.Get<IFormFeature>()?.Form != null)
                    throw new PlatformNotSupportedException("Prepare JSON before other body/form consumers, with session disabled.");
                if (!state.Body.CanRead || (state.Seekable && state.Body.Position != 0))
                    throw new PlatformNotSupportedException("Prepare JSON before reading the native request body.");
                if (state.ContentLength < 0) throw new HttpException(400, "Invalid JSON content length.");
                if (state.ContentLength > maximumBodyBytes) throw new HttpException(413, "JSON body limit exceeded.");
                using var bytes = new MemoryStream();
                byte[] buffer = new byte[Math.Min(maximumBodyBytes + 1, 8192)];
                while (true)
                {
                    state.Check();
                    int remaining = maximumBodyBytes - (int)bytes.Length;
                    int count = await state.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), state.Abort).ConfigureAwait(false);
                    state.Position += count; state.Check();
                    if (count == 0) break;
                    if (count > remaining) throw new HttpException(413, "JSON body limit exceeded.");
                    bytes.Write(buffer, 0, count);
                }
                if (state.ContentLength.HasValue && state.ContentLength.Value != bytes.Length)
                    throw new HttpException(400, "JSON content length does not match the received body.");
                byte[] data = bytes.ToArray(); string text;
                try { text = new UTF8Encoding(false, true).GetString(data); }
                catch (DecoderFallbackException) { throw new HttpException(400, "Malformed JSON UTF-8."); }
                // Parse and validate once. The original MVC factory receives
                // this private graph through the owned stream, then flattens it.
                object value = NativeJsonParser.Parse(text, maximumDepth, maximumValues, validateJsonValue, state.Check);
                state.Check(); state.Stage = 2;
                return new NativeJsonInput(state, data, value);
            }
            catch { state.Failed = true; throw; }
        }

        internal Stream Claim(Microsoft.AspNetCore.Http.HttpContext core, Action checkContext, NativeRequestLifetime lifetime)
        {
            try
            {
                _ownership.Check();
                if (!ReferenceEquals(core, _ownership.Core) || Interlocked.CompareExchange(ref _ownership.Stage, 3, 2) != 2)
                    throw new InvalidOperationException("Prepared JSON belongs to one unchanged native request and one MVC wrapper.");
                _ownership.Lifetime = lifetime;
                _ownership.Thread = lifetime == null ? Environment.CurrentManagedThreadId : 0;
                lifetime?.CheckIdentity();
                return new PreparedStream(_bytes, _value, () => { checkContext(); _ownership.Check(); });
            }
            catch { _ownership.Failed = true; throw; }
        }
        internal void Check() => _ownership.Check();

        private sealed class Ownership
        {
            internal int Stage, Thread;
            internal NativeRequestLifetime Lifetime;
            internal volatile bool Failed;
            internal volatile bool Completed;
            internal readonly object RequestItems;
            private readonly IHttpResponseFeature _responseFeature;
            internal Microsoft.AspNetCore.Http.HttpContext Core;
            internal Stream Body;
            internal long? ContentLength;
            internal long Position;
            internal bool Seekable;
            internal CancellationToken Abort;
            private IHttpRequestFeature _feature;
            private NativePrincipalSnapshot _principal;
            private NativeSessionOwnership _sessionOwner;
            private string _method, _scheme, _path, _pathBase, _query, _contentType;
            private string[] _encoding, _transfer, _verbOverride;
            internal Ownership(Microsoft.AspNetCore.Http.HttpContext core)
            {
                RequestItems = core.Items;
                _responseFeature = core.Features.Get<IHttpResponseFeature>();
            }
            internal void Capture(Microsoft.AspNetCore.Http.HttpContext core, bool sessionEnabled)
            {
                _sessionOwner = new NativeSessionOwnership(core, sessionEnabled);
                Core = core; Body = core.Request.Body; Seekable = Body.CanSeek; Position = Seekable ? Body.Position : 0;
                ContentLength = core.Request.ContentLength; Abort = core.RequestAborted;
                _feature = core.Features.Get<IHttpRequestFeature>(); _principal = new NativePrincipalSnapshot(core);
                _method = core.Request.Method; _scheme = core.Request.Scheme; _path = core.Request.Path.Value;
                _pathBase = core.Request.PathBase.Value; _query = core.Request.QueryString.Value; _contentType = core.Request.ContentType;
                _encoding = core.Request.Headers.ContentEncoding.ToArray(); _transfer = core.Request.Headers.TransferEncoding.ToArray();
                _verbOverride = core.Request.Headers["X-HTTP-Method-Override"].ToArray();
                Check();
            }
            internal void Check()
            {
                try
                {
                    if (Failed || Completed || Core == null || (Thread != 0 && Thread != Environment.CurrentManagedThreadId))
                        throw new InvalidOperationException("JSON request ownership was violated.");
                    Lifetime?.CheckIdentity();
                    Abort.ThrowIfCancellationRequested(); _principal.Check(Core); _sessionOwner.Check();
                    var request = Core.Request;
                    if (Abort != Core.RequestAborted || Core.Response.HasStarted || !ReferenceEquals(RequestItems, Core.Items) ||
                        !ReferenceEquals(_responseFeature, Core.Features.Get<IHttpResponseFeature>()) || !ReferenceEquals(_feature, Core.Features.Get<IHttpRequestFeature>()) ||
                        !ReferenceEquals(Body, request.Body) || ContentLength != request.ContentLength ||
                        _method != request.Method || _scheme != request.Scheme || _path != request.Path.Value || _pathBase != request.PathBase.Value ||
                        _query != request.QueryString.Value || _contentType != request.ContentType ||
                        !_encoding.SequenceEqual(request.Headers.ContentEncoding.ToArray()) || !_transfer.SequenceEqual(request.Headers.TransferEncoding.ToArray()) ||
                        !_verbOverride.SequenceEqual(request.Headers["X-HTTP-Method-Override"].ToArray()) ||
                        Body.CanSeek != Seekable || (Seekable && Body.Position != Position) ||
                        Core.Features.Get<IFormFeature>()?.Form != null)
                        throw new InvalidOperationException("The native JSON request changed during owned input processing.");
                }
                catch { Failed = true; throw; }
            }
        }

        private sealed class PreparedStream : Stream, INativeJsonStream
        {
            private readonly MemoryStream _inner;
            private readonly object _value;
            private readonly Action _check;
            private bool _disposed;
            internal PreparedStream(byte[] bytes, object value, Action check) { _inner = new MemoryStream(bytes, writable: false); _value = value; _check = check; }
            object INativeJsonStream.ReadValue()
            {
                Check();
                if (_inner.Position == _inner.Length) return null;
                if (_inner.Position != 0) throw new HttpException(400, "MVC JSON binding requires the complete prepared body.");
                _inner.Position = _inner.Length;
                return _value;
            }
            private void Check() { _check(); ObjectDisposedException.ThrowIf(_disposed, this); }
            public override bool CanRead { get { Check(); return true; } }
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length { get { Check(); return _inner.Length; } }
            public override long Position { get { Check(); return _inner.Position; } set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) { Check(); int n = _inner.Read(buffer, offset, count); Check(); return n; }
            public override int Read(Span<byte> buffer) { Check(); int n = _inner.Read(buffer); Check(); return n; }
            public override int ReadByte() { Check(); int n = _inner.ReadByte(); Check(); return n; }
            public override void Flush() { Check(); }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) { _check(); _disposed = true; _inner.Dispose(); } base.Dispose(disposing); }
        }
    }
    // Internal handoff: no mutable parse graph is exposed on the public request API.
    internal interface INativeJsonStream { object ReadValue(); }
}
