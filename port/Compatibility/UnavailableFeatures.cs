// Explicit failure boundaries for U02. These are not implementations of these features.
using System;
using System.Configuration;
using System.Web.Routing;

namespace System.Web.Profile
{
    // Required by Controller.Profile's existing return type. Profile remains unavailable.
    public class ProfileBase : SettingsBase
    {
        public ProfileBase() => throw new PlatformNotSupportedException("U02 does not implement ASP.NET Profile.");
        private static Exception Unavailable() => new PlatformNotSupportedException("U02 does not implement ASP.NET Profile.");
        public static ProfileBase Create(string username) => throw Unavailable();
        public static ProfileBase Create(string username, bool isAuthenticated) => throw Unavailable();
        public ProfileGroupBase GetProfileGroup(string groupName) => throw Unavailable();
        public object GetPropertyValue(string propertyName) => throw Unavailable();
        public void Initialize(string username, bool isAuthenticated) => throw Unavailable();
        public override void Save() => throw Unavailable();
        public void SetPropertyValue(string propertyName, object propertyValue) => throw Unavailable();
        public new static SettingsPropertyCollection Properties => throw Unavailable();
        public bool IsAnonymous => throw Unavailable();
        public bool IsDirty => throw Unavailable();
        public override object this[string propertyName] { get => throw Unavailable(); set => throw Unavailable(); }
        public DateTime LastActivityDate => throw Unavailable();
        public DateTime LastUpdatedDate => throw Unavailable();
        public string UserName => throw Unavailable();
    }
    public class ProfileGroupBase
    {
        public ProfileGroupBase() => throw Unavailable();
        private static Exception Unavailable() => new PlatformNotSupportedException("U02 does not implement ASP.NET Profile groups.");
        public object GetPropertyValue(string propertyName) => throw Unavailable();
        public void Init(ProfileBase parent, string myName) => throw Unavailable();
        public void SetPropertyValue(string propertyName, object propertyValue) => throw Unavailable();
        public object this[string propertyName] { get => throw Unavailable(); set => throw Unavailable(); }
    }
}
namespace System.Web.WebPages
{
    internal static class UrlUtil
    {
        public static string GenerateClientUrl(HttpContextBase context, string path)
        {
            NativeRouteBinding.RequireContext(context);
            // Original two-argument UrlUtil query split/restore, using the existing
            // adapter path service. Native outward route paths already include
            // PathBase. Historical IIS rewrite detection is not a native service.
            if (!String.IsNullOrEmpty(path) && path[0] == '~')
            {
                int queryIndex = path.IndexOf('?');
                string contentPath = queryIndex < 0 ? path : path.Substring(0, queryIndex);
                string query = queryIndex < 0 ? String.Empty : path.Substring(queryIndex);
                return VirtualPathUtility.ToAbsolute(contentPath, context.Request.ApplicationPath) + query;
            }
            if (String.IsNullOrEmpty(path) || !path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\'))
                throw new PlatformNotSupportedException("Only native-generated local paths are supported; virtual-path rewriting is unavailable.");
            return path;
        }
    }
    internal static class RequestExtensions
    {
        // Original WebPages predicate; IsEmpty is String.IsNullOrEmpty. The
        // original request argument is intentionally unused. This class remains
        // an internal MVC dependency, not a new public WebPages assembly surface.
        public static bool IsUrlLocalToHost(HttpRequestBase request, string url) =>
            !String.IsNullOrEmpty(url) &&
            ((url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))) ||
             (url.Length > 1 && url[0] == '~' && url[1] == '/'));
    }
}
namespace System.Web.Mvc
{
    internal interface INet10ValidatedRequest { void ValidateInput(); }
    internal static class Net10RequestValidation
    {
        public static void Validate(HttpRequestBase request)
        {
            if (request is INet10ValidatedRequest supported) supported.ValidateInput();
            else throw new PlatformNotSupportedException("Request validation requires the restricted U02 request bridge.");
        }
    }
}
namespace System.Web
{
    public enum HttpValidationStatus { Invalid = 1, IgnoreThisRequest = 2, Valid = 3 }
    public delegate void HttpCacheValidateHandler(HttpContext context, object data, ref HttpValidationStatus validationStatus);
    internal static class CacheValidationExtensions
    {
        public static void AddValidationCallback(this HttpCachePolicyBase cache, HttpCacheValidateHandler handler, object data) => throw new PlatformNotSupportedException("U02 does not implement authorization cache validation callbacks.");
        public static object GetGlobalResourceObject(this HttpContextBase context, string classKey, string resourceKey, System.Globalization.CultureInfo culture) => throw new PlatformNotSupportedException("U02 does not implement ASP.NET global resources.");
    }
}
