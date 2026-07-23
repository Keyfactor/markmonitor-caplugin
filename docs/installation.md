---
title: Installation
layout: default
nav_order: 3
---

# Installation
{: .no_toc }

Get the plugin installed and MarkMonitor registered as a certificate authority in Keyfactor Command.
{: .fs-6 .fw-300 }

1. TOC
{: toc}

---

## 1. Install the AnyCA Gateway REST

If you haven't already, install the Keyfactor AnyCA Gateway REST following the
[official Keyfactor documentation](https://software.keyfactor.com/Guides/AnyCAGatewayREST/Content/AnyCAGatewayREST/InstallIntroduction.htm).
This plugin requires **AnyCA Gateway REST v24.2.0 or later**.

## 2. Trust the DigiCert issuing CA chain

MarkMonitor's certificates are issued through DigiCert, so both the Gateway server and the Command
server need to trust DigiCert's root and intermediate certificates before you can enroll.

Download the appropriate certificates from
[DigiCert's root certificate page](https://www.digicert.com/kb/digicert-root-certificates.htm) and
import them into:

- **Trusted Root Certification Authorities** — for the root CA certificate
- **Intermediate Certification Authorities** — for any intermediate certificates

on both the Gateway server and the Command server.

## 3. Download and install the plugin

1. On the server hosting the AnyCA Gateway REST, download and unzip the latest release from the
   [Markmonitor-caplugin releases page](https://github.com/Keyfactor/markmonitor-caplugin/releases/latest).
2. Copy the unzipped directory (`net8.0` or `net10.0`, matching your Gateway's .NET version) into the
   Gateway's Extensions folder:

   ```text
   Program Files\Keyfactor\AnyCA Gateway\AnyGatewayREST\net8.0\Extensions
   Program Files\Keyfactor\AnyCA Gateway\AnyGatewayREST\net10.0\Extensions
   ```

   The folder name itself doesn't matter, as long as it's unique within `Extensions`.
3. Restart the AnyCA Gateway REST service.
4. Open the AnyCA Gateway REST portal and hover over the ⓘ icon near the top left to confirm the
   Gateway recognizes the MarkMonitor plugin.

## 4. Register MarkMonitor as a certificate authority

Follow the
[official AnyCA Gateway REST documentation](https://software.keyfactor.com/Guides/AnyCAGatewayREST/Content/AnyCAGatewayREST/AddCA-Gateway.htm)
to define a new Certificate Authority, using the **Gateway Registration** and **CA Connection** tabs.

- On **Gateway Registration**, provide the DigiCert issuing CA certificate you imported in step 2 —
  either as a local file path or from a Command certificate store (the store must already exist in
  Command).
- On **CA Connection**, enter your MarkMonitor API key, service-account username/password, base URL,
  and organization. Field-by-field details are in the
  [Configuration reference](configuration#ca-connection-fields).

## 5. Add the CA to Keyfactor Command and create templates

1. Follow the
   [official Keyfactor documentation](https://software.keyfactor.com/Guides/AnyCAGatewayREST/Content/AnyCAGatewayREST/AddCA-Keyfactor.htm)
   to add the Certificate Authority you just defined to Keyfactor Command, and import its templates.
2. Create one Command certificate template per MarkMonitor product you plan to enroll for — see the
   full list in the [Configuration reference](configuration#product-ids).
3. (Command v12.3+) For each imported template, define enrollment fields for the parameters listed in
   the [Configuration reference](configuration#template-enrollment-parameters) — all of them are
   optional.

You're ready to enroll. New requests, renewals, revocations, and the recurring sync will now flow
through MarkMonitor.
