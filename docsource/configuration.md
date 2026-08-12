## Overview

The MarkMonitor AnyCA Gateway REST plugin extends the certificate lifecycle capabilities of the
MarkMonitor SSL certificate service to Keyfactor Command via the Keyfactor AnyCA Gateway REST. It
implements `IAnyCAPlugin` and is loaded as a DLL extension by the AnyCA Gateway REST host process —
it is not a standalone service. The plugin supports the following capabilities:

* CA Synchronization:
    * Downloads all certificate orders visible to the configured MarkMonitor organization and
      imports the issued certificates (and their chains) into Keyfactor Command.
    * Orders that have not yet produced a certificate are mapped to the appropriate pending/failed
      status rather than imported as certificates.
* Certificate Enrollment for the SSL product types MarkMonitor exposes:
    * Submits a new MarkMonitor certificate order per product type.
    * A process-local dedup guard prevents Keyfactor Command retries from creating duplicate orders.
* Renewal / Reissue:
    * MarkMonitor has no in-place "renew" enrollment endpoint through this plugin, so a
      `RenewOrReissue` enrollment places a **new** order and then revokes the prior certificate once
      the replacement has been created successfully.
* Certificate Revocation:
    * Revokes a previously issued certificate, with a cross-organization ownership check that refuses
      to revoke an order belonging to a different organization than the one the CA connector is
      configured for.

MarkMonitor's SSL API is backed by DigiCert (the only certificate `provider` its API currently
supports), so issued certificates chain up to DigiCert roots.

## Requirements

- A MarkMonitor **API Key** (contact MarkMonitor support to obtain one).
- A MarkMonitor **service account** (username and password) with permission to create certificate
  orders.
