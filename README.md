# MultiTenant MVC (.NET 10 preview)

Features:
- Subdomain-based multitenancy (alpha/beta) with per-tenant config and connection
- EF Core per-tenant DbContext with automatic migrations
- SignalR `TenantHub` broadcasting messages to tenants
- Quartz per-tenant scheduled job (15s) pushing messages
- EF Outbox with background dispatcher
- HTMX components with typed events (HX-Trigger)
- OpenTelemetry tracing/metrics (console exporter)
- Tailwind (placeholder build; optional CLI instructions below)

## Run
```
cd Web
../.dotnet/dotnet run --urls http://localhost:5069
```
Open:
- http://alpha.localtest.me:5069/
- http://beta.localtest.me:5069/

## Tailwind real build (optional)
```
curl -sSL -o tailwindcss https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64
chmod +x tailwindcss
./tailwindcss -i Styles/input.css -o wwwroot/css/tailwind.css --minify
```
