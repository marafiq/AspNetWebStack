# Source and dependency provenance

The public branch preserves official [aspnet/AspNetWebStack](https://github.com/aspnet/AspNetWebStack) history at `c98468c806db7d72e7a8f1e0179d2d64deff7647`. The reviewed MVC source baseline is released **v3.3.0 / MVC 5.3.0**, `1231b77d79956152831b75ad7f094f844251b97f`. Only the accepted port changes and necessary runtime/build/demo inputs are added; unrelated later upstream project changes are retained. The public snapshot corresponds to independently accepted local checkpoint U62 (`be609d3297ff46bd7a802a8bd379554f9dc267dc`); private evidence and its commit history are excluded.

AspNetWebStack retains its Apache-2.0 license. The modified unsigned SystemWebAdapters sources retain their MIT license and pinned source manifest at `port/SystemWebAdapters`, based on [30ccbfbd54f59381f98066c2197f313cd2161fe1](https://github.com/dotnet/systemweb-adapters/tree/30ccbfbd54f59381f98066c2197f313cd2161fe1). Microsoft Reference Source extracts retain their MIT notices and provenance under `port/Compatibility/ReferenceSource`.

Runtime package dependencies are System.CodeDom 10.0.1 and System.Runtime.Caching 10.0.0, with pinned transitive ConfigurationManager/ProtectedData 10.0.0. Stockroom uses Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.0, Microsoft.IdentityModel.Protocols.OpenIdConnect 8.16.0 and its family, and Microsoft.Data.Sqlite 10.0.0 with SQLitePCLRaw 2.1.11. Original notices are in `licenses/`; NuGet restores retain upstream package metadata. The private Razor compiler is build-only and absent from published app output.

The packages are experimental unsigned builds, not official Microsoft releases. Source APIs, assembly identity, wire/token formats and observed behavior are separate contracts. No runtime binaries, credentials, private keys or certificates are committed. Public screenshots/video use a disposable local fixture and are labeled U59; they do not claim unreviewed client-script behavior.

Original MVC unobtrusive validation/Ajax is byte-identical to released v3.3.0. Pinned jQuery3.7.1 and jQuery Validation1.22.1 are distributed locally with full MIT licenses; MVC scripts retain Apache-2.0 notices. `CLIENT-ASSETS.json` in the runtime package records exact file hashes and public source URLs. No CDN is required.

U62 restores the original AreaRegistration.RegisterAllAreas overloads over the explicit native application assembly catalog during the owning synchronous Map callback. Original type filtering, state and namespace behavior remain; lifecycle guards replace runtime BuildManager discovery.
