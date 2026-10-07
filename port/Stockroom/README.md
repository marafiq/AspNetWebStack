# Run Stockroom

Stockroom is a package-only MVC 5.3 demo on native .NET 10. This public preview contains the reviewed **U61** source: public stock, native cookie login, Editor/Reader roles, original MVC client/server validation, protected Ajax form/link updates, JSON/fetch and redirect/TempData. [The screenshots](../demo/README.md) show the earlier accepted U59 local flow; U60 added deployment configuration and durable SQLite state; U61 adds the original unobtrusive client scripts.

From the repository root, select installed SDK/runtime hosts and build:

```sh
export DOTNET10_SDK=/path/to/sdk/dotnet
export DOTNET10_RUNTIME=/path/to/runtime/dotnet
python3 port/build_demo.py
```

The SDK must be 10.0.101. The runtime host must provide both Core and ASP.NET Core in the 10.0 servicing line, at least 10.0.2. Python 3 and NuGet.org access are required. The build helper creates fresh local packages, then publishes a copied consumer with no project/source references back to the framework. It does not install an SDK/runtime, fetch a browser, or touch certificate stores.

Supply a disposable local account. In **zsh**, from the repository root:

```sh
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_CLI_TELEMETRY_OPTOUT=1
export Accounts__0__Name=editor Accounts__0__Role=Editor
read -s 'Accounts__0__Password?Choose a disposable demo password (12+ characters): '
export Accounts__0__Password
cd port/artifacts/demo/published
"$DOTNET10_RUNTIME" Stockroom.dll --LocalEvaluation true --Port 7443 --PathBase /
```

Open `https://127.0.0.1:7443/Stock`, continue past the self-signed certificate interstitial for this local page, and sign in. No certificate installation or trust command is needed. TLS and Data Protection keys stay in memory. Local stock resets and existing cookies become invalid when the process restarts. Stop with Ctrl-C, then unset `Accounts__0__Password`. Another shell can supply the same environment variable through its own secret-input mechanism; never use a real account password.

Try an invalid quantity such as `-1`, then save `23`. The first is stopped by original unobtrusive validation with field/summary feedback; server validation also rejects invalid direct submissions. The second saves through the original Ajax form and refreshes the editor. **Reload stock** exercises the original Ajax link. With JavaScript disabled, a successful save redirects and shows a one-time confirmation. **Save with JSON** exercises the separate native-fetch path. A Reader account, configured with `Accounts__1__Name`, `Accounts__1__Password` and `Accounts__1__Role=Reader`, cannot edit. Use `--PathBase /tenant/app` to exercise a mounted app and open `/tenant/app/Stock`.

Deployment is explicitly selected with `DOTNET_ENVIRONMENT=Deployment` and the supplied `appsettings.Deployment.json`. Its placeholders must be configured by the operator, with secrets supplied through protected configuration. Do not use the local account mode for production. [Operations](OPERATIONS.md) covers OIDC, HTTPS/proxy settings, protected persistent keys and bounded SQLite state; actual platform provisioning remains untested here.

Framework-dependent, untrimmed directory publishing is supported. AOT, trimming, single-file, signed Framework binary replacement and historical cookie/token interoperability are not. See [PROFILE.md](PROFILE.md).
