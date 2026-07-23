---
title: Architecture reference
layout: default
nav_order: 5
---

# Architecture reference
{: .no_toc }

Sequence and flow diagrams for the plugin's core certificate lifecycle operations - useful if you
want to understand exactly what happens, in what order, when Keyfactor Command asks this plugin to
start up, sync, enroll, renew, revoke, or validate a connection.

1. TOC
{: toc}

---

## Gateway startup

When the AnyCA Gateway loads the plugin, it deserializes the CA connection configuration first.
Authentication is lazy - it doesn't happen until the Gateway calls `Ping()` to verify connectivity.

```mermaid
sequenceDiagram
    participant GW as AnyCA Gateway
    participant Plugin as MarkMonitor Plugin
    participant API as MarkMonitor API

    GW->>Plugin: Initialize(configProvider, certificateDataReader)
    Plugin->>Plugin: Deserialize CA connection config
    Note over Plugin: Not authenticated yet (lazy)
    GW->>Plugin: Ping()
    Plugin->>API: POST /auth/v1/auth/authenticate<br/>(API key + username/password)
    API-->>Plugin: Bearer token (+ expiry)
    Plugin->>API: GET /certs/v1/organization
    API-->>Plugin: Organizations
    Plugin-->>GW: Ping OK (auth works and at least one org exists)
```

## Synchronization

Keyfactor Command periodically syncs its certificate inventory with MarkMonitor. The plugin walks
every order, page by page, and feeds issued certificates into Command's buffer.

```mermaid
sequenceDiagram
    participant CMD as Keyfactor Command
    participant Plugin as MarkMonitor Plugin
    participant API as MarkMonitor API

    CMD->>Plugin: Synchronize(buffer, lastSync, fullSync, cancelToken)
    Plugin->>API: Authenticate

    loop Retrieve one page at a time
        Plugin->>API: GET /certs/v1/order?page=N&size=100
        API-->>Plugin: Page of order records

        loop For each order on the page
            alt Order has no certificate yet
                Plugin->>Plugin: Skip for this sync
            else Order has a certificate
                Plugin->>Plugin: Map MarkMonitor status to Keyfactor status
                Plugin->>Plugin: Assemble end-entity + intermediate + root chain
                Plugin->>CMD: Add certificate to buffer
            end
        end
    end

    Plugin-->>CMD: Synchronization complete
```

## Certificate enrollment

When someone requests a certificate through Keyfactor Command, the plugin resolves the
organization/contact/group, validates the CSR, and places a MarkMonitor order. Because domain
control validation is required before issuance, an accepted order typically comes back pending.

```mermaid
sequenceDiagram
    participant CMD as Keyfactor Command
    participant Plugin as MarkMonitor Plugin
    participant API as MarkMonitor API

    CMD->>Plugin: Enroll(csr, subject, san, productInfo, format, enrollmentType)
    Plugin->>Plugin: Check for an identical enrollment already in flight

    alt Duplicate request
        Plugin-->>CMD: Await & return the original result (no duplicate order)
    else New request
        Plugin->>API: Resolve organization, contact, and group
        Plugin->>Plugin: Parse and validate the CSR
        Plugin->>API: POST /certs/v1/order (product, org, contact, DCV method, CSR)
        API-->>Plugin: Order created - order ID + status

        alt Renewal / reissue
            Plugin->>API: Revoke the prior certificate
        end

        Plugin-->>CMD: Enrollment result (order ID, mapped status)
    end
```

### Renewal / reissue

A renewal or reissue always places a brand-new order first, then revokes the certificate it's
replacing - it never touches MarkMonitor's own reissue endpoint. If the prior certificate can't be
identified, the new certificate is still issued; only the revoke step is skipped.

```mermaid
flowchart TD
    A([Renewal / reissue enrollment]) --> B[Place a new MarkMonitor order]
    B --> C{"Prior certificate<br/>identified?"}
    C -- No --> D([Treat as a new issuance - done])
    C -- Yes --> E["Resolve the prior order"]
    E --> F{"Resolved and<br/>organization configured?"}
    F -- No --> G([Log a warning - prior cert not revoked])
    F -- Yes --> H["Revoke the prior order"]
    H --> I([New cert delivered, prior revoked])
    D --> I
    G --> I
```

## Revocation

Before revoking a certificate, the plugin verifies the order actually belongs to the organization
configured on the connector - it refuses to revoke an order from a different organization.

```mermaid
sequenceDiagram
    participant CMD as Keyfactor Command
    participant Plugin as MarkMonitor Plugin
    participant API as MarkMonitor API

    CMD->>Plugin: Revoke(orderId, serialNumber, revocationReason)
    Plugin->>API: Resolve the configured organization
    Plugin->>API: GET /certs/v1/order/{orderId}

    alt Order belongs to a different organization
        Plugin-->>CMD: Error - refusing to revoke
    else Order belongs to the configured organization
        Plugin->>API: PATCH /certs/v1/order/{orderId}/revoke
        API-->>Plugin: Revocation confirmed
        Plugin-->>CMD: Revoked
    end
```

## Connector validation

When an administrator saves or edits the CA connector, the plugin checks the supplied fields before
allowing it to be saved in an enabled state. This is a field-level check only - it doesn't make a
live API call; `Ping` is the live connectivity check.

```mermaid
flowchart TD
    A([Save connector configuration]) --> B{"API key, username,<br/>password all present?"}
    B -- Missing --> E([Validation error shown to the administrator])
    B -- Present --> C{"Base URL uses https?"}
    C -- No --> E
    C -- Yes --> D{"Organization ID present?"}
    D -- Missing --> E
    D -- Present --> F([Connector saved])
```

---

Looking for the full endpoint list, status mapping table, or implementation-level detail behind
these diagrams? See
[`DEVELOPMENT.md`](https://github.com/Keyfactor/markmonitor-caplugin/blob/main/DEVELOPMENT.md) in
the repository.
