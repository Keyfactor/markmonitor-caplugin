---
title: Home
layout: default
nav_order: 1
description: MarkMonitor AnyCA Gateway REST plugin for Keyfactor Command.
permalink: /
---

# MarkMonitor AnyCA Gateway REST plugin
{: .fs-9 }

Issue, renew, revoke, and synchronize MarkMonitor SSL certificates directly from Keyfactor Command.
{: .fs-6 .fw-300 }

[Get started](installation){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[See configuration fields](configuration){: .btn .fs-5 .mb-4 .mb-md-0 }

---

> **Pilot integration.** This plugin is a Keyfactor-supported pilot integration. If you run into an
> issue, open a ticket through the [Keyfactor Support Portal](https://support.keyfactor.com) or file
> it on [GitHub Issues](https://github.com/Keyfactor/markmonitor-caplugin/issues).

## What it does

Keyfactor Command manages certificate lifecycles centrally across every CA an organization uses. This
plugin plugs MarkMonitor's SSL certificate service into that picture, through Keyfactor's AnyCA
Gateway REST framework — so certificates from MarkMonitor show up, get renewed, and get revoked
alongside every other CA Command already manages.

<div class="code-example" markdown="1">
- **Bring MarkMonitor certificates into Command's inventory.** A sync job pulls in every certificate
  order your MarkMonitor organization can see, chain included.
- **Request new certificates without leaving Command.** Enroll for any of MarkMonitor's SSL product
  types — OV, EV, and DV, across GeoTrust, Thawte, and DigiCert Secure Site — from a Command
  certificate template.
- **Renew and reissue.** A renewal places a fresh MarkMonitor order and retires the certificate it
  replaces once the new one is ready.
- **Revoke on demand.** Revoke a MarkMonitor-issued certificate from Command, with a safety check that
  refuses to touch a certificate belonging to a different MarkMonitor organization.
</div>

MarkMonitor's SSL certificates are issued by DigiCert, so everything you get through this plugin
chains up to a DigiCert root.

## Where to go next

| I want to... | Go to |
|---|---|
| Install the plugin and register MarkMonitor as a CA in Command | [Installation](installation) |
| Understand what MarkMonitor and Keyfactor Command each require before I start | [Overview](overview) |
| Look up a connector field, enrollment parameter, or product ID | [Configuration reference](configuration) |
| See what changed in the latest release | [Changelog](changelog) |

## Support

This plugin is supported by Keyfactor for Keyfactor customers. Keyfactor customers with a support
issue should open a ticket through the
[Keyfactor Support Portal](https://support.keyfactor.com). To report a bug or suggest an enhancement,
use [GitHub Issues](https://github.com/Keyfactor/markmonitor-caplugin/issues) or
[Pull Requests](https://github.com/Keyfactor/markmonitor-caplugin/pulls).