- The **organization** name or ID (GUID) the certificates will be ordered under.
- Keyfactor Command >= v12.0.0.
- AnyCA Gateway REST >= v25.5.0.
- Network connectivity from the AnyCA Gateway host to the MarkMonitor API base URL, and trust of the
  DigiCert issuing CA chain on both the gateway host and the Command server (see
  [Gateway Registration](#gateway-registration)).

## MarkMonitor API Setup

MarkMonitor requires **two** credentials that are used together (there is no OAuth mode):

1. **API Key** — sent as the `X-API-KEY` header on the authentication request. Enter it in the
   `ApiKey` connector field (masked in the Command UI).
2. **Service-account username and password** — POSTed to `/auth/v1/auth/authenticate`, which returns
   a short-lived bearer token used on all subsequent calls. Enter them in the `Username` and
   `Password` connector fields (the password is masked in the UI).

Contact your MarkMonitor administrator to provision the API key and a service account with order
permissions, and to confirm the correct API base URL and organization name/ID for your environment.

## Gateway Registration

In order to enroll for certificates the Keyfactor Command server must trust the issuing CA chain.
MarkMonitor's default issuing CA (`provider`) is **DigiCert** — download and import the appropriate
certificate chain from <https://www.digicert.com/kb/digicert-root-certificates.htm> to the AnyCA
Gateway host and Command server.

Once the necessary files are copied to the appropriate locations and the AnyCA Gateway REST is up and
running, navigate to the AnyCA Gateway REST portal and configure the CA.

### Using file path for issuing CA certificate
![gateway_registration_local_file.png](docsource/images/gateway_registration_local_file.png)

### Using Keyfactor Command certificate store for issuing CA certificate
> **⚠️ Warning:** The cert store must already exist in Keyfactor Command.

![gateway_registration_store.png](docsource/images/gateway_registration_store.png)

## CA Connection Configuration

The following fields are presented in the AnyCA Gateway REST portal (and the Keyfactor Command
Management Portal) when creating or editing the MarkMonitor CA connector. All fields except `Enabled`
must be provided before the connector can be saved in an enabled state.

![gateway_ca_configuration.png](docsource/images/gateway_ca_configuration.png)

| Field | Required / Optional | Masked | Default | Description |
|---|---|---|---|---|
| `ApiKey` | Required | Yes | *(none)* | The MarkMonitor API key, sent as the `X-API-KEY` header when authenticating. |
| `Username` | Required | No | *(none)* | Username for the MarkMonitor API service account. |
| `Password` | Required | Yes | *(none)* | Password for the MarkMonitor API service account. |
| `BaseUrl` | Required | No | `https://api.markmonitor.com` | The MarkMonitor API base URL. Must start with `https://` — credentials and the bearer token are sent to it. |
| `OrgId` | Required | No | *(none)* | The MarkMonitor organization to use for API calls. Accepts either the organization **name** (e.g. `MarkMonitor`) or its **ID in GUID format**. Used to scope enrollment and to verify ownership on revoke. |
| `Enabled` | Optional | No | `true` | Enables or disables gateway functionality. Disable to allow the CA to be created before configuration information is available. |
| `TimeoutSeconds` | Optional | No | `120` | The HTTP request timeout, in seconds, for calls to the MarkMonitor API. |
| `PageSize` | Optional | No | `100` | The number of certificate orders requested per page during synchronization. Clamped to 1-500. |
| `ForceCompleteSync` | Optional | No | `false` | When `true`, bypasses the skip-unchanged synchronization optimization and re-emits every order on every sync. |
| `PickupRetries` | Optional | No | `5` | How many times `Enroll` polls a freshly-created order for issuance before returning it in its still-pending state. `0` disables polling. |
| `PickupDelaySeconds` | Optional | No | `10` | The delay, in seconds, between issuance pickup polls. |

> **Note:** Credentials are stored in Keyfactor Command's encrypted gateway configuration. `ApiKey`
> and `Password` are masked in the UI and are never written to logs by the plugin.

## Certificate Template Creation Step

A certificate template must be created in Keyfactor Command for each MarkMonitor product type you
want to enroll. One template is required per product type (see [Product IDs](#product-ids)). Below is
an example of a template for a GeoTrust DV SSL certificate. For more on certificate product types,
contact your MarkMonitor administrator or support.

![gateway_template.png](docsource/images/gateway_template.png)

## Template Enrollment Parameters

Custom enrollment parameters can be added to templates in Keyfactor Command after they have been
imported from the AnyCA Gateway. **All parameters are optional** and are read case-insensitively at
enrollment time.

![template_enrollment_params.png](docsource/images/template_enrollment_params.png)

| Parameter | Type | Default | Description |
|---|---|---|---|
| `AdditionalEmails` | String | *(empty)* | Zero or more comma-separated email addresses that MarkMonitor will send the issued certificate to. Spaces are also treated as separators. |
| `MarkmonitorGroup` | String | *(none)* | The name or GUID of a MarkMonitor group to associate with the order. Matched by GUID or exact (case-insensitive) name against the account-wide group list. A blank or unresolved value is omitted (best-effort). |
| `MarkmonitorContact` | String | *(org default)* | The GUID, email, or `First Last` name of a MarkMonitor contact within the organization. Falls back to the organization's default contact if not specified or not resolvable. |
| `DCVMethod` | String | `EMAIL` | Domain Control Validation method. Valid values: `EMAIL`, `DNS_CNAME_TOKEN`, `HTTP_TOKEN`, `DNS_TXT_TOKEN`. An invalid value logs a warning and falls back to `EMAIL`. |
| `comments` | String | `Requested via Keyfactor Command` | Free-text comments attached to the MarkMonitor order. |
| `locale` | String | `en` | Locale for the MarkMonitor order. |
| `provider` | String | `DIGICERT` | The certificate provider for the order. `DIGICERT` is currently the only provider the MarkMonitor API supports. |
| `RenewalWindowDays` | Number | `90` | For a `RenewOrReissue` enrollment, how many days before its expiration the prior certificate must be within before it's revoked after the replacement issues. Outside that window, the prior certificate is left unrevoked and the request is treated like a plain new issuance. An invalid (non-numeric or non-positive) value falls back to the default. |

> **Note on DCV:** The plugin passes the selected `DCVMethod` to MarkMonitor but does not itself
> automate DNS/HTTP token publication. For `EMAIL` (the default), MarkMonitor falls back to the
> domain/organization's registered DCV contacts; the plugin sends an empty `dcvEmails` list.

## Product IDs

`GetProductIds()` returns the names of the `CertOrderTypes` enum. The **Product ID** column is the
value Keyfactor Command sees and stores; the plugin maps it to the **MarkMonitor cert type** string
(the enum's `[Description]`) when placing an order. Adding a new MarkMonitor product means adding an
enum member with the matching `[Description]` — not editing a separate list.

| Product ID (Command) | MarkMonitor cert type | Typical product family |
|---|---|---|
| `SslOvBasic` | `SSL_OV_BASIC` | OV SSL (basic) |
| `SslEvBasic` | `SSL_EV_BASIC` | EV SSL (basic) |
| `SslDvGeotrust` | `SSL_DV_GEOTRUST` | GeoTrust DV SSL |
| `SslDvThawte` | `SSL_DV_THAWTE` | Thawte DV SSL |
| `SslOvThawteWebserver` | `SSL_OV_THAWTE_WEBSERVER` | Thawte OV Web Server SSL |
| `SslEvThawteWebserver` | `SSL_EV_THAWTE_WEBSERVER` | Thawte EV Web Server SSL |
| `SslOvGeotrustTruebizid` | `SSL_OV_GEOTRUST_TRUEBIZID` | GeoTrust OV True BusinessID SSL |
| `SslEvGeotrustTruebizid` | `SSL_EV_GEOTRUST_TRUEBIZID` | GeoTrust EV True BusinessID SSL |
| `SslOvSecuresite` | `SSL_OV_SECURESITE` | DigiCert OV Secure Site SSL |
| `SslEvSecuresite` | `SSL_EV_SECURESITE` | DigiCert EV Secure Site SSL |
| `SslOvSecuresitePro` | `SSL_OV_SECURESITE_PRO` | DigiCert OV Secure Site Pro SSL |
| `SslEvSecuresitePro` | `SSL_EV_SECURESITE_PRO` | DigiCert EV Secure Site Pro SSL |

> **Note:** The "typical product family" column is descriptive. Which product types your MarkMonitor
> account may actually order — and any per-product required fields — depend on your account's
> entitlements. Confirm availability with your MarkMonitor administrator.

## How It Works

This section describes, at a high level, how the plugin connects Keyfactor Command's certificate
lifecycle operations to the MarkMonitor SSL certificate service.

### Component Overview

```
┌─────────────────────────────────────────────────────────┐
│                  Keyfactor Command                       │
│                                                          │
│   Certificate Enrollment  ·  Revocation  ·  Sync Jobs    │
└────────────────────────────┬─────────────────────────────┘
                             │
                    AnyCA Gateway REST
                    (plugin host process)
                             │
┌────────────────────────────▼─────────────────────────────┐
│                   The MarkMonitor plugin                  │
│                                                          │
│   Translates Keyfactor operations into MarkMonitor API   │
│   calls and maps responses back to Command's data model. │
└────────────────────────────┬─────────────────────────────┘
                             │  HTTPS · Bearer token + X-API-KEY
                             │
┌────────────────────────────▼─────────────────────────────┐
│               MarkMonitor REST API (DigiCert)            │
│                                                          │
│   /auth/v1/auth/authenticate   /certs/v1/order           │
│   /certs/v1/organization       /auth/v1/group            │
└──────────────────────────────────────────────────────────┘
```

### Synchronization

Keyfactor Command periodically synchronizes its certificate inventory with MarkMonitor. The plugin
retrieves all certificate orders visible to the configured organization, page by page, and imports
issued certificates — along with their full certificate chain — into Command.

```mermaid
sequenceDiagram
    participant CMD as Keyfactor Command
    participant Plugin as MarkMonitor plugin
    participant API as MarkMonitor API

    CMD->>Plugin: Start synchronization
    Plugin->>API: Authenticate with MarkMonitor

    loop Retrieve one page of orders at a time
        Plugin->>API: List certificate orders
        API-->>Plugin: Page of order records

        loop For each order on the page
            alt Order has no certificate yet
                Plugin->>Plugin: Skip for this sync
            else Order has a certificate
                Plugin->>Plugin: Map the MarkMonitor status to a Keyfactor status
                alt Unchanged since Command's last known status (and not forced)
                    Plugin->>Plugin: Skip re-emission
                else New or changed
                    Plugin->>Plugin: Assemble the full certificate chain
                    Plugin->>CMD: Add certificate to Command's inventory
                end
            end
        end
    end

    Plugin-->>CMD: Synchronization complete (emitted / skipped-unchanged / errored counts)
```

> A record that fails to process is logged, counted, and skipped rather than aborting the sync - but
> the sync aborts outright if more than 25% of records fail once at least 50 have been observed.

> The current implementation always performs a full listing of orders on each sync, rather than only
> retrieving certificates that changed since the last sync - `PageSize` optimizes the *mitigation*, not
> the listing itself: each order is compared against what Command already has for that request ID, and
> skipped (not re-emitted) when the status is unchanged, unless `ForceCompleteSync` is enabled or
> Command requests a full sync. Orders that have not yet produced a certificate are simply skipped for
> that sync rather than treated as an error. A record that fails to process (bad status string, bad
> date, unexpected null) is logged and skipped rather than aborting the sync - but if more than 25% of
> records fail once at least 50 have been observed, the sync aborts outright rather than reporting a
> quietly near-empty "success".

### Certificate Enrollment (including Renewal / Reissue)

When a requester submits a certificate request through Keyfactor Command, the plugin translates it
into a MarkMonitor order: it resolves the organization, contact, and (optional) group; validates and
normalizes the CSR; and submits the order. MarkMonitor never issues synchronously from the
create-order call - a newly submitted order always comes back pending (typically `CREATED`), since
Domain Control Validation (and, in some environments, manual approval) is required first. For a
product whose DCV/approval resolves quickly, the plugin polls the order for up to `PickupRetries`
attempts (every `PickupDelaySeconds`) before returning, so Command can get the issued certificate
back from the enroll call itself rather than always waiting for the next sync; `PickupRetries=0`
disables this and restores the original always-returns-pending behavior.

```mermaid
sequenceDiagram
    participant CMD as Keyfactor Command
    participant Plugin as MarkMonitor plugin
    participant API as MarkMonitor API

    CMD->>Plugin: Submit certificate request
    Plugin->>API: Authenticate with MarkMonitor
    Plugin->>Plugin: Check for a duplicate in-flight request

    alt An identical request was already submitted / just completed
        Plugin-->>CMD: Return the original result (no duplicate order placed)
    else New request
        Plugin->>API: Resolve organization, contact, and group
        Plugin->>Plugin: Validate and normalize the CSR
        Plugin->>API: Submit the certificate order
        API-->>Plugin: Order accepted — order ID and status

        opt Not yet issued and PickupRetries > 0
            loop Up to PickupRetries times, every PickupDelaySeconds
                Plugin->>API: Poll the order
                API-->>Plugin: Current status
            end
        end

        alt Renewal/Reissue request
            Plugin->>API: Revoke the certificate being replaced
        end

        Plugin-->>CMD: Enrollment result (order ID, current status)
    end
```

> A concurrent duplicate request folded into this same in-flight reservation (the "identical request
> already submitted" branch above) receives the polled result too, not just the original pending
> status - the fold happens after polling completes, not before.

MarkMonitor has no in-place "renew" enrollment endpoint through this plugin, so a Renewal/Reissue
request always places a brand-new order. Once the replacement certificate has been created
successfully, the plugin revokes the certificate it is replacing — but only if that certificate is
within its `RenewalWindowDays` template parameter (default 90) of expiring. If the certificate being
replaced can't be identified, or still has substantial life left (outside the renewal window), the
request is simply treated as a new issuance and the prior certificate is left alone — a failure to
revoke the old certificate never blocks delivery of the new one either.

### Revocation

When a certificate is revoked in Keyfactor Command, the plugin confirms that the target order belongs
to the organization the CA connector is configured for before calling MarkMonitor's revoke operation.

```mermaid
sequenceDiagram
    participant CMD as Keyfactor Command
    participant Plugin as MarkMonitor plugin
    participant API as MarkMonitor API

    CMD->>Plugin: Revoke certificate
    Plugin->>API: Authenticate with MarkMonitor
    Plugin->>API: Look up the order's owning organization

    alt Order belongs to a different organization
        Plugin-->>CMD: Error — refusing to revoke (different organization)
    else Order belongs to the configured organization
        Plugin->>API: Revoke the order
        API-->>Plugin: Revocation confirmed
        Plugin-->>CMD: Certificate marked revoked
    end
```

> MarkMonitor's revoke operation has no field for a revocation reason code, so the reason supplied by
> Keyfactor Command cannot be forwarded to MarkMonitor.

### Connector Validation

When an administrator saves or edits the CA connector, the plugin checks the supplied configuration
before the connector can be saved in an enabled state.

```mermaid
flowchart TD
    A([Save connector configuration]) --> B{"API Key, Username,<br/>Password all present?"}
    B -- Missing --> E([Validation error shown to administrator])
    B -- Present --> C{"Base URL starts with https://<br/>(or blank → default)?"}
    C -- Not https --> E
    C -- OK --> D{"Organization present?"}
    D -- Missing --> E
    D -- Present --> G{"Authenticate with the<br/>submitted credentials"}
    G -- Fails --> E
    G -- Succeeds --> H{"At least one organization<br/>visible?"}
    H -- No / fails --> E
    H -- Yes --> F([Connector saved])
```

After the field checks above pass, the plugin also places a live call to MarkMonitor: it
authenticates with the submitted (not yet saved) credentials and confirms at least one organization
is visible, using a transient client built from exactly what's about to be saved — never the
connector's already-cached client, which could be validating stale credentials. A failure at either
step is summarized ("authentication failed" / "listing organizations failed") rather than forwarding
the raw HTTP response, which could otherwise leak transport-layer detail to the UI.

### Order Status Mapping

MarkMonitor order statuses are mapped to Keyfactor statuses as follows:

| MarkMonitor order status | Keyfactor status |
|---|---|
| `DIGI_PENDING`, `DIGI_PROCESSING`, `DIGI_REISSUE_PENDING`, `DIGI_WAITING_PICKUP`, `REISSUE_PENDING`, `DIGI_NEEDS_APPROVAL`, `REISSUE_REQUEST_PENDING` | `INPROCESS` |
| `CREATED` | `EXTERNALVALIDATION` (accepted, awaiting DCV/issuance) |
| `DIGI_ISSUED` | `GENERATED` (issued) |
| `DIGI_REVOKED` | `REVOKED` |
| `DIGI_FAILED`, `DIGI_REISSUE_FAILED` | `FAILED` |
| `DIGI_CANCELED`, `DIGI_REJECTED`, `DIGI_EXPIRED`, `DIGI_NEEDS_CSR` | `CANCELLED` |
| *(null/empty or unrecognized status)* | `FAILED` |

> A freshly-submitted order (`CREATED`) is deliberately mapped to `EXTERNALVALIDATION` rather than a
> failure status — this means the order was accepted by MarkMonitor and is simply awaiting DCV or
> issuance. Once the order reaches `DIGI_ISSUED`, the next synchronization imports the certificate.

### API Endpoint Reference

The plugin calls the following MarkMonitor API endpoints. This is useful for firewall and network
connectivity planning.

| Operation | MarkMonitor API endpoint |
|---|---|
| Authenticate / obtain bearer token | `POST /auth/v1/auth/authenticate` (with `X-API-KEY` header) |
| List certificate orders (sync) | `GET /certs/v1/order` (paginated via `page`/`size`) |
| Get a single order | `GET /certs/v1/order/{orderId}` |
| Place a new order (enroll) | `POST /certs/v1/order` |
| Revoke a certificate | `PATCH /certs/v1/order/{orderId}/revoke` |
| Cancel an order | `PATCH /certs/v1/order/{orderId}/cancel` |
| Reissue a certificate | `PATCH /certs/v1/order/{orderId}/reissue` |
| List organizations | `GET /certs/v1/organization` (paginated) |
| Get an organization | `GET /certs/v1/organization/{orgId}` |
| List groups | `GET /auth/v1/group` (paginated) |
