# CODEMAP

> **Purpose:** a fast orientation map of this repository for humans and AI agents. Read this first
> before diving into the source.
>
> **⚠️ Maintenance rule — any agent (or developer) that makes a code change MUST update this
> CODEMAP in the same change** if the change adds/removes/moves a source file of note, alters the
> architecture or a data flow, changes the build/test layout, or changes config/enrollment fields or
> product IDs. Keep it terse and accurate; a stale codemap is worse than none. This file is committed
> to the repo

Last verified against the codebase: 2026-07-16.

## What this is

An AnyCA REST Gateway plugin that lets Keyfactor Command issue, revoke, and synchronize certificates
through the MarkMonitor SSL certificate API. It implements `IAnyCAPlugin` from
`Keyfactor.AnyGateway.Extensions` and is loaded as a DLL extension by the AnyCA Gateway REST host
process — **not** a standalone service. Plugin type:
`Keyfactor.Extensions.CAPlugin.MarkMonitor.MarkMonitorCAPlugin`.

MarkMonitor's SSL API is backed by **DigiCert** (the only `provider` it supports).

## Solution layout

| Project | Role |
|---|---|
| `markmonitor-caplugin/` | The plugin. Dual-targets `net8.0` + `net10.0`; produces `MarkMonitorCAPlugin.dll` per TFM under `bin/Release/<tfm>/`, with `manifest.json` copied alongside (host discovery). |
| `markmonitor-caplugin.Tests/` | xUnit unit tests (mocked HTTP via `FakeHttpMessageHandler`; no live API). Targets `net8.0`. CI-safe. |
| `TestConsole/` | Manual live smoke-test console app (creates REAL orders). Needs `MARKMONITOR_*` env vars. Not CI-safe. |

## Key files (`markmonitor-caplugin/`)

| File | Responsibility |
|---|---|
| `MarkMonitorCAConnector.cs` | `IAnyCAPlugin` entry point. Methods the Gateway host calls: `Initialize`, `Enroll`, `Revoke`, `Synchronize`, `GetSingleRecord`, `Ping`, `ValidateCAConnectionInfo`, `ValidateProductInfo` (no-op), `GetProductIds`, `GetCAConnectorAnnotations` / `GetTemplateParameterAnnotations`. Holds the deserialized config and one lazily-built, cached `MarkMonitorClient` (`CreateAndAuthenticateClientAsync`). Contains the `RenewOrReissue` → revoke-prior logic and the `EnsureOrgNameConfigured` guard. |
| `Client/MarkMonitorClient.cs` | The MarkMonitor REST HTTP client. Owns: bearer-token auth (`X-API-KEY` header + username/password → token, cached with 30s early-expiry, double-checked locking); list pagination (`MarkMonitorPage.TotalPages`); CSR PEM/DER handling via BouncyCastle; ECC named-curve validation; the process-local enrollment dedup cache (5-min, keyed org\|product\|subject\|csr); `MarkMonitorCertificateStatusToCAStatus` mapping; `BuildErrorString` error parsing; org/contact/group resolution. |
| `MarkMonitorCAPluginConfig.cs` | CA-connection + enrollment-parameter schema, UI annotations/defaults, and canonical field-name constants: `ConfigConstants` (ApiKey, Username, Password=`"Password"`, BaseUrl, OrgId=`"OrgId"`, Enabled) and `EnrollmentConfigConstants` (AdditionalEmails, MarkmonitorGroup, MarkmonitorContact, DCVMethod, comments, locale, provider). Also `ConfigurationValidationException`. |
| `MarkMonitorConfig.cs` | The deserialized CA-connection config type used at runtime. |
| `Models/` | Request/response DTOs (orders, organizations, contacts, groups, token) + `Enums.cs`. |
| `Models/Enums.cs` | `CertOrderTypes` (product IDs → API strings via `[Description]`), `OrderStatus`, `OrderActions`, `AlgorithmTypes`, `DomainControlValidationMethods`, `CertServerPlatforms`, and `EnumExtensions.GetDescription()`. |
| `Constants.cs` | Empty placeholder — real constants live in `MarkMonitorCAPluginConfig`. |

## Core flows

