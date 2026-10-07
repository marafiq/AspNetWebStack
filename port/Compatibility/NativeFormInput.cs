using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace AspNetWebStack.Native
{
    // Preparation consumes the native body asynchronously before synchronous MVC
    // owns a context. This admits form values; it grants no authority to mutate.
    public sealed class NativeFormInput : IDisposable
    {
        private readonly Microsoft.AspNetCore.Http.HttpContext _core;
        private readonly Stream _body;
        private readonly string _contentType;
        private readonly NameValueCollection _values;
        private int _claimed, _disposed;
        private readonly Stream _buffer;
        private readonly IFormCollection _form;
        private readonly NativeSessionOwnership _sessionOwner;
        internal NativeFormInput(Microsoft.AspNetCore.Http.HttpContext core, NameValueCollection values, Stream buffer = null, IFormCollection form = null)
        { _sessionOwner = new NativeSessionOwnership(core, true); _core = core; _body = core.Request.Body; _contentType = core.Request.ContentType; _values = values; _buffer = buffer; _form = form ?? core.Request.Form; }

        public static Task<NativeFormInput> ReadMultipartAsync(Microsoft.AspNetCore.Http.HttpContext core, FormOptions options)
            => NativeMultipartForm.ReadAsync(core, options);

        internal void Check()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _core.RequestAborted.ThrowIfCancellationRequested(); _sessionOwner.Check();
            if (!ReferenceEquals(_core.Request.Body, _body) || _core.Request.ContentType != _contentType ||
                !HttpMethods.IsPost(_core.Request.Method))
                throw new InvalidOperationException("Prepared form input belongs to the unchanged native request.");
        }
        internal HttpFileCollectionBase Files(Action check) => new NativePostedFiles(_form.Files, check);
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _buffer?.Dispose(); }

        public static Task<NativeFormInput> ReadAsync(Microsoft.AspNetCore.Http.HttpContext core,
            int maximumBodyBytes = 65536, int maximumFormValues = 256)
            => ReadAsync(core, false, maximumBodyBytes, maximumFormValues);

        internal static async Task<NativeFormInput> ReadAsync(Microsoft.AspNetCore.Http.HttpContext core,
            bool sessionEnabled, int maximumBodyBytes = 65536, int maximumFormValues = 256)
        {
            ArgumentNullException.ThrowIfNull(core);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBodyBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFormValues);
            var request = core.Request;
            core.RequestAborted.ThrowIfCancellationRequested();
            if (!HttpMethods.IsPost(request.Method)) throw new PlatformNotSupportedException("Form preparation requires POST.");
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media) ||
                !String.Equals(media.MediaType.Value, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ||
                (media.Charset.HasValue && !String.Equals(media.Charset.Value.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) ||
                request.Headers.ContainsKey("Content-Encoding"))
                throw new HttpException(415, "Only uncompressed UTF-8 URL-encoded forms are admitted.");
            var sessionOwner = new NativeSessionOwnership(core, sessionEnabled);
            if (core.Features.Get<IFormFeature>()?.Form != null || (request.Body.CanSeek && request.Body.Position != 0))
                throw new PlatformNotSupportedException("Prepare form input before middleware reads or replaces form/body values.");
            if (request.ContentLength > maximumBodyBytes) throw new HttpException(413, "Form body limit exceeded.");
            using var bytes = new MemoryStream();
            byte[] buffer = new byte[Math.Min(maximumBodyBytes, 8192)];
            while (true)
            {
                sessionOwner.Check();
                int count = await request.Body.ReadAsync(buffer.AsMemory(), core.RequestAborted).ConfigureAwait(false);
                sessionOwner.Check();
                if (count == 0) break;
                if (count > maximumBodyBytes - bytes.Length) throw new HttpException(413, "Form body limit exceeded.");
                bytes.Write(buffer, 0, count);
            }
            string wire;
            try { wire = new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
            catch (DecoderFallbackException) { throw new HttpException(400, "Malformed form UTF-8 encoding."); }
            NativeQueryContext.ValidateUrlEncodedInput("?" + wire, "form");
            Dictionary<string, Microsoft.Extensions.Primitives.StringValues> parsed;
            try
            {
                using var reader = new FormReader(wire) { ValueCountLimit = maximumFormValues, KeyLengthLimit = maximumBodyBytes, ValueLengthLimit = maximumBodyBytes };
                parsed = await reader.ReadFormAsync(core.RequestAborted).ConfigureAwait(false);
            }
            catch (InvalidDataException) { throw new HttpException(400, "Form value limit exceeded."); }
            // Preserve first decoded key spelling, including dictionary/ModelState
            // keys; all values are supplied by the native form parser.
            var firstNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in new QueryStringEnumerable(wire))
            { string name = pair.DecodeName().ToString(); firstNames.TryAdd(name, name); }
            var snapshot = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in parsed)
                foreach (string value in pair.Value) snapshot.Add(firstNames.TryGetValue(pair.Key, out var first) ? first : pair.Key, value);
            core.RequestAborted.ThrowIfCancellationRequested();
            sessionOwner.Check();
            request.Form = new FormCollection(parsed); // Native consumers and the adapter see the parsed, file-free form.
            return new NativeFormInput(core, snapshot);
        }
        internal NameValueCollection Claim(Microsoft.AspNetCore.Http.HttpContext core)
        {
            Check();
            if (!ReferenceEquals(core, _core) || !ReferenceEquals(core.Request.Body, _body) ||
                core.Request.ContentType != _contentType || !HttpMethods.IsPost(core.Request.Method))
                throw new InvalidOperationException("Prepared form input belongs to the unchanged native request.");
            if (Interlocked.Exchange(ref _claimed, 1) != 0) throw new InvalidOperationException("Prepared form input is claimed once.");
            return new NameValueCollection(_values);
        }
    }
}
