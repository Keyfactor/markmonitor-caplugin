## Developer Guide

This document covers local development, testing, and live API smoke-testing for the MarkMonitor
AnyCA Gateway REST plugin. For production deployment and CA connector configuration, see
[configuration.md](configuration.md); for design internals, see [architecture.md](architecture.md).

## Prerequisites

- .NET SDK 8.0 **and** 10.0 (the plugin dual-targets `net8.0` and `net10.0`).
- A MarkMonitor API key and service account for live smoke-testing (see
  [Live smoke-testing](#live-smoke-testing-testconsole)). Not required to build or run the unit
  tests.

## Solution Layout

The solution (`markmonitor-caplugin.sln`) contains three projects:

| Project | Purpose |
|---|---|
| `markmonitor-caplugin/` | The plugin itself. Produces `MarkMonitorCAPlugin.dll` per TFM under `bin/Release/<tfm>/`. `manifest.json` is copied alongside the DLL on every build — it is how the AnyCA Gateway host discovers the plugin type (`Keyfactor.Extensions.CAPlugin.MarkMonitor.MarkMonitorCAPlugin`). |
| `markmonitor-caplugin.Tests/` | xUnit unit-test project (mocked HTTP, no live API). Targets `net8.0`. |
| `TestConsole/` | Manual live integration/smoke-test console app that drives `MarkMonitorClient` directly against a live or sandbox MarkMonitor API. Destructive by nature (creates real orders). |

Key source files inside `markmonitor-caplugin/`:

- **`MarkMonitorCAConnector.cs`** — `IAnyCAPlugin` implementation; the Gateway host entry point.
- **`Client/MarkMonitorClient.cs`** — the MarkMonitor REST HTTP client (auth, pagination, CSR
  handling, status mapping, dedup cache).
- **`MarkMonitorCAPluginConfig.cs`** — CA-connection and enrollment-parameter schema, UI
  annotations/defaults, and the canonical field-name constants (`ConfigConstants` /
  `EnrollmentConfigConstants`).
- **`Models/`** — request/response DTOs plus `Enums.cs` (`CertOrderTypes`, `OrderStatus`,
  `DomainControlValidationMethods`, etc.). Enum-to-API-string mapping goes through the
  `[Description]` attribute + `EnumExtensions.GetDescription()`.

## Build

```shell
dotnet build markmonitor-caplugin.sln -c Release
```

This produces `MarkMonitorCAPlugin.dll` for each TFM under
`markmonitor-caplugin/bin/Release/net8.0/` and `.../net10.0/`, each with `manifest.json` copied
alongside.

To deploy manually, copy the contents of the target framework's output directory into the Gateway's
`Extensions` folder and restart the AnyCA Gateway REST service (see the README Installation section
for the exact path).

## Unit Tests

The `markmonitor-caplugin.Tests/` project is an xUnit suite that exercises the connector and client
against a fake `HttpMessageHandler` (see `TestHelpers/FakeHttpMessageHandler.cs`) and injected fakes
(`FakeCertificateDataReader`, `FakeAnyCAPluginConfigProvider`, `ManualTimeProvider`) — no live API or
credentials are required, so it is safe to run in CI.

```shell
dotnet test markmonitor-caplugin.sln -c Release
```

Coverage is collected via `coverlet.collector`. The suite covers, among other areas:

- Connector operations — enroll, revoke, renew-or-reissue, connection-info validation, client
  caching.
- Client behavior — inventory/sync, pagination and query encoding, order-ID GUID validation, subject
  (`CN`) cleaning, ECC named-curve validation, cancel, error propagation, and enroll logging/redaction.

When you add or change behavior, add or update tests here — a fake handler that returns canned
MarkMonitor JSON is the established pattern for new client tests.

## Live Smoke-Testing (`TestConsole`)

`TestConsole/Program.cs` runs a scripted sequence against a live (or sandbox) MarkMonitor API:
authenticate, list organizations, list certificate orders, then generate RSA/ECC CSRs and submit real
enrollment orders. It is a **manual smoke-test harness, not a repeatable CI test suite** — it creates
real orders and is destructive/live by nature.

It requires real credentials as environment variables:

```
MARKMONITOR_BASE_URL      # e.g. https://api.markmonitor.com (or your sandbox URL)
MARKMONITOR_API_TOKEN     # the X-API-KEY value
MARKMONITOR_USERNAME      # service-account username
MARKMONITOR_PASSWORD      # service-account password
```

`TestConsole/.env` holds these locally and is gitignored. By default the console cancels/revokes the
orders it creates so test runs don't accumulate charges; set `MARKMONITOR_SKIP_CLEANUP=true` to leave
them in place.

Run it with:

```shell
dotnet run --project TestConsole
```

> **Test-environment note:** in the MarkMonitor test setup, an enrolled order's common name must be
> `<something>.mmcertdomain.com` or the request is rejected, and issuance requires manual email
> approval — enrollments will sit pending until approved, then appear in Command on the next
> incremental sync. See [enrollment.md](enrollment.md) for the internal end-to-end testing walkthrough.

## Adding a New MarkMonitor Product

1. Add a member to `CertOrderTypes` in `Models/Enums.cs` with the matching `[Description("...")]`
   (the MarkMonitor cert-type string).
2. Add the same product-ID name to `product_ids` in `integration-manifest.json`.
3. No separate list needs editing — `GetProductIds()` derives from the enum, and enrollment maps the
   Command product ID to the MarkMonitor string via `GetDescription()`.

## Keeping Metadata and Docs in Sync

- `integration-manifest.json` (repo root) drives Keyfactor's integration catalog metadata
  (`product_ids`, `ca_plugin_config`, `enrollment_config`) — keep it in sync with
  `MarkMonitorCAPluginConfig.cs` and `CertOrderTypes` whenever either changes.
- `docsource/*.md` are the source-of-truth documentation fragments; the root `README.md` is the
  composed/published doc built by the Keyfactor bootstrap workflow from `readme_source.md` +
  `docsource/`. Prefer editing `docsource/` for content changes.
- `docsource/CODEMAP.md` is the orientation map for this repo — update it whenever you change the
  architecture, add/move key files, or change the build/test layout.
