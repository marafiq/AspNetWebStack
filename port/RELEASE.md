# Native evaluation release

This release assembles the reviewed MVC 5.3 port, original C# Razor build tooling, native host integrations and package-only Stockroom application. Version `0.1.0-u62` is an unsigned evaluation build. It is not an official Microsoft package, a signed Framework replacement or production certification.

Start with the [runnable demo](Stockroom/README.md). The build helper creates both packages locally and publishes a copied consumer. The supported form is an untrimmed, framework-dependent directory using SDK 10.0.101 for builds and Core plus ASP.NET Core 10.0.2 or later in the 10.0 servicing line for execution. No registry release, SDK/runtime bundle or persistent cryptographic material is included.

| Release criterion | Evidence and boundary |
|---|---|
| Original MVC application contracts | Released-reference application compilation and preserved admitted MVC/Razor/adapter API snapshots. [Source compatibility](SOURCE-COMPATIBILITY.md) identifies all 16 absent released types and separates binary/wire/behavior claims. |
| Native framework composition | Accepted combined consumers exercise scoped controller activation, routing/URLs, awaited actions and filters, request binding, Razor, synchronous children, eligible caches, bounded files/uploads, session and TempData. Each retains the limits in [PROFILE.md](Packaging/PROFILE.md). |
| Actual application consumption | Fresh local package restore, copied package-only Debug build and Release publish. The build-only Razor compiler is excluded from deployment. |
| Browser client behavior | Original unobtrusive validation/Ajax, pinned local dependencies, direct server validation, protected mutations, role checks, non-JavaScript fallback and repaired expired-login behavior at root and nested mounts. |
| Area startup compatibility | Original `AreaRegistration.RegisterAllAreas` overloads discover only explicitly supplied application/feature assemblies inside the owning synchronous `Map` callback. Independent lifecycle and root/nested HTTP tests cover state, namespaces, generated links and security. |
| Host configuration and state | Explicit local evaluation versus deployment settings; native OIDC/cookies, protected persistent-key configuration, HTTPS/proxy configuration and bounded SQLite compare-and-swap/reopen. [Operations](Stockroom/OPERATIONS.md) identifies operator responsibilities and failure limits. |
| Provenance and delivery | Official upstream lineage, pinned released comparison, exact accepted implementation inputs, local asset hashes, full third-party licenses and a separately reviewed clean public history. See [provenance](Packaging/PROVENANCE.md). |

The current [verification record](VERIFICATION.md) distinguishes freshly executed public-artifact checks from unchanged accepted evidence reused from earlier reviews. Native security boundaries remain mandatory; an unsupported mode is not treated as a successful no-op. Area registration can retain partial routes when user constructors or callbacks fail: clean up explicitly before retrying. No registration rollback, universal retry safety or arbitrary multi-application hosting is claimed.

Real identity providers, certificate/private-key loading, encrypted persistent key-ring writes and rotation, cross-restart authentication cookies, and deployed proxy infrastructure were not exercised. Those concrete integrations require operator provisioning and environment-specific validation. They receive no tested-behavior credit here.

The agreed native MVC port is complete within this explicit public-contract profile and its approved native differences. All fourteen capability groups remain visible. Independent integration, publication and delivery review passed; a focused follow-up confirms the existing native byte[] model path. Historical hosting, WebForms, signed binary identity and old token/wire parity are not automatic completion gates. Real provider/key/proxy provisioning is operator work, not missing MVC framework code. No completion percentage is inferred from omitted types.

The [hard engineering rule](../AGENTS.md) protects the public MVC contract while allowing native .NET 10 to replace internals. Compatibility code requires a demonstrated application need; a departure requires an explicit user decision before implementation. Reviewers must reject unjustified legacy reimplementation or scope expansion.
