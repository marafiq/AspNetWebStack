# Pinned unsigned SystemWebAdapters source

This modified MIT-licensed source build is based on [dotnet/systemweb-adapters 30ccbfbd54f59381f98066c2197f313cd2161fe1](https://github.com/dotnet/systemweb-adapters/tree/30ccbfbd54f59381f98066c2197f313cd2161fe1), associated with upstream package2.3.0. `SOURCE-MANIFEST.json` retains original hashes; `SOURCE-DEVIATIONS.json` records changes. The original project and license are retained.

The build excludes `HtmlString.IHtmlContent.cs` to preserve the legacy `IHtmlString` contract. Accepted changes correct canonical parent-path traversal and restore the documented file-collection/custom-error declarations. `NativePathHosting.cs` connects the native host; it does not emulate AppDomain, modules or Global.asax.

The assembly is unsigned and must not be mixed with the official signed SystemWebAdapters package. Recompile consumers against this source profile. See [the supported native profile](../Packaging/PROFILE.md) for source/binary/behavior limits.
