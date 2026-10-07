using System;
namespace AspNetWebStack.Native
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class CompiledRazorViewAttribute : Attribute
    {
        public CompiledRazorViewAttribute(string virtualPath, Type viewType)
        { VirtualPath = virtualPath; ViewType = viewType; }
        public string VirtualPath { get; }
        public Type ViewType { get; }
    }
}
