# Preview verification

This public snapshot contains reviewed U60 framework/demo implementation sources. Publication preparation changed documentation and provenance locators, added the quick-start build helper and labeled demo captures, and omitted private local evidence/history. The implementation files remain byte-identical to the accepted snapshot. Microsoft upstream history and unrelated upstream files are retained.

The public `build_demo.py` path was exercised in a fresh isolated output and NuGet cache using SDK10.0.101 and Core/ASP.NET Core10.0.2. An existing offline package feed supplied the same pinned dependencies used in review. It produced both local packages, then restored, built Debug and published Release for a copied package-only Stockroom consumer. Four original Razor views compiled, and the private Razor compiler was absent from publish. Package/cache/published identities were checked.

The rebuilt public demo passed59 browser assertions across `/` and `/tenant/app` with isolated Playwright1.63.0/Chromium153. The exercised flows include login/logout, Editor/Reader authorization, mandatory antiforgery, invalid server redisplay, a valid awaited save, one-time TempData, JSON/fetch, stale-version rejection and local assets. All owned processes stopped normally; no browser requests left loopback.

Earlier independent U60 review also covered early configuration rejection, real loopback OIDC code/PKCE and token/security cases, SQLite compare-and-swap/rollback and separate-process reopen. The public packaging changes reuse that unchanged implementation evidence. Real identity providers, certificate/private-key file loading, persistent cryptographic key rings and deployed proxy infrastructure were not exercised.

The screenshots/video show accepted U59 and are labeled accordingly. Client-side unobtrusive validation/Ajax is not part of this U60 preview. Native source recompilation is not signed binary replacement, legacy token/wire interoperability, full source API completion or .NET Framework/IIS parity. See [the profile](Packaging/PROFILE.md).
