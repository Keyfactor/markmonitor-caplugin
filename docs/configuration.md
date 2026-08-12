---
title: Configuration reference
layout: default
nav_order: 4
---

# Configuration reference
{: .no_toc }

Every connector field, enrollment parameter, and product ID this plugin exposes.
{: .fs-6 .fw-300 }

1. TOC
{: toc}

---

## CA Connection fields

These fields appear on the **CA Connection** tab when you register MarkMonitor as a certificate
authority, in both the AnyCA Gateway REST portal and the Keyfactor Command Management Portal. Every
field except `Enabled` must be filled in before the connector can be saved and enabled.

<img src="{{ site.baseurl }}/assets/images/gateway_ca_configuration.png" alt="MarkMonitor CA Connection configuration screen" style="max-width:100%;">

| Field | Required | Masked | Default | What it's for |
|---|---|---|---|---|
| `ApiKey` | Yes | Yes | — | Your MarkMonitor API key. |
| `Username` | Yes | No | — | Username for your MarkMonitor service account. |
| `Password` | Yes | Yes | — | Password for your MarkMonitor service account. |
| `BaseUrl` | Yes | No | `https://api.markmonitor.com` | The MarkMonitor API address. Must start with `https://`. |
| `OrgId` | Yes | No | — | Your MarkMonitor organization — either its name (e.g. `MarkMonitor`) or its ID in GUID format. Used both to scope enrollment and to confirm ownership before a revoke. |
| `Enabled` | No | No | `true` | Turns the connector on or off. Useful for saving a connector before all configuration details are ready. |
| `TimeoutSeconds` | No | No | `120` | How long, in seconds, to wait on a single MarkMonitor API call before giving up. |
| `PageSize` | No | No | `100` | How many certificate orders to request per page during synchronization (1-500). |
| `ForceCompleteSync` | No | No | `false` | When `true`, re-imports every order on every sync instead of skipping ones that haven't changed. |
| `PickupRetries` | No | No | `5` | How many times enrollment polls a freshly-created order for issuance before giving up and returning it pending. `0` turns this off. |
| `PickupDelaySeconds` | No | No | `10` | How long to wait between issuance pickup polls. |

Your API key and password are encrypted in Command's gateway configuration, masked in the UI, and
never written to logs.

## Gateway Registration

Before you can enroll, Command and the Gateway both need to trust MarkMonitor's issuing CA chain
(DigiCert). See [Installation](installation#2-trust-the-digicert-issuing-ca-chain) for the download
link, then register it here using either method:

**A local file path:**

<img src="{{ site.baseurl }}/assets/images/gateway_registration_local_file.png" alt="Gateway Registration using a local file path" style="max-width:100%;">

**A Keyfactor Command certificate store** (the store must already exist in Command):

<img src="{{ site.baseurl }}/assets/images/gateway_registration_store.png" alt="Gateway Registration using a Command certificate store" style="max-width:100%;">

## Certificate templates

Create one Command certificate template per MarkMonitor product you plan to enroll for (see
[Product IDs](#product-ids) below). Here's an example set up for a GeoTrust DV SSL certificate:

<img src="{{ site.baseurl }}/assets/images/gateway_template.png" alt="Example certificate template for a GeoTrust DV SSL product" style="max-width:100%;">

## Template enrollment parameters

Once a template is imported into Command, you can add these optional parameters to it. All of them
are read case-insensitively at enrollment time, so casing doesn't matter.

<img src="{{ site.baseurl }}/assets/images/template_enrollment_params.png" alt="Template enrollment parameters" style="max-width:100%;">

| Parameter | Type | Default | What it does |
|---|---|---|---|
| `AdditionalEmails` | String | *(none)* | One or more email addresses (comma- or space-separated) that MarkMonitor should send the issued certificate to. |
| `MarkmonitorGroup` | String | *(none)* | A MarkMonitor group to associate the order with — by name or GUID. Left blank or unresolved, it's simply omitted. |
| `MarkmonitorContact` | String | *(org default)* | The MarkMonitor contact for this order — by GUID, email, or full name. Falls back to your organization's default contact. |
| `DCVMethod` | String | `EMAIL` | How MarkMonitor should validate domain control: `EMAIL`, `DNS_CNAME_TOKEN`, `HTTP_TOKEN`, or `DNS_TXT_TOKEN`. An unrecognized value falls back to `EMAIL`. |
| `comments` | String | `Requested via Keyfactor Command` | Free-text note attached to the order. |
| `locale` | String | `en` | Locale for the order. |
| `provider` | String | `DIGICERT` | The certificate provider. `DIGICERT` is currently the only one MarkMonitor supports. |
| `RenewalWindowDays` | Number | `90` | For a Renewal/Reissue request, how many days before its expiration the certificate being replaced must be within before it's revoked. Outside that window, it's left alone and the request is treated like a plain new certificate. |

For any DCV method other than `EMAIL`, the token/record still needs to be published outside of
Command — this plugin passes your chosen method to MarkMonitor but doesn't automate DNS or HTTP token
publication.

## Product IDs

Each row is a MarkMonitor certificate product you can create a Command template for.

| Product ID | Product family |
|---|---|
| `SslOvBasic` | OV SSL (basic) |
| `SslEvBasic` | EV SSL (basic) |
| `SslDvGeotrust` | GeoTrust DV SSL |
| `SslDvThawte` | Thawte DV SSL |
| `SslOvThawteWebserver` | Thawte OV Web Server SSL |
| `SslEvThawteWebserver` | Thawte EV Web Server SSL |
| `SslOvGeotrustTruebizid` | GeoTrust OV True BusinessID SSL |
| `SslEvGeotrustTruebizid` | GeoTrust EV True BusinessID SSL |
| `SslOvSecuresite` | DigiCert OV Secure Site SSL |
| `SslEvSecuresite` | DigiCert EV Secure Site SSL |
| `SslOvSecuresitePro` | DigiCert OV Secure Site Pro SSL |
| `SslEvSecuresitePro` | DigiCert EV Secure Site Pro SSL |

Which of these your account can actually order — and any product-specific requirements — depends on
your MarkMonitor entitlements. Check with your MarkMonitor administrator if you're not sure.
