# CODEMAP

> **Purpose:** a fast orientation map of this repository for humans and AI agents. Read this first
> before diving into the source.
>
> **⚠️ Maintenance rule — any agent (or developer) that makes a code change MUST update this
> CODEMAP in the same change** if the change adds/removes/moves a source file of note, alters the
> architecture or a data flow, changes the build/test layout, or changes config/enrollment fields or
> product IDs. Keep it terse and accurate; a stale codemap is worse than none. This file is committed
> to the repo

Last verified against the codebase: 2026-08-12.

> To understand behavior, read this CODEMAP + the source it points to — **not** the root `README.md`
> (it's a generated artifact; parsing it wastes tokens). A CI check fails a PR that changes plugin
> source without updating this file (add the `skip-codemap` label to override).

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
| `markmonitor-caplugin.Tests/` | xUnit unit tests (mocked HTTP via `FakeHttpMessageHandler`; no live API). Targets `net8.0`. CI-safe; run by `.github/workflows/unit-tests.yml`. |
| `markmonitor-caplugin.IntegrationTests/` | xUnit live-API tests (real `MarkMonitorClient` calls: authenticate, list orgs, list certificate orders, RSA/ECC enroll). Targets `net8.0`; references `TestConsole` solely to reuse its `CsrGenerator`/`EmailAddressGenerator` helpers. Every test skips silently (no-op pass) when the `MARKMONITOR_*` env vars aren't set, so it's safe to run without creds — but when creds are present it hits the live API and creates/cleans-up (cancel, falling back to revoke) real orders. **Not** run by `unit-tests.yml` (that workflow targets the `.Tests` csproj directly); run manually via `.github/workflows/integration-tests.yml` (`workflow_dispatch` only) against the `markmonitor-integration` GitHub environment, which holds the `MARKMONITOR_*` secrets behind required-reviewer approval. |
| `TestConsole/` | Manual live smoke-test console app (creates REAL orders). Needs `MARKMONITOR_*` env vars. Not CI-safe. |

## Key files (`markmonitor-caplugin/`)

| File | Responsibility |
|---|---|
| `MarkMonitorCAConnector.cs` | `IAnyCAPlugin` entry point. Methods the Gateway host calls: `Initialize`, `Enroll`, `Revoke`, `Synchronize`, `GetSingleRecord`, `Ping`, `ValidateCAConnectionInfo` (field checks, then a live auth + org-list call via a transient client - never `_cachedClient`), `ValidateProductInfo` (cheap static `ProductID` enum check only - contact/group stay a no-op), `GetProductIds`, `GetCAConnectorAnnotations` / `GetTemplateParameterAnnotations`. Holds the deserialized config and one lazily-built, cached `MarkMonitorClient` (`CreateAndAuthenticateClientAsync`). Contains the `RenewOrReissue` → revoke-prior logic and the `EnsureOrgNameConfigured` guard. |
| `Client/MarkMonitorClient.cs` | The MarkMonitor REST HTTP client. Owns: bearer-token auth (`X-API-KEY` header + username/password → token, cached with 30s early-expiry, double-checked locking); list pagination (`MarkMonitorPage.TotalPages`); CSR PEM/DER handling via BouncyCastle; ECC named-curve validation; the process-local enrollment dedup cache (5-min, keyed org\|product\|subject\|csr); `MarkMonitorCertificateStatusToCAStatus` mapping; `BuildErrorString` error parsing; org/contact/group resolution; `SendWithRetryAsync` (3-attempt retry with jittered exponential backoff on network failures/timeouts and 5xx/429, honoring `Retry-After` on 429 - **not** used for the order-create POST or the reissue PATCH, both of which risk creating a duplicate billable resource on an ambiguous failure); `HttpClient.Timeout` from the `TimeoutSeconds` config field (default 120s). |
| `MarkMonitorCAPluginConfig.cs` | CA-connection + enrollment-parameter schema, UI annotations/defaults, and canonical field-name constants: `ConfigConstants` (ApiKey, Username, Password=`"Password"`, BaseUrl, OrgId=`"OrgId"`, Enabled, TimeoutSeconds) and `EnrollmentConfigConstants` (AdditionalEmails, MarkmonitorGroup, MarkmonitorContact, DCVMethod, comments, locale, provider). Also `ConfigurationValidationException`. |
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
  prior cert (via `PriorCertSN` → `ICertificateDataReader`), but only when that prior cert's
  resolvable expiration date is within its `RenewalWindowDays` template param (default 90) - if it's
  resolvable and outside the window, the prior cert is left unrevoked and the request behaves like a
  plain new issuance (unresolvable expiration falls back to always revoking, the pre-existing
  behavior). No in-place renew/reissue in the enroll path. MarkMonitor issues CN ∪ order `dnsNames` - it does **not** honor a CSR's own SAN extension
  as authoritative - so `BuildDnsNames` unions the Enroll `san` dictionary (`Dns`/`dnsname` keys,
  case-insensitive) with any SAN extension embedded in the CSR itself before submitting; non-DNS SAN
  types (IP/email/URI) have no MarkMonitor field and are dropped with a logged warning.
- **Revoke:** requires `OrgId`; resolves it to a GUID, fetches order, compares owning org as parsed
  GUIDs, then `PATCH /certs/v1/order/{id}/revoke`. Reason code has no MarkMonitor field (logged only).
  `CancelCertificateAsync` takes the same optional `orgName` parameter and runs the identical
  cross-organization ownership check (shared via a private `EnsureOrderBelongsToOrganizationAsync`
  helper) before `PATCH /certs/v1/order/{id}/cancel`.
- **Sync:** `GET /certs/v1/order` paginated (size from the `PageSize` config field, default 100),
  map status, assemble full chain, buffer issued certs. Still **always a full listing** —
  `lastSync`/date filtering not yet used — but each record is now checked against
  `ICertificateDataReader` and skipped if Command already has it at the same status (skip-unchanged);
  `ForceCompleteSync` (config) or Command's own `fullSync` flag bypasses that. A bad individual
  record is logged + counted + skipped rather than aborting the sync, but an error rate over 25%
  (once ≥50 records observed) aborts the whole sync as a circuit breaker.

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
- **Org name resolution requires an exact (case-insensitive) name match** — `ResolveOrganizationAsync`/
  `ResolveOrganizationIdAsync` filter MarkMonitor's `/certs/v1/organization?name=` search results down
  to an exact match rather than taking the first result, since that endpoint's own matching semantics
  aren't guaranteed to be exact (a configured name that's a substring of another org's name must not
  silently resolve to the wrong org — [#9](../../issues/9)).
- **Order IDs** are validated as GUIDs before being interpolated into URLs.
- **Revocation reason** cannot be forwarded to MarkMonitor (no schema field).
- **`ValidateProductInfo`'s contact/group handling stays a no-op** — resolved and defaulted at enroll
  time, not validated at template save (a deliberate tradeoff documented in the method's own
  comment). It does now reject an unparseable `ProductID` (a cheap static enum check, no live call).
- **`ValidateCAConnectionInfo` makes a live MarkMonitor call** (authenticate + list one organization)
  after its field checks pass, via a transient client built from the connectionInfo being saved -
  never `_cachedClient`. A constructor-injected client (the same test seam every other method uses)
  is reused as-is and left undisposed, rather than building a second transient one, so tests don't
  need a live API.

## Build / test / deploy

```shell
dotnet build markmonitor-caplugin.sln -c Release                  # build (both TFMs)
dotnet test  markmonitor-caplugin.sln -c Release                  # unit + integration tests (integration tests no-op skip without MARKMONITOR_* env)
dotnet test  markmonitor-caplugin.IntegrationTests -c Release      # live-API tests only (skips without MARKMONITOR_* env; creates/cleans up real orders when creds present)
dotnet run --project TestConsole                                   # live smoke test (needs MARKMONITOR_* env; destructive)
```

Deploy: copy a TFM output dir into the Gateway `Extensions` folder, restart the AnyCA Gateway REST
service.

## Docs & metadata to keep in sync

- `docs/` — the customer-facing GitHub Pages site (Jekyll + the `just-the-docs` remote theme,
  Keyfactor-branded via `docs/_sass/color_schemes/keyfactor.scss`), published from `main`/`docs` with
  no Actions workflow. **Not** generated from `docsource/` — its five pages (`index.md`,
  `overview.md`, `installation.md`, `configuration.md`, `changelog.md`) carry independently-written,
  lighter-tone copy aimed at customers, and its own copies of the screenshots under
  `docs/assets/images/` (copied from `docsource/images/`, not symlinked). When a connector field,
  enrollment parameter, or product ID changes, update both `docsource/configuration.md` (feeds
  `README.md`) **and** `docs/configuration.md` (feeds the Pages site) — they are two separate,
  hand-maintained copies by design, not one generating the other. `docs/changelog.md` only links out
  to the root `CHANGELOG.md`; Jekyll's build root is `docs/`, so it cannot `include_relative` a file
  outside that directory. Preview it locally before publishing with `just docs-preview` (needs
  Docker; runs Jekyll via the `jekyll/jekyll` image, no local Ruby toolchain required) at
  `http://localhost:4000/markmonitor-caplugin/`; `docs/Gemfile` pins the plugins (`jekyll-remote-theme`,
  `jekyll-seo-tag`, `jekyll-include-cache`) `just-the-docs` needs that aren't in the base image.
- `docsource/configuration.md` and `docsource/overview.md` — source-of-truth, customer-facing doc
  fragments, plus this `CODEMAP.md`. Root `README.md` is **generated by doctool**
  (`Keyfactor/doctooldotnet`, cloned at `~/RiderProjects/doctooldotnet`) from `docsource/
  configuration.md` + `integration-manifest.json` — never hand-edit `README.md`; edit
  `docsource/configuration.md` and regenerate:
  `cd ~/RiderProjects/doctooldotnet && just build && just docs <repo-path> markmonitor-caplugin`.
  Only `configuration.md` (including its hand-written `## How It Works` custom section) and the
  manifest feed the README; `overview.md` stays standalone. Images referenced from
  `configuration.md` must use `docsource/images/...`. (`readme_source.md` is a stale placeholder and
  is not used.)
- Root `DEVELOPMENT.md` is the hand-maintained developer guide (solution layout, build/test,
  `TestConsole` smoke-testing, and full architecture/design internals with real class and method
  names) — like `LICENSE`, it is **not** part of the doctool pipeline and is edited directly.
  `docsource/architecture.md`, `docsource/development.md`, and the internal-only
  `docsource/enrollment.md` have been removed/merged into it (the MarkMonitor login walkthrough in
  the old `enrollment.md` now lives in an internal Confluence doc, not this repo).
- `integration-manifest.json` — catalog metadata (`product_ids`, `ca_plugin_config`,
  `enrollment_config`). Keep aligned with `MarkMonitorCAPluginConfig.cs` and `CertOrderTypes`.
