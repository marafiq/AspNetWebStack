# MVC 5.3 native .NET 10 preview packages

Experimental unsigned source recompilation, version `0.1.0-u61`. Build from this fork with `python3 port/build_demo.py`; the resulting runtime/private Razor packages are consumed by the copied Stockroom app. They are not published to a package registry.

Original MVC controller/model/helper/C# Razor shapes run on native ASP.NET Core hosting, routing, identity and protection. Read [PROFILE.md](PROFILE.md), [OPERATIONS.md](OPERATIONS.md), [PROVENANCE.md](PROVENANCE.md) and the included licenses before adoption. The private compiler is build-only. Use untrimmed framework-dependent directory deployment with Core and ASP.NET Core 10.0.2+; SDK10.0.101 is the verified build tool.

U61 is independently reviewed within its bounded native profile. Full source API, signed binary, historical wire/token and System.Web/IIS behavior parity are not claimed. Original MVC unobtrusive validation/Ajax and pinned local jQuery dependencies are included; see CLIENT-ASSETS.json for source hashes and license attribution.
