using Microsoft.AspNetCore.Http;

namespace AspNetWebStack.Native;

// Native endpoint admission; original MVC attributes still select the action.
internal static class NativeRequestMethods
{
    internal static readonly string[] Application = { "GET", "HEAD", "OPTIONS", "POST", "PUT", "PATCH", "DELETE" };
    internal static bool IsRead(string method) => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
    internal static bool IsMutation(string method) => HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
