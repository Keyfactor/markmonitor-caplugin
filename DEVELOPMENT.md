# Developer Guide

This document covers local development, testing, live API smoke-testing, and the internal design of
the MarkMonitor AnyCA Gateway REST plugin. For production deployment and CA connector configuration
for end users, see [README.md](README.md).

## Prerequisites

- .NET SDK 8.0 **and** 10.0 (the plugin dual-targets `net8.0` and `net10.0`).
- A MarkMonitor API key and service account for live smoke-testing (see
  [Live smoke-testing](#live-smoke-testing-testconsole)). Not required to build or run the unit
  tests.

## Solution Layout

The solution (`markmonitor-caplugin.sln`) contains four projects:

| Project | Purpose |
|---|---|
| `markmonitor-caplugin/` | The plugin itself. Produces `MarkMonitorCAPlugin.dll` per TFM under `bin/Release/<tfm>/`. `manifest.json` is copied alongside the DLL on every build — it is how the AnyCA Gateway host discovers the plugin type (`Keyfactor.Extensions.CAPlugin.MarkMonitor.MarkMonitorCAPlugin`). |
| `markmonitor-caplugin.Tests/` | xUnit unit-test project (mocked HTTP, no live API). Targets `net8.0`. |
| `markmonitor-caplugin.IntegrationTests/` | xUnit live-API test project — real `MarkMonitorClient` calls against the actual MarkMonitor API (authenticate, list orgs, list certificate orders, RSA/ECC enroll). Targets `net8.0`. Each test skips (no-op pass) when the `MARKMONITOR_*` env vars aren't set, so it's always safe to run; when creds are present it creates and cleans up real orders. See [Live Integration Tests](#live-integration-tests-markmonitor-caplugin-integrationtests) below. |
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
`Extensions` folder and restart the AnyCA Gateway REST service (see the [README.md](README.md)
Installation section for the exact path).

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

## Live Integration Tests (`markmonitor-caplugin.IntegrationTests`)

The `markmonitor-caplugin.IntegrationTests/` project is an xUnit suite that hits the **real**
MarkMonitor API, covering the same scenarios `TestConsole/Program.cs` walks through by hand:
authenticate, list organizations, list certificate orders, and enroll (once with an RSA CSR, once
with an ECC CSR, both generated via `TestConsole.Helpers.CsrGenerator`/`EmailAddressGenerator` —
this project references `TestConsole/TestConsole.csproj` purely to reuse those two helpers, not to
run `TestConsole` itself).

Every test reads the same four `MARKMONITOR_*` environment variables `TestConsole` uses and returns
immediately (a silent pass, not a skip/failure) if any are unset:

```
MARKMONITOR_BASE_URL
MARKMONITOR_API_TOKEN
MARKMONITOR_USERNAME
MARKMONITOR_PASSWORD
```

This makes the project safe to run unconditionally — in CI or on a laptop with no `.env` sourced —
without ever failing for lack of credentials. It is **not** wired into `.github/workflows/unit-tests.yml`
(that workflow runs `markmonitor-caplugin.Tests/markmonitor-caplugin.Tests.csproj` directly, not the
whole solution).

To run locally:

```shell
set -a && source .env && set +a   # or TestConsole/.env
dotnet test markmonitor-caplugin.IntegrationTests -c Release
```

`.github/workflows/integration-tests.yml` runs the same command in CI, but only on a manual
`workflow_dispatch` — never on push or pull_request. The job also targets the `markmonitor-integration`
GitHub environment, which holds the four `MARKMONITOR_*` secrets and requires reviewer approval before
a dispatched run can access them — a second gate on top of the manual trigger, since a run creates
real (sandbox) orders.

Unlike `TestConsole`, the enrollment tests have **no `MARKMONITOR_SKIP_CLEANUP` escape hatch** — each
one always cancels (falling back to revoke) the order it creates before returning, win or lose,
since this is a repeatable automated suite rather than a manual inspection tool. A cleanup failure
throws (failing the test loudly) instead of just logging a warning, so a billable order that
couldn't be cleaned up is never left behind silently.

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
> incremental sync.

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
- `docsource/configuration.md` and `docsource/overview.md` are the source-of-truth, customer-facing
  documentation fragments. Root `README.md` is generated by the Keyfactor doctool
  (`~/RiderProjects/doctooldotnet`) from `docsource/configuration.md` + `integration-manifest.json` —
  never hand-edit `README.md`; edit `docsource/configuration.md` and regenerate. This `DEVELOPMENT.md`
  file, like `LICENSE`, is hand-maintained at the repo root and is **not** part of the doctool
  pipeline — edit it directly.
- `docsource/CODEMAP.md` is the orientation map for this repo — update it whenever you change the
  architecture, add/move key files, or change the build/test layout.

## Architecture

This section describes how the MarkMonitor AnyCA Gateway REST plugin integrates with Keyfactor
Command and the MarkMonitor SSL certificate API. It covers the primary certificate lifecycle
operations — synchronization, enrollment, and revocation — and how the plugin routes each through
the MarkMonitor REST API.

### Component Overview

See [README.md](README.md#component-overview) for the high-level component diagram. The two source
files that matter most:

* **`MarkMonitorCAConnector.cs`** — the `IAnyCAPlugin` entry point the Gateway host calls
  (`Initialize`, `Enroll`, `Revoke`, `Synchronize`, `GetSingleRecord`, `Ping`,
  `ValidateCAConnectionInfo`, `ValidateProductInfo`, `GetProductIds`, and the annotation pair). It
  holds the deserialized connection config and a single lazily-built `MarkMonitorClient`.
* **`Client/MarkMonitorClient.cs`** — the HTTP client for the MarkMonitor REST API. It owns
  bearer-token authentication, list pagination, CSR PEM/DER handling (via BouncyCastle), the
  enrollment dedup cache, and the MarkMonitor-order-status → Keyfactor-status mapping.

### Request Authentication

MarkMonitor uses two credentials together. The API key is sent as the `X-API-KEY` header on the
authentication request; the service-account username and password are POSTed to
`/auth/v1/auth/authenticate`, which returns a bearer token and its lifetime (`expiresIn`). Every
subsequent request carries `Authorization: Bearer <token>`.

The token is cached in the `MarkMonitorClient` instance with a small safety buffer (it is treated as
expired 30 seconds early so a request that starts just before expiry doesn't race the token dying
mid-flight). Re-authentication is lazy and guarded by double-checked locking on a semaphore: callers
that find a valid token proceed without locking; only a caller that observes an
expired/missing token takes the lock, and re-checks inside it, so concurrent operations cannot race
each other into two authentication calls or send requests under a half-written token.

```
Authorization: Bearer <token>   where token ← POST /auth/v1/auth/authenticate
                                              headers: X-API-KEY: <api key>
                                              body:    { username, password }
```

### Certificate Identifiers

MarkMonitor identifies each order by a **GUID order ID**. That order ID is what the plugin stores in
Keyfactor Command as the `CARequestID`, and it is the identifier used for every post-enrollment
operation (get single record, revoke). Because order IDs are interpolated directly into request
URLs, the client validates that any incoming order ID parses as a GUID before using it — a corrupted
or manipulated `CARequestID` fails fast with a clear error rather than producing an unexpected URL
path segment.

The configured `OrgId` may be supplied either as a friendly organization **name** or as a **GUID**.
The client resolves a name to its GUID by listing organizations filtered by name; a value that
already parses as a GUID is used directly.

---

### Gateway Startup

When the AnyCA Gateway process loads the connector, `Initialize` deserializes the CA connection data
into the plugin's config object. The `MarkMonitorClient` itself is not built until the first
operation needs it — `CreateAndAuthenticateClientAsync()` builds (or adopts an injected) client once
and caches it for the lifetime of the plugin instance. Each client method authenticates or
re-authenticates itself lazily, so startup does not force an eager authentication call.

```mermaid
sequenceDiagram
    participant GW as AnyCA Gateway
    participant Plugin as MarkMonitorCAPlugin
    participant API as MarkMonitor API

    GW->>Plugin: Initialize(configProvider, certificateDataReader)
    Plugin->>Plugin: Deserialize CAConnectionData into config
    Note over Plugin: Client is NOT built yet (lazy)
    GW->>Plugin: Ping()
    Plugin->>Plugin: CreateAndAuthenticateClientAsync()<br/>(builds & caches the client once)
    Plugin->>API: POST /auth/v1/auth/authenticate<br/>(X-API-KEY header + username/password)
    API-->>Plugin: Bearer token (+ expiry)
    Plugin->>API: GET /certs/v1/organization
    API-->>Plugin: Organizations
    Plugin-->>GW: Ping OK (auth works and at least one org exists)
```

---

### Synchronization

Keyfactor Command periodically synchronizes its certificate inventory with MarkMonitor. The plugin
retrieves all orders for the account, page by page (looping until `MarkMonitorPage.TotalPages` is
reached), and feeds the issued certificates into Command's buffer.

> **Note:** The current implementation performs a **full** listing on every sync — the `lastSync`
> timestamp and `fullSync` flag passed by the framework are not yet used to filter orders by date,
> and there is no configurable page size (the connector requests a fixed page size of 100). Orders
> that have no certificate yet are skipped for the current sync rather than aborting the page.

```mermaid
sequenceDiagram
    participant CMD as Keyfactor Command
    participant Plugin as MarkMonitorCAPlugin
    participant API as MarkMonitor API

    CMD->>Plugin: Synchronize(buffer, lastSync, fullSync, cancelToken)
    Plugin->>Plugin: CreateAndAuthenticateClientAsync()

    loop Retrieve one page at a time (until TotalPages)
        Plugin->>API: GET /certs/v1/order?page=N&size=100
        API-->>Plugin: Page of order records

        loop For each order on the page
            alt Order has no cert yet (e.g. CREATED / DIGI_NEEDS_CSR)
                Plugin->>Plugin: Skip for this sync
            else Order has a certificate
                Plugin->>Plugin: Map MarkMonitor status → Keyfactor status
                Plugin->>Plugin: Assemble end-entity + intermediate + root chain
                Plugin->>CMD: Add AnyCAPluginCertificate to buffer
            end
        end
    end

    Plugin->>CMD: CompleteAdding()
    Plugin-->>CMD: Synchronization complete
```

For each imported certificate the plugin builds the full chain by concatenating the end-entity,
intermediate, and root certificates returned by MarkMonitor, and records a revocation date when the
order's `RevokeStatus` is `REVOKED`. (MarkMonitor does not expose a revocation *reason* on its order
records, so a reason is not populated on the imported record.)

---

### Certificate Enrollment

When a requester submits a certificate request through Keyfactor Command, the plugin translates it
into a MarkMonitor order. It resolves the organization, contact, and (optional) group; validates and
normalizes the CSR; and places the order. Because DCV/approval is required before issuance, an
accepted order typically comes back pending.

```mermaid
sequenceDiagram
    participant CMD as Keyfactor Command
    participant Plugin as MarkMonitorCAPlugin
    participant API as MarkMonitor API

    CMD->>Plugin: Enroll(csr, subject, san, productInfo, format, enrollmentType)
    Plugin->>Plugin: CreateAndAuthenticateClientAsync()
    Plugin->>Plugin: Check dedup cache (org|product|subject|csr)

    alt Identical enrollment already in flight / just completed
        Plugin-->>CMD: Await & return the original result (no duplicate order)
    else New request
        Plugin->>Plugin: Reserve dedup key BEFORE any real work
        Plugin->>API: Resolve organization (name→GUID) & contact & group
        Plugin->>Plugin: Parse CSR, reject ECC explicit-curve params,<br/>derive RSA/ECC, convert to PEM
        Plugin->>API: POST /certs/v1/order (product, org, contact, DCV method, CSR)
        API-->>Plugin: Order created — GUID order ID + status

        alt enrollmentType == RenewOrReissue
            Plugin->>API: Revoke prior cert (resolved from PriorCertSN)
        end

        Plugin-->>CMD: EnrollmentResult (CARequestID = order ID, mapped status)
    end
```

#### Enrollment inputs resolved from template parameters

The plugin reads these product/template parameters (case-insensitive) when building the order —
falling back to defaults or a best-effort lookup when a value is missing or cannot be resolved:

* **Organization** — resolved from the connector `OrgId` (name or GUID). Enrollment fails if it
  cannot be resolved.
* **Contact** (`MarkmonitorContact`) — matched within the org's contacts by GUID, then email, then
  `First Last` name. Falls back to the org's `ORGANIZATION_CONTACT` (or the first contact) if the
  parameter is blank or unresolved (logged as a warning).
* **Group** (`MarkmonitorGroup`) — MarkMonitor groups are account-/tenant-wide, so this is matched by
  GUID or exact (case-insensitive) name against `/auth/v1/group`. A blank or unresolved group is
  simply omitted (best-effort; never fails the enrollment).
* **DCV method** (`DCVMethod`) — validated against `EMAIL`, `DNS_CNAME_TOKEN`, `HTTP_TOKEN`,
  `DNS_TXT_TOKEN`; an invalid value logs a warning and falls back to `EMAIL`.
* **Additional emails** (`AdditionalEmails`), **comments** (`comments`), **locale** (`locale`),
  **provider** (`provider`) — see the Template Enrollment Parameters table in
  [README.md](README.md).

#### Renewal / Reissue

MarkMonitor exposes reissue and cancel endpoints (`PATCH /certs/v1/order/{id}/reissue`,
`PATCH /certs/v1/order/{id}/cancel`), but the enrollment path does **not** use them. A
`RenewOrReissue` enrollment always places a brand-new order and then revokes the prior certificate.
The prior certificate's serial number is supplied by the framework in
`productInfo.ProductParameters["PriorCertSN"]`; the plugin resolves it to a `CARequestID` via the
injected `ICertificateDataReader` and revokes it after the replacement has issued successfully. If
`PriorCertSN` is missing or cannot be resolved, the enrollment is treated as a plain new issuance and
no revoke is attempted — a failure to revoke the old certificate never fails delivery of the new one.

```mermaid
flowchart TD
    A([RenewOrReissue enrollment]) --> B[Place NEW MarkMonitor order]
    B --> C{"PriorCertSN present<br/>in product parameters?"}
    C -- No --> D([Treat as new issuance — done])
    C -- Yes --> E["Resolve PriorCertSN → CARequestID<br/>via ICertificateDataReader"]
    E --> F{"Resolved and<br/>OrgId configured?"}
    F -- No --> G([Log warning — prior cert not revoked])
    F -- Yes --> H["Revoke prior order<br/>(with org-ownership check)"]
    H --> I([New cert delivered, prior revoked])
    D --> I
    G --> I
```

---

### Revocation

When a certificate is revoked in Keyfactor Command, the plugin first ensures an `OrgId` is
configured, then verifies the target order belongs to that organization before calling MarkMonitor's
revoke endpoint.

```mermaid
sequenceDiagram
    participant CMD as Keyfactor Command
    participant Plugin as MarkMonitorCAPlugin
    participant API as MarkMonitor API

    CMD->>Plugin: Revoke(orderId, hexSerialNumber, revocationReason)
    Plugin->>Plugin: EnsureOrgNameConfigured()<br/>(refuse if OrgId is blank)
    Plugin->>Plugin: Validate orderId is a GUID
    Plugin->>API: Resolve configured OrgId → GUID
    Plugin->>API: GET /certs/v1/order/{orderId}

    alt Order org != configured org (compared as parsed GUIDs)
        Plugin-->>CMD: Error — refusing to revoke (different organization)
    else Order belongs to configured org
        Note over Plugin: revocationReason has no MarkMonitor field —<br/>logged if non-default, not sent
        Plugin->>API: PATCH /certs/v1/order/{orderId}/revoke
        API-->>Plugin: Revocation confirmed
        Plugin-->>CMD: REVOKED
    end
```

**Ownership check:** the configured org and the order's org are compared as **parsed GUIDs**, not raw
strings, so an admin-configured `OrgId` in a non-canonical format (braces, no dashes, etc.) still
matches MarkMonitor's canonical serialization of the same GUID.

**Reason codes:** MarkMonitor's revoke schema has no reason field, so the Keyfactor reason code
cannot be forwarded. A non-default reason is logged rather than silently dropped.

---

### Connector Validation

When an administrator saves or edits the CA connector, `ValidateCAConnectionInfo` checks the supplied
fields before the connector can be saved in an enabled state.

```mermaid
flowchart TD
    A([Save connector configuration]) --> B{"ApiKey, Username,<br/>Password all present?"}
    B -- Missing --> E([Validation error shown to administrator])
    B -- Present --> C{"BaseUrl starts with https://<br/>(or blank → default)?"}
    C -- Not https --> E
    C -- OK --> D{"OrgId present?"}
    D -- Missing --> E
    D -- Present --> F([Connector saved])
```

`ValidateCAConnectionInfo` performs field-level validation only (it does not place a live API call);
`Ping` is the live connectivity check — it authenticates and lists organizations. `ValidateProductInfo`
is intentionally a no-op: contact/group values are resolved (and gracefully defaulted) at enroll
time rather than validated at template-save time, to avoid coupling template configuration to
MarkMonitor's availability.

---

### Order Status Mapping

See the [Order Status Mapping table](README.md#order-status-mapping) in README.md for the full
MarkMonitor-status → Keyfactor-status table. The mapping is implemented in
`MarkMonitorClient.MarkMonitorCertificateStatusToCAStatus`.

`CREATED` is deliberately mapped to `EXTERNALVALIDATION`, not `INITIALIZED`. The AnyCA Gateway REST
framework treats `INITIALIZED` as a hard enrollment failure, which caused freshly-created orders
(that were in fact accepted by MarkMonitor and simply awaiting DCV/issuance) to be reported as
failures. `EXTERNALVALIDATION` is what the framework treats as "accepted, still pending".

### API Endpoint Reference

See the [API Endpoint Reference table](README.md#api-endpoint-reference) in README.md for the full
list of MarkMonitor endpoints the plugin calls.

> The cancel and reissue endpoints exist in the client but are not currently invoked by the
> `IAnyCAPlugin` operations (enrollment reissue is implemented as new-order-plus-revoke; see
> [Renewal / Reissue](#renewal--reissue)).
