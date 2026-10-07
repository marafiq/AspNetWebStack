# Source compatibility and native replacements

The native C# Razor profile retains ordinary MVC controller, model, binder, filter, helper and generated-view contracts. It does **not** preserve the complete released MVC surface. This distinction matters when deciding whether an existing application can use the port.

Comparison with released v3.3.0 / MVC 5.3.0 finds 305 of 321 released MVC type names present and the 16 below absent. No exact declared member row is missing on a present type in that snapshot comparison. There are also 51 differing type metadata rows, including inherited Framework COM interfaces and native enum interfaces. These are declaration observations, not a compatibility percentage or proof of every source program and runtime behavior.

| Absent released types | Dependency and supported alternative |
|---|---|
| `MvcHandler`, `MvcHttpHandler` | Classic `IHttpHandler`/APM and ambient System.Web hosting. Native endpoint dispatch prepares requests and runs the original controller/filter pipeline with scoped activation and awaited release. Subclassable handlers and their old entry points are not provided. |
| `PreApplicationStartCode` | Automatic WebPages modules, parsers and build-provider registration. Explicit native startup installs request scope storage and the original view-context scope connection; build targets register compiled Razor views. A legacy `Start()` call is not required or exposed. |
| `MvcWebRazorHostFactory` | Runtime virtual-path/web.config/BuildManager factory selection. The private build-time compiler directly uses original `MvcWebPageRazorHost` for admitted C# views. Runtime factory overrides, `_AppStart`, standalone WebPages and VB compilation are unsupported. |
| `ViewMasterPage`, `ViewMasterPage<TModel>`, `ViewPage`, `ViewPage<TModel>`, `ViewTemplateUserControl`, `ViewTemplateUserControl<TModel>`, `ViewType`, `ViewUserControl`, `ViewUserControl<TModel>`, `WebFormView`, `WebFormViewEngine` | Eleven WebForms page/control/parser/view-engine shapes. Use Razor views, layouts, sections and partials for the supported profile. ASPX/ASCX/master controls and their lifecycle require application changes; Razor is not source compatible with them. |
| `LinqBinaryModelBinder` | Constructs the exact `System.Data.Linq.Binary` value used by LINQ-to-SQL. Original `ByteArrayModelBinder` and default `byte[]` binding remain supported. The LINQ-to-SQL value type and its binder are not supplied or silently replaced by another return type. |

Four entries are legacy integration shapes whose useful responsibilities have native implementations; the remaining twelve belong to the omitted WebForms or LINQ-to-SQL platform shapes. Their omission does not earn implementation credit. Applications using them are outside the source-compatible profile and must adapt those dependencies or remain on their original platform. Microsoft documents that [WebForms is available only on .NET Framework](https://learn.microsoft.com/en-us/dotnet/standard/choosing-core-framework-server#when-to-choose-net-framework) and identifies other [Framework technologies unavailable on modern .NET](https://learn.microsoft.com/en-us/dotnet/core/porting/net-framework-tech-unavailable).

## Four separate contracts

| Contract | What this port establishes |
|---|---|
| Source API | Released-reference compilation of the exercised application/controller/generated-view sources and preservation of admitted declarations. The 16 absent types and metadata differences prevent a claim of full released source compatibility. |
| Binary/type identity | Unsigned native source recompilation with adapted dependency placement. Existing signed Framework binaries are not promised to load unchanged. |
| Wire and serialization | Bounded original outbound JSON behavior and native inbound JSON; native cookies, authentication, antiforgery and Data Protection. Historical MachineKey/token/cookie interoperability is not promised. |
| Observed behavior | The workflows and bounds in [PROFILE.md](Packaging/PROFILE.md) and [verification](VERIFICATION.md). Native macOS execution is not evidence of .NET Framework/IIS parity. |

The native evaluation release and the original broader compatibility objective must remain distinct. The supported native profile can be accepted on its evidence while complete original source, binary, historical wire and platform parity remain unmet.
