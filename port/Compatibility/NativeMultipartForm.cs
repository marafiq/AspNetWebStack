using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace AspNetWebStack.Native;

// Native parsing and disk-backed buffering; this bridge only admits a bounded
// request and hands the parsed files to the original MVC file value provider.
internal static class NativeMultipartForm
{
    private static readonly object AttemptKey = new();
    internal static FormOptions Defaults() => new FormOptions
    {
        BufferBodyLengthLimit = 8 * 1024 * 1024,
        MultipartBodyLengthLimit = 8 * 1024 * 1024,
        MemoryBufferThreshold = 32 * 1024,
        ValueCountLimit = 256,
        KeyLengthLimit = 2048,
        ValueLengthLimit = 65536
    };

    // Snapshot host policy. Native FormOptions remains the configuration API.
    internal static FormOptions Snapshot(FormOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BufferBodyLengthLimit < 1 || options.BufferBodyLengthLimit >= Int32.MaxValue ||
            options.MultipartBodyLengthLimit < 1 || options.MultipartBodyLengthLimit > options.BufferBodyLengthLimit ||
            options.MemoryBufferThreshold < 0 || options.MemoryBufferThreshold > options.BufferBodyLengthLimit ||
            options.ValueCountLimit < 1 || options.KeyLengthLimit < 1 || options.ValueLengthLimit < 1 ||
            options.MultipartBoundaryLengthLimit < 1 || options.MultipartHeadersCountLimit < 1 || options.MultipartHeadersLengthLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Multipart limits must be positive and fit the bounded legacy file length; memory and section limits cannot exceed the total limit.");
        return new FormOptions
        {
            // Buffering is already complete when MultipartReader starts parsing.
            BufferBody = false,
            BufferBodyLengthLimit = options.BufferBodyLengthLimit,
            MultipartBodyLengthLimit = options.MultipartBodyLengthLimit,
            MemoryBufferThreshold = options.MemoryBufferThreshold,
            ValueCountLimit = options.ValueCountLimit,
            KeyLengthLimit = options.KeyLengthLimit,
            ValueLengthLimit = options.ValueLengthLimit,
            MultipartBoundaryLengthLimit = options.MultipartBoundaryLengthLimit,
            MultipartHeadersCountLimit = options.MultipartHeadersCountLimit,
            MultipartHeadersLengthLimit = options.MultipartHeadersLengthLimit
        };
    }

    internal static bool IsMultipart(string contentType) => MediaTypeHeaderValue.TryParse(contentType, out var media) &&
        String.Equals(media.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase);

