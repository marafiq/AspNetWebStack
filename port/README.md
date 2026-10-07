# MVC 5.3 on native .NET 10 — preview

This fork recompiles original ASP.NET MVC controller, model, helper and C# Razor code on a native ASP.NET Core host. It is an experimental, unsigned **U62 preview**, independently reviewed within the [supported profile](Packaging/PROFILE.md). It is not an official Microsoft release or a drop-in replacement for System.Web/IIS.

[Release scope](RELEASE.md) · [Source compatibility](SOURCE-COMPATIBILITY.md) · [Run Stockroom](Stockroom/README.md) · [Screenshots](demo/README.md) · [Deployment configuration](Stockroom/OPERATIONS.md) · [Source provenance](Packaging/PROVENANCE.md)

Stockroom is a small package-only demo: login, roles, original client and server validation, Ajax form/link updates, an awaited protected edit, JSON/fetch, and redirect/TempData. It can run entirely with disposable local accounts and process-only TLS/protection, or use the explicit native OIDC/protected-key/SQLite deployment configuration. Real identity providers, persistent cryptographic stores and deployed proxy infrastructure were not exercised during review.

Prerequisites: Python 3; .NET SDK **10.0.101**; a separately selected .NET host providing both **Microsoft.NETCore.App and Microsoft.AspNetCore.App 10.0.2+**; access to NuGet.org for pinned dependencies. No SDK, runtime, credentials, certificates or binary package feed is bundled. The helper does not install an SDK or runtime or configure certificate trust.

```sh
export DOTNET10_SDK=/path/to/sdk/dotnet
export DOTNET10_RUNTIME=/path/to/runtime/dotnet
python3 port/build_demo.py
```

The helper builds the local runtime and private Razor packages, copies Stockroom outside the source tree, and restores/builds/publishes it using package references only. Outputs are under `port/artifacts/demo/`. Follow the [demo guide](Stockroom/README.md) to supply a disposable account and run the published app.

Original AreaRegistration.RegisterAllAreas startup registration now uses only the explicitly supplied application/feature assemblies inside the synchronous Map callback. See [operations](Packaging/OPERATIONS.md) for ordering and failure limits.

Original MVC unobtrusive validation/Ajax is included with pinned local jQuery dependencies. The demo also retains its separate native-fetch JSON path. Ajax authentication expiry returns401 and preserves the editor with a sign-in message. Sixteen released MVC source types are absent, and binary identity, historical tokens/wire formats, WebForms and broad System.Web hosting are not preserved. [The finite profile](Packaging/PROFILE.md) states the boundaries. A passing native workflow does not establish .NET Framework behavior parity.
