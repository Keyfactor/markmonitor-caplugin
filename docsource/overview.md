## Overview

The MarkMonitor AnyCA Gateway REST plugin extends the certificate lifecycle capabilities of the
MarkMonitor SSL certificate service to Keyfactor Command via the Keyfactor AnyCA Gateway REST. It
implements `IAnyCAPlugin` and is loaded as a DLL extension by the AnyCA Gateway REST host process —
it is not a standalone service. See [configuration.md](configuration.md) for full installation and
configuration details, [architecture.md](architecture.md) for design notes, and
[DEVELOPMENT.md](../DEVELOPMENT.md) for local development and testing.

The plugin supports the following capabilities:

* **CA Synchronization** — Downloads all certificate orders visible to the configured MarkMonitor
  organization and imports the issued certificates (and their chains) into Keyfactor Command. Orders
  that have not yet produced a certificate are mapped to the appropriate pending/failed status
  rather than imported as certificates.
* **Certificate Enrollment** — Submits a new MarkMonitor certificate order for each of the SSL
  product types MarkMonitor exposes (see the [product IDs](configuration.md#product-ids) table). A
  process-local dedup guard prevents Command retries from creating duplicate orders.
* **Renewal / Reissue** — MarkMonitor has no dedicated "renew in place" enrollment endpoint through
  this plugin, so a `RenewOrReissue` enrollment places a **new** order and then revokes the prior
  certificate once the replacement has been created successfully.
* **Certificate Revocation** — Revokes a previously issued certificate, with a cross-organization
  ownership check that refuses to revoke an order belonging to a different organization than the one
  the CA connector is configured for.

MarkMonitor's SSL API is backed by DigiCert (the only certificate `provider` its API currently
supports), so issued certificates chain up to DigiCert roots.

## Authentication Model

Unlike some AnyCA plugins, MarkMonitor uses **two** credentials together:

* An **API key**, sent as the `X-API-KEY` request header on the authentication call.
* A **service-account username and password**, exchanged at `/auth/v1/auth/authenticate` for a
  short-lived **bearer token**. All subsequent API calls carry that bearer token.

The bearer token is cached in memory for the lifetime of the plugin's client and refreshed
automatically shortly before it expires. There is no OAuth client-credentials mode. See
[DEVELOPMENT.md](../DEVELOPMENT.md#request-authentication) for details.

## MarkMonitor CA Certificates

Before the gateway can register a CA backed by this plugin, the Keyfactor Command server (and the
AnyCA Gateway REST host) must trust the issuing CA chain. MarkMonitor's default issuing CA
(`provider`) is **DigiCert**, so download the appropriate root and intermediate CA certificates from
<https://www.digicert.com/kb/digicert-root-certificates.htm> and import them into the appropriate
Windows certificate stores on the gateway host (**Trusted Root Certification Authorities** for the
root CA and **Intermediate Certification Authorities** for any subordinates). See
[configuration.md](configuration.md#gateway-registration) for the full Gateway Registration
walkthrough.

## Troubleshooting

### Enrollment succeeds but the certificate never arrives immediately

**Symptom**

An enrollment returns successfully but the certificate is not delivered inline — Command shows the
request in a pending/external-validation state. A later synchronization picks the certificate up.

**Root cause**

This is expected. MarkMonitor SSL orders require Domain Control Validation (DCV) — and, in the
current test configuration, manual email approval — before DigiCert issues the certificate. A newly
placed order comes back with MarkMonitor status `CREATED`, which the plugin maps to Keyfactor's
`EXTERNALVALIDATION` (accepted, still pending) rather than a hard failure. Once the order reaches
`DIGI_ISSUED`, the next incremental CA sync transitions the record to `GENERATED` and the
certificate becomes available in Command. See the status mapping table in
[DEVELOPMENT.md](../DEVELOPMENT.md#order-status-mapping).

**Mitigation**

No action needed beyond completing DCV/approval on the MarkMonitor side. The certificate is imported
on the next sync cycle after issuance.

### An ECC CSR is rejected at enrollment with an "explicit curve parameters" error

**Symptom**

Enrolling with an ECC key fails immediately with an error stating the CSR uses explicit curve
parameters instead of a named curve.

**Root cause**

MarkMonitor's DigiCert-backed products silently reject an ECC CSR whose public key encodes the curve
with **explicit parameters** (the curve's prime/coefficients/base point spelled out) rather than a
**named-curve OID** (e.g. P-256/`secp256r1`). The order reaches a failed status almost immediately
with no reason surfaced anywhere in MarkMonitor's API, history, or order details. CA/Browser Forum
baseline requirements disallow explicit parameters for publicly trusted certificates. The plugin
validates this at enrollment time and rejects such a CSR with an explicit, actionable error rather
than submitting an order that will fail invisibly.

**Mitigation**

Regenerate the CSR with a named curve. `openssl req -in your.csr -noout -text` should show
`ASN1 OID: prime256v1` (named curve) rather than explicit `Prime:` / `A:` / `B:` / `Generator:`
fields.

### "An identical enrollment … was already submitted" warning

**Symptom**

A retried enrollment logs a warning that an identical enrollment was already submitted, and returns
the original result instead of placing a new order.

**Root cause**

This is by design. The plugin keeps a short-lived, process-local dedup cache keyed on
organization + product + subject + CSR. When Keyfactor Command retries an `Enroll` call (because the
first response was lost to a timeout or dropped connection, or because the first call is still in
flight), the retry awaits the original in-flight/just-completed result instead of creating a
duplicate MarkMonitor order. The cache window is five minutes and is not durable across gateway
restarts — it guards the narrow retry window only.

**Mitigation**

None needed. A genuinely new request (different CSR/subject) is unaffected. A failed attempt is not
cached, so a retry after a real failure gets a fresh attempt.

### "Refusing to revoke order … it belongs to a different organization"

**Symptom**

A revocation fails with an error stating the order belongs to a different organization than the
configured one.

**Root cause**

Before revoking, the plugin resolves the CA connector's configured `OrgId` to a GUID, fetches the
order, and compares the order's owning organization against it. MarkMonitor's own
`ignoreOrgCheck=false` default only guards against revoking an order belonging to a different
reseller account entirely — it has no notion of the specific sub-organization this connector is
scoped to. This matters most for `RenewOrReissue`, where the order ID being revoked comes from
Command's certificate store rather than from this org's own enrollment.

**Mitigation**

Confirm the CA connector's `OrgId` matches the organization that owns the certificate. If `OrgId` is
left blank, the ownership check is *skipped* (and logged as a warning) for `Ping`/sync, but `Revoke`
refuses to run at all without an `OrgId` configured.

### Revocation reason code is not reflected in MarkMonitor

**Symptom**

A certificate is revoked with a specific RFC 5280 reason code in Command, but MarkMonitor shows no
reason.

**Root cause**

MarkMonitor's revoke action (`PATCH /certs/v1/order/{id}/revoke`) has no field for a revocation
reason code — its request schema accepts only cert/`ignoreOrgCheck`/`additionalEmails`. A non-default
reason is logged (so it is visible that the reason was received but could not be forwarded) rather
than silently dropped, but it cannot be sent to MarkMonitor.

**Mitigation**

None available at the API level.