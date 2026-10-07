using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AspNetWebStack.Native
{
    // Original MVC entry points use the native host's antiforgery and protection
    // services. No MachineKey, token serializer or cryptographic algorithm is copied.
    internal static class NativeAntiforgery
    {
        private const string FieldName = "__RequestVerificationToken";
        internal static MvcHtmlString GetHtml(HttpContextBase context)
        {
            var core = Resolve(context, out var service, out var options);
            var tokens = NativeAntiforgeryOwnership.Run(core, service, () =>
            {
                CheckCookieMultiplicity(core, options, false);
                return service.GetAndStoreTokens(core);
            }, NativeRequestLifetime.For(context)?.JsonAntiforgeryHeader);
            // Core 10.0.1 adds this while writing a new cookie. Original MVC's
            // helper also protects renders that reuse an existing cookie.
            if (!core.Response.Headers.ContainsKey("X-Frame-Options")) core.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            var input = new TagBuilder("input");
            input.Attributes["type"] = "hidden";
            input.Attributes["name"] = tokens.FormFieldName;
            input.Attributes["value"] = tokens.RequestToken;
            return new MvcHtmlString(input.ToString(TagRenderMode.SelfClosing));
        }

        internal static void Validate(HttpContextBase context)
        {
            var core = Resolve(context, out var service, out var options);
            // Preparation guarantees native validation performs no async body I/O
            // inside the original synchronous authorization filter.
            var form = context.Request as INativeFormRequest;
            string headerName = NativeRequestLifetime.For(context)?.JsonAntiforgeryHeader;
            bool json = (context is NativeJsonContext || context is NativeChildContext child && child.Root is NativeJsonContext) && headerName != null;
            bool admitted = json && NativeRequestLifetime.For(context) != null
                ? NativeRequestMethods.IsMutation(core.Request.Method) : HttpMethods.IsPost(core.Request.Method);
            if (!admitted || (!json && form?.HasFormInput != true))
                throw new PlatformNotSupportedException("Antiforgery validation requires a prepared native POST form or configured JSON mutation request.");
            try
            {
                NativeAntiforgeryOwnership.Run(core, service, () =>
                {
                    CheckCookieMultiplicity(core, options, true);
                    if (json)
                    {
                        var token = core.Request.Headers[headerName];
                        if (token.Count != 1 || String.IsNullOrEmpty(token[0]))
                            throw new HttpAntiForgeryException("Exactly one configured JSON verification header is required.");
                    }
                    else
                    {
                        // Native antiforgery prefers headers. Keep prepared form
                        // token semantics by refusing an override on form requests.
                        if (headerName != null && core.Request.Headers.ContainsKey(headerName))
                            throw new HttpAntiForgeryException("Form verification tokens must come from the prepared form.");
                        var values = ((IUnvalidatedRequestValues)form).Form.GetValues(FieldName);
                        var native = core.Request.Form[FieldName];
                        if (values == null || values.Length != 1 || String.IsNullOrEmpty(values[0]) ||
                            native.Count != 1 || !String.Equals(values[0], native[0], StringComparison.Ordinal))
                            throw new HttpAntiForgeryException("Exactly one unchanged form verification token is required.");
                    }
                    core.RequestAborted.ThrowIfCancellationRequested();
                    // ValidateRequestAsync always validates; never use the safe-method shortcut.
                    var validation = service.ValidateRequestAsync(core);
                    if (!validation.IsCompleted)
                        throw new PlatformNotSupportedException("Prepared antiforgery validation must complete synchronously.");
                    validation.GetAwaiter().GetResult();
                    return true;
                }, headerName);
            }
            catch (AntiforgeryValidationException error)
            {
                throw new HttpAntiForgeryException("Antiforgery validation failed.", error);
            }
            core.RequestAborted.ThrowIfCancellationRequested();
        }

        private static Microsoft.AspNetCore.Http.HttpContext Resolve(HttpContextBase context,
            out IAntiforgery service, out AntiforgeryOptions options)
        {
            if (context is not INativeRoutingContext routing)
                throw new PlatformNotSupportedException("Antiforgery requires the native request boundary.");
            var core = routing.CoreContext; // Enforces owner, cancellation and completion.
            service = core.RequestServices?.GetService<IAntiforgery>()
                ?? throw new PlatformNotSupportedException("The host must register native anti-forgery and Data Protection services.");
            options = core.RequestServices.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
            if (options.FormFieldName != FieldName || options.HeaderName != NativeRequestLifetime.For(context)?.JsonAntiforgeryHeader || options.SuppressReadingTokenFromFormBody ||
                options.SuppressXFrameOptionsHeader || String.IsNullOrEmpty(options.Cookie.Name) || !options.Cookie.HttpOnly ||
                options.Cookie.SecurePolicy != CookieSecurePolicy.Always || options.Cookie.SameSite != Microsoft.AspNetCore.Http.SameSiteMode.Strict ||
                options.Cookie.Domain != null)
                throw new InvalidOperationException("This boundary requires the original form field, the configured application token transport, a secure HttpOnly SameSite=Strict host cookie and frame headers.");
            if (!core.Request.IsHttps)
                throw new InvalidOperationException("This native antiforgery boundary requires HTTPS.");
            if (core.Response.HasStarted)
                throw new InvalidOperationException("Antiforgery must run before the native response starts.");
            return core;
        }

        private static void CheckCookieMultiplicity(Microsoft.AspNetCore.Http.HttpContext core, AntiforgeryOptions options, bool required)
        {
            int count;
            try { count = core.Request.GetTypedHeaders().Cookie.Count(c => String.Equals(c.Name.Value, options.Cookie.Name, StringComparison.Ordinal)); }
            catch (FormatException error) { throw new HttpAntiForgeryException("Malformed antiforgery cookie header.", error); }
            if (count > 1 || (required && count != 1))
                throw new HttpAntiForgeryException("Exactly one verification cookie is required.");
            // Parse through the native parser independently of a possibly
            // replaced request-cookie feature. Do not trust a parsed token
            // which no longer corresponds to the received cookie headers.
            var headerContext = new DefaultHttpContext();
            headerContext.Request.Headers.Cookie = core.Request.Headers.Cookie;
            bool received = headerContext.Request.Cookies.TryGetValue(options.Cookie.Name, out string wireToken);
            bool current = core.Request.Cookies.TryGetValue(options.Cookie.Name, out string parsedToken);
            if (received != current || !String.Equals(wireToken, parsedToken, StringComparison.Ordinal))
                throw new HttpAntiForgeryException("The native cookie token no longer matches the received headers.");
        }
    }
}
