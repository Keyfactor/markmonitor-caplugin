---
title: Overview
layout: default
nav_order: 2
---

# Overview
{: .no_toc }

1. TOC
{: toc}

---

## How the pieces fit together

Three systems are involved, and each one plays a different role:

- **Keyfactor Command** is where your team requests, tracks, and manages certificates.
- **The AnyCA Gateway REST** is Keyfactor's connector host — it's what actually talks to a
  certificate authority on Command's behalf. This plugin is a DLL that the Gateway loads.
- **MarkMonitor** is the certificate authority itself, issuing SSL certificates that ultimately chain
  up to a DigiCert root.

When someone requests a certificate in Command, the request travels through the Gateway to this
plugin, which translates it into a MarkMonitor order. The reverse happens for revocation, and a
recurring sync job keeps Command's records of MarkMonitor certificates up to date automatically.

## What you'll need

Before installing, have these ready:

- A MarkMonitor **API key** — contact MarkMonitor support to obtain one.
- A MarkMonitor **service account** (username and password) with permission to create certificate
  orders.
- The **organization** name or ID that certificates should be ordered under.
- Keyfactor Command v12.0.0 or later.
- Keyfactor AnyCA Gateway REST v25.5.0 or later.
- Network access from the Gateway server to the MarkMonitor API, and the DigiCert root/intermediate
  CA certificates trusted on both the Gateway server and the Command server. See
  [Installation](installation) for the download link and where they go.

## Two credentials, not one

Most CAs Command connects to use a single API key or an OAuth client. MarkMonitor uses **two**
credentials together, and both are required:

1. An **API key**, which identifies your MarkMonitor account.
2. A **service-account username and password**, which the plugin exchanges for a short-lived access
   token behind the scenes.

You'll enter all three — API key, username, password — when you configure the CA connector in
Command. There's nothing further to manage day to day: the plugin keeps itself authenticated and
refreshes the token automatically.

## What you can do once it's set up

- **Enroll** for any MarkMonitor SSL product your account is entitled to — OV, EV, and DV
  certificates across the GeoTrust, Thawte, and DigiCert Secure Site product families.
- **Renew or reissue** a certificate. MarkMonitor doesn't have a true "renew in place" — so a renewal
  places a new order, and once the replacement certificate is ready, the plugin retires the one it's
  replacing.
- **Revoke** a certificate you no longer need. The plugin double-checks that the certificate actually
  belongs to your configured MarkMonitor organization before revoking it.
- **Sync automatically.** On each sync, Command pulls in every certificate your organization can see
  from MarkMonitor, complete with the full certificate chain.

## Common questions

**I enrolled, but the certificate didn't come back right away — is something wrong?**

No — this is expected. MarkMonitor certificates go through Domain Control Validation (and sometimes
manual approval) before they're issued, so a brand-new request typically comes back in a pending
state rather than with a certificate attached. Command shows it as pending validation; the next
automatic sync picks up the certificate as soon as MarkMonitor issues it.

**My ECC certificate request was rejected — why?**

MarkMonitor requires ECC certificate requests to use a **named curve** (for example, P-256), not one
that spells out its curve parameters explicitly. The plugin checks for this before submitting your
request, so you'll see a clear error immediately instead of a silent failure on MarkMonitor's side.
Regenerating the CSR with a named curve resolves it.

**I retried a request that seemed stuck, and got a warning about a duplicate submission — did it go
through twice?**

No. If Command retries a request (for example, after a dropped connection), the plugin recognizes the
retry and returns the original result instead of placing a second order with MarkMonitor.

**Revocation failed with an error about the certificate belonging to a different organization.**

The plugin won't revoke a certificate unless it can confirm the certificate belongs to the MarkMonitor
organization your CA connector is configured for. Double-check that the connector's `OrgId` field
matches the organization that actually owns the certificate.

Looking for a specific connector field or enrollment parameter? See the
[Configuration reference](configuration).
