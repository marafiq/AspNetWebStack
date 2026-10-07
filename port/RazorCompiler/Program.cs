using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Mvc.Razor;
using System.Web.Razor;
using Microsoft.CSharp;

if (args.Length != 2) { Console.Error.WriteLine("Usage: RazorCompiler <application-directory> <generated-output-directory>"); return 2; }
string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
string views = Path.Combine(root, "Views");
var generated = new Dictionary<string, string>(StringComparer.Ordinal);
var virtualPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
using var provider = new CSharpCodeProvider();
bool failed = false;
IEnumerable<string> sources = Directory.Exists(views) ? Directory.EnumerateFiles(views, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal) : Array.Empty<string>();
foreach (string source in sources)
{
    string extension = Path.GetExtension(source);
    if (extension.Equals(".vbhtml", StringComparison.OrdinalIgnoreCase))
    { Console.Error.WriteLine(source + "(1,1): error RAZOR: This build target supports C# .cshtml views only."); failed = true; continue; }
    if (!extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase)) continue;
    string relative = Path.GetRelativePath(root, source).Replace(Path.DirectorySeparatorChar, '/');
    string virtualPath = "~/" + relative;
    if (!virtualPaths.Add(virtualPath))
    { Console.Error.WriteLine(source + "(1,1): error RAZOR: Compiled virtual paths must be unique ignoring case."); failed = true; continue; }
    if (Path.GetFileNameWithoutExtension(source).Equals("_AppStart", StringComparison.OrdinalIgnoreCase))
    { Console.Error.WriteLine(source + "(1,1): error RAZOR: Global application-start pages are outside the MVC build contract."); failed = true; continue; }
    string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(virtualPath))).ToLowerInvariant();
    var host = new MvcWebPageRazorHost(virtualPath, source);
    host.DefaultClassName += "_" + identity.Substring(0, 12);
    // These Framework namespaces have no native implementation. References to
    // their APIs fail compilation; no replacement security/UI types are supplied.
    foreach (string ns in new[] { "System.Web.Helpers", "System.Web.Security", "System.Web.UI" }) host.NamespaceImports.Remove(ns);
    host.NamespaceImports.Add("System.Web.Mvc"); host.NamespaceImports.Add("System.Web.Mvc.Html");
    GeneratorResults result;
    using (var input = File.OpenText(source)) result = new RazorTemplateEngine(host).GenerateCode(input, host.DefaultClassName, host.DefaultNamespace, source);
    foreach (var error in result.ParserErrors)
        Console.Error.WriteLine(source + "(" + (error.Location.LineIndex + 1) + "," + (error.Location.CharacterIndex + 1) + "): error RAZOR: " + error.Message);
    if (!result.Success) { failed = true; continue; }
    result.GeneratedCode.AssemblyCustomAttributes.Add(new CodeAttributeDeclaration("AspNetWebStack.Native.CompiledRazorViewAttribute",
        new CodeAttributeArgument(new CodePrimitiveExpression(virtualPath)),
        new CodeAttributeArgument(new CodeTypeOfExpression(host.DefaultNamespace + "." + host.DefaultClassName))));
    using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
    provider.GenerateCodeFromCompileUnit(result.GeneratedCode, writer, new CodeGeneratorOptions());
    generated.Add("RazorGenerated_" + identity + ".g.cs", writer.ToString());
}
if (failed) return 1; // Do not publish a partial successful generation.
Directory.CreateDirectory(output);
foreach (var entry in generated)
{
    string destination = Path.Combine(output, entry.Key);
    if (!File.Exists(destination) || File.ReadAllText(destination) != entry.Value)
        File.WriteAllText(destination, entry.Value, new UTF8Encoding(false));
}
foreach (string stale in Directory.EnumerateFiles(output, "RazorGenerated_*.g.cs"))
    if (!generated.ContainsKey(Path.GetFileName(stale))) File.Delete(stale);
Console.WriteLine("Original Razor 3 generated " + generated.Count + " MVC views.");
return 0;
