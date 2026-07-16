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
- AnyCA Gateway REST >= v24.2.0.
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

## Mechanics

### Authentication

MarkMonitor authentication is a two-credential flow: the `ApiKey` is sent as the `X-API-KEY` header
to `POST /auth/v1/auth/authenticate` along with the service-account username/password, which returns
a bearer token used for all subsequent calls. The token is cached in memory (with a 30-second early-
expiry safety buffer) and refreshed lazily under a lock. See
[architecture.md](architecture.md#request-authentication).

### Key Types and CSR Handling

Enrollment is CSR-based. The plugin accepts the CSR as PEM or base64 DER, parses it with BouncyCastle,
derives the request algorithm (RSA or ECC) from the CSR signature/public-key OID, and re-serializes
it to normalized PEM before submitting. DSA is not supported.

> **ECC CSRs must use a named curve** (e.g. P-256/`secp256r1`), not explicit curve parameters.
> MarkMonitor silently fails an order whose ECC CSR uses explicit parameters, so the plugin rejects
> such a CSR up front with an actionable error. See the
> [troubleshooting note](overview.md#an-ecc-csr-is-rejected-at-enrollment-with-an-explicit-curve-parameters-error).

### Enrollment Decision Logic

`Enroll` always places a new MarkMonitor order (`POST /certs/v1/order`). For an
`EnrollmentType.RenewOrReissue`, after the new order is created the plugin revokes the prior
certificate, which the framework identifies via `PriorCertSN`. There is no in-place renewal or reissue
call in the enrollment path — see [architecture.md](architecture.md#renewal--reissue).

A short-lived, process-local dedup cache (keyed on organization + product + subject + CSR, 5-minute
window) prevents a Command retry from creating a duplicate order.

### Order Lifecycle and Pending Enrollment

MarkMonitor SSL orders require DCV (and, in the test configuration, manual email approval) before
DigiCert issues the certificate. A freshly-placed order returns MarkMonitor status `CREATED`, which
the plugin maps to Keyfactor `EXTERNALVALIDATION` (accepted, still pending). Once the order reaches
`DIGI_ISSUED`, the next CA sync transitions the Command record to `GENERATED` and imports the
certificate. See the full [order status mapping](architecture.md#order-status-mapping).

### Synchronization

Synchronization lists orders from `GET /certs/v1/order`, paging until `MarkMonitorPage.TotalPages` is
reached (fixed page size of 100), maps each order's status, assembles the full certificate chain, and
feeds issued certificates into Command's buffer. The current implementation always performs a full
listing — it does not yet filter by `lastSync`/`fullSync` — and skips orders that have no certificate
yet rather than aborting the page.

### Revocation and Organization Scoping

`Revoke` refuses to run unless `OrgId` is configured. It resolves `OrgId` to a GUID, fetches the
order, and revokes only if the order's owning organization matches (compared as parsed GUIDs). This
adds a sub-organization ownership check on top of MarkMonitor's account-level `ignoreOrgCheck`.
MarkMonitor's revoke schema has no reason-code field, so the Keyfactor revocation reason is logged
but not forwarded. See [architecture.md](architecture.md#revocation).

{% include 'architecture.md' %}
