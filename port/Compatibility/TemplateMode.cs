namespace AspNetWebStack.Native;

// MVC template dispatch needs only these internal modes. There is no public
// System.Web.UI enum or WebForms control/lifecycle implementation on .NET 10.
internal enum TemplateMode { ReadOnly, Edit }

internal static class NativeTemplateTypes
{
    // Optional legacy EF type identity; do not suppress modern EF or user enums
    // that happen to share its short name. No substitute enum is introduced.
    internal static readonly System.Type LegacyEntityState = System.Type.GetType(
        "System.Data.EntityState, System.Data.Entity, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", false);
}