    internal static async Task<NativeFormInput> ReadAsync(Microsoft.AspNetCore.Http.HttpContext core, FormOptions options, bool sessionEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(core);
        options = Snapshot(options);
        if (core.Items.ContainsKey(AttemptKey)) throw new InvalidOperationException("Multipart preparation is attempted once per request.");
        core.Items[AttemptKey] = true;
        var request = core.Request;
        var body = request.Body;
        var abort = core.RequestAborted;
        var contentType = request.ContentType;
        var contentLength = request.ContentLength;
        var principal = new NativePrincipalSnapshot(core);
        var sessionOwner = new NativeSessionOwnership(core, sessionEnabled);
        var requestFeature = core.Features.Get<IHttpRequestFeature>();
        var formFeature = core.Features.Get<IFormFeature>();
        void Check()
        {
            abort.ThrowIfCancellationRequested(); principal.Check(core); sessionOwner.Check();
            if (!ReferenceEquals(body, request.Body) || abort != core.RequestAborted ||
                !ReferenceEquals(requestFeature, core.Features.Get<IHttpRequestFeature>()) ||
                !ReferenceEquals(formFeature, core.Features.Get<IFormFeature>()) || formFeature?.Form != null ||
                contentType != request.ContentType || contentLength != request.ContentLength ||
                !HttpMethods.IsPost(request.Method) || request.Headers.ContainsKey("Content-Encoding") ||
                core.Response.HasStarted)
                throw new InvalidOperationException("The native multipart request changed during preparation.");
        }
        if (!HttpMethods.IsPost(request.Method)) throw new PlatformNotSupportedException("Multipart preparation requires POST.");
        if (!IsMultipart(contentType) || request.Headers.ContainsKey("Content-Encoding"))
            throw new HttpException(415, "Multipart preparation requires uncompressed multipart/form-data.");
        if (formFeature?.Form != null || !body.CanRead || (body.CanSeek && body.Position != 0))
            throw new PlatformNotSupportedException("Prepare multipart input before other body/form consumers, with session disabled.");
        if (contentLength < 0) throw new HttpException(400, "Invalid multipart content length.");
        if (contentLength > options.BufferBodyLengthLimit) throw new HttpException(413, "Multipart body limit exceeded.");
        Check();
        string directory = Environment.GetEnvironmentVariable("ASPNETCORE_TEMP") ?? Path.GetTempPath();
        var buffer = new FileBufferingReadStream(body, options.MemoryBufferThreshold, options.BufferBodyLengthLimit + 1, directory);
        try
        {
            // Bound the complete wire body, including multipart preamble and
            // epilogue. Read one extra byte to classify over-limit input without
            // depending on native exception-message text. Borrowed body stays open.
            byte[] block = new byte[8192];
            long received = 0;
            while (true)
            {
                Check();
                int count = await buffer.ReadAsync(block.AsMemory(0, (int)Math.Min(block.Length, options.BufferBodyLengthLimit - received + 1)), abort).ConfigureAwait(false);
                Check();
                received += count;
                if (received > options.BufferBodyLengthLimit) throw new HttpException(413, "Multipart body limit exceeded.");
                if (count == 0) break;
            }
            if (contentLength.HasValue && contentLength != received) throw new HttpException(400, "Multipart content length does not match the received body.");
            buffer.Position = 0;
            // Native MultipartReader owns wire parsing. FormFeature treats an
            // empty filename as a form field, which can shadow real file arrays
            // in MVC's original value-provider order. Preserve filename presence
            // here so the original file binder can choose its null-file value.
            IFormCollection parsed;
            try { parsed = await ParseAsync(buffer, contentType, options, abort).ConfigureAwait(false); }
            catch (InvalidDataException) { throw new HttpException(400, "Invalid multipart form or native multipart limit exceeded."); }
            catch (IOException) { throw new HttpException(400, "Incomplete multipart form."); }
            var values = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            // Native multipart parsing bounds sections/headers; its URL-encoded
            // key/value limits do not apply to multipart, so enforce them here.
            foreach (var pair in parsed)
            {
                if (pair.Key.Length > options.KeyLengthLimit || pair.Value.Any(value => value?.Length > options.ValueLengthLimit))
                    throw new HttpException(400, "Multipart field length limit exceeded.");
                foreach (string value in pair.Value) values.Add(pair.Key, value);
            }
            foreach (var file in parsed.Files)
                if (file.Name.Length > options.KeyLengthLimit || file.Length > Int32.MaxValue)
                    throw new HttpException(400, "Multipart file metadata exceeds the legacy limit.");
            Check();
            request.Form = parsed;
            var input = new NativeFormInput(core, values, buffer, parsed);
            core.Response.RegisterForDispose(input); // Fallback when preparation is not claimed.
            return input;
        }
        catch { buffer.Dispose(); throw; }
    }
    private static async Task<IFormCollection> ParseAsync(Stream buffer, string contentType, FormOptions options, System.Threading.CancellationToken abort)
    {
        var media = MediaTypeHeaderValue.Parse(contentType);
        var boundary = HeaderUtilities.RemoveQuotes(media.Boundary);
        if (!boundary.HasValue || boundary.Length == 0 || boundary.Length > options.MultipartBoundaryLengthLimit)
            throw new InvalidDataException("Missing or over-limit multipart boundary.");
        var reader = new MultipartReader(boundary.Value, buffer)
        {
            HeadersCountLimit = options.MultipartHeadersCountLimit,
            HeadersLengthLimit = options.MultipartHeadersLengthLimit,
            BodyLengthLimit = options.MultipartBodyLengthLimit
        };
        var values = new KeyValueAccumulator();
        var files = new FormFileCollection();
        int count = 0;
        MultipartSection section;
        while ((section = await reader.ReadNextSectionAsync(abort).ConfigureAwait(false)) != null)
        {
            if (++count > options.ValueCountLimit) throw new InvalidDataException("Multipart section count limit exceeded.");
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Multipart form-data disposition is required.");
            string name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
            if (name == null) throw new InvalidDataException("Multipart field name is required.");
            bool file = disposition.Parameters.Any(parameter => parameter.Name.Equals("filename", StringComparison.OrdinalIgnoreCase) || parameter.Name.Equals("filename*", StringComparison.OrdinalIgnoreCase));
            if (file)
            {
                await section.Body.DrainAsync(abort).ConfigureAwait(false);
                string filename = disposition.FileNameStar.HasValue ? disposition.FileNameStar.Value : HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
                var upload = new FormFile(buffer, section.BaseStreamOffset ?? throw new InvalidOperationException("Multipart parsing requires the complete seekable buffer."), section.Body.Length, name, filename ?? String.Empty)
                { Headers = new HeaderDictionary(section.Headers) };
                files.Add(upload);
            }
            else
            {
                var field = new FormMultipartSection(section, disposition);
                string value = await field.GetValueAsync(abort).ConfigureAwait(false);
                values.Append(name, value);
            }
        }
        return new FormCollection(values.GetResults(), files);
    }

}