- **Auth:** `POST /auth/v1/auth/authenticate` with `X-API-KEY` header + `{username,password}` → bearer
  token (cached, lazy refresh). No OAuth.
- **Enroll:** dedup-check → resolve org/contact/group → parse+validate CSR (reject ECC explicit
  curve) → `POST /certs/v1/order`. Accepted orders usually return `CREATED` →
  `EXTERNALVALIDATION` (pending DCV/approval). `RenewOrReissue` places a new order then revokes the
  prior cert (via `PriorCertSN` → `ICertificateDataReader`). No in-place renew/reissue in the enroll
  path.
- **Revoke:** requires `OrgId`; resolves it to a GUID, fetches order, compares owning org as parsed
  GUIDs, then `PATCH /certs/v1/order/{id}/revoke`. Reason code has no MarkMonitor field (logged only).
- **Sync:** `GET /certs/v1/order` paginated (fixed size 100), map status, assemble full chain,
  buffer issued certs. **Always full** — `lastSync`/`fullSync` not yet used for date filtering.

### MarkMonitor endpoints

`POST /auth/v1/auth/authenticate` · `GET|POST /certs/v1/order` · `GET /certs/v1/order/{id}` ·
`PATCH /certs/v1/order/{id}/{revoke|cancel|reissue}` · `GET /certs/v1/organization[/{id}]` ·
`GET /auth/v1/group`. (cancel/reissue exist in the client but are **not** wired into `IAnyCAPlugin`.)

### Status mapping (`OrderStatus` → `EndEntityStatus`)

`CREATED`→EXTERNALVALIDATION · `DIGI_ISSUED`→GENERATED · `DIGI_REVOKED`→REVOKED ·
pending set (`DIGI_PENDING`/`DIGI_PROCESSING`/`DIGI_REISSUE_PENDING`/`DIGI_WAITING_PICKUP`/
`REISSUE_PENDING`/`DIGI_NEEDS_APPROVAL`/`REISSUE_REQUEST_PENDING`)→INPROCESS ·
`DIGI_FAILED`/`DIGI_REISSUE_FAILED`→FAILED ·
`DIGI_CANCELED`/`DIGI_REJECTED`/`DIGI_EXPIRED`/`DIGI_NEEDS_CSR`→CANCELLED · else→FAILED.

## Gotchas / non-obvious behavior

- **Two credentials, used together:** API key (header) *and* service account (token). Missing either
  fails auth.
- **ECC CSRs must use a named curve** — explicit curve params are silently failed by MarkMonitor, so
  the plugin rejects them up front (`ValidateEccCsrUsesNamedCurve`).
- **DSA is not supported** for CSRs.
- **Enrollment dedup cache** is process-local and short-lived — guards Command retries only, not a
  durable store. Failed attempts are not cached.
- **`OrgId`** accepts a name or GUID; blank `OrgId` skips the revoke ownership check for some paths
  but `Revoke` refuses to run without it.
- **Order IDs** are validated as GUIDs before being interpolated into URLs.
- **Revocation reason** cannot be forwarded to MarkMonitor (no schema field).
- **`ValidateProductInfo` is a no-op** — contact/group are resolved and defaulted at enroll time, not
  validated at template save.

## Build / test / deploy

```shell
dotnet build markmonitor-caplugin.sln -c Release   # build (both TFMs)
dotnet test  markmonitor-caplugin.sln -c Release   # unit tests (CI-safe)
dotnet run --project TestConsole                   # live smoke test (needs MARKMONITOR_* env; destructive)
```

Deploy: copy a TFM output dir into the Gateway `Extensions` folder, restart the AnyCA Gateway REST
service.

## Docs & metadata to keep in sync

- `docsource/*.md` — source-of-truth doc fragments (`overview.md`, `architecture.md`,
  `configuration.md`, `development.md`, plus this `CODEMAP.md` and the internal `enrollment.md`).
  Root `README.md` is generated from `readme_source.md` + `docsource/` by the Keyfactor bootstrap
  workflow — edit `docsource/`, not the composed `README.md`.
- `integration-manifest.json` — catalog metadata (`product_ids`, `ca_plugin_config`,
  `enrollment_config`). Keep aligned with `MarkMonitorCAPluginConfig.cs` and `CertOrderTypes`.
