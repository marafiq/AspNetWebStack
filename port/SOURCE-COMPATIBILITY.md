# Public MVC contract and approved native differences

The public MVC application contract is the protected boundary: controllers, models, binding, filters, helpers and generated C# Razor views. Native .NET 10 services may replace the internals freely. The agreed port includes the explicit platform and model differences below; recreating historical internals or preserving source identity is not a completion requirement.

Comparison with released v3.3.0 / MVC 5.3.0 finds 305 of 321 released MVC type names present and the 16 below absent. No exact declared member row is missing on a present type in that snapshot comparison. There are also 51 differing type metadata rows, including inherited Framework COM interfaces and native enum interfaces. These are declaration observations, not a compatibility percentage or proof of every source program and runtime behavior.

| Absent released types | Dependency and supported alternative |
|---|---|
| `MvcHandler`, `MvcHttpHandler` | Classic `IHttpHandler`/APM and ambient System.Web hosting. Native endpoint dispatch prepares requests and runs the original controller/filter pipeline with scoped activation and awaited release. Subclassable handlers and their old entry points are not provided. |
| `PreApplicationStartCode` | Automatic WebPages modules, parsers and build-provider registration. Explicit native startup installs request scope storage and the original view-context scope connection; build targets register compiled Razor views. A legacy `Start()` call is not required or exposed. |
| `MvcWebRazorHostFactory` | Runtime virtual-path/web.config/BuildManager factory selection. The private build-time compiler directly uses original `MvcWebPageRazorHost` for admitted C# views. Runtime factory overrides, `_AppStart`, standalone WebPages and VB compilation are unsupported. |
| `ViewMasterPage`, `ViewMasterPage<TModel>`, `ViewPage`, `ViewPage<TModel>`, `ViewTemplateUserControl`, `ViewTemplateUserControl<TModel>`, `ViewType`, `ViewUserControl`, `ViewUserControl<TModel>`, `WebFormView`, `WebFormViewEngine` | Eleven WebForms page/control/parser/view-engine shapes. Use Razor views, layouts, sections and partials for the supported profile. ASPX/ASCX/master controls and their lifecycle require application changes; Razor is not source compatible with them. |
| `LinqBinaryModelBinder` | Constructs the exact `System.Data.Linq.Binary` value used by LINQ-to-SQL. Use native `byte[]` models and the existing original `ByteArrayModelBinder`. The standalone Microsoft `System.Data.Linq.Binary` class already ships for earlier helper compatibility, but its historical binder and the LINQ-to-SQL provider are not supplied. Migrating `Binary` models to `byte[]` is an explicit approved native difference; no replacement object is returned under the old binder name. |

Four entries are legacy integration shapes whose responsibilities have native implementations; eleven belong to the omitted WebForms platform, and the remaining binder is replaced at the application model boundary by native `byte[]`. These approved differences do not block completion of the agreed native MVC port. Applications using the omitted entry points must migrate those dependencies explicitly. Microsoft documents that [WebForms is available only on .NET Framework](https://learn.microsoft.com/en-us/dotnet/standard/choosing-core-framework-server#when-to-choose-net-framework) and identifies other [Framework technologies unavailable on modern .NET](https://learn.microsoft.com/en-us/dotnet/core/porting/net-framework-tech-unavailable).

## Four separate contracts

| Contract | What this port establishes |
|---|---|
| Source API | Released-reference compilation of the exercised application/controller/generated-view sources and preservation of admitted declarations. The 16 absent types and metadata differences prevent a claim of full released source compatibility. |
| Binary/type identity | Unsigned native source recompilation with adapted dependency placement. Existing signed Framework binaries are not promised to load unchanged. |
| Wire and serialization | Bounded original outbound JSON behavior and native inbound JSON; native cookies, authentication, antiforgery and Data Protection. Historical MachineKey/token/cookie interoperability is not promised. |
| Observed behavior | The workflows and bounds in [PROFILE.md](Packaging/PROFILE.md) and [verification](VERIFICATION.md). Native macOS execution is not evidence of .NET Framework/IIS parity. |

The agreed native MVC port is complete within the documented public contract and approved differences, based on independent integration and delivery review. Historical Framework/IIS behavior, signed binary identity and old token interoperability are outside that target. Their absence is a compatibility consideration, not an unfinished completion gate. Production provisioning remains environment-specific work.

## Binary model migration

Use `public byte[] Token { get; set; }` in the model and `@Html.HiddenFor(model => model.Token)` in the view. The helper emits Base64 and the existing default byte-array binder decodes form input back to `byte[]`, including complex models and action parameters. Missing or empty input returns null; malformed Base64 retains the binder's `FormatException` behavior. Use ordinary MVC validation for application requirements. Convert an existing Binary value with `ToArray()` at the application migration boundary; no LINQ-to-SQL provider is needed.
