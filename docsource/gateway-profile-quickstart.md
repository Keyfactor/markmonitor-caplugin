# Gateway Certificate Profile Quickstart

This is a standalone walkthrough for `scripts/register-gateway-profiles.sh`, the helper that
creates/updates the AnyCA Gateway REST **certificate profiles** this plugin needs (one per
MarkMonitor product in `integration-manifest.json`). It's idempotent — safe to re-run any time the
product list changes. See [Certificate Profiles](configuration.md#certificate-profiles) for where
this fits in the overall setup flow.

## Prerequisites

- `bash`, `curl`, and `jq` on the machine running the script.
- [`just`](https://github.com/casey/just) (optional) — the repo's `justfile` wraps the script; you
  can also invoke it directly.
- The AnyCA Gateway REST instance up and reachable, and one of the three credentials below for its
  admin API.

## 1. Set the gateway host

```shell
GATEWAY_HOST=gateway.example.com
```

If your gateway hosts multiple instances, each instance is mounted at its own base path (e.g.
`/markmonitor-0` instead of the default `/AnyGatewayREST`) — check the Portal or Swagger URL for the
instance you're targeting and set it explicitly:

```shell
GATEWAY_BASE_PATH=/markmonitor-0
```

## 2. Choose an auth method

`scripts/lib/gateway-auth.sh` supports three ways to authenticate to the gateway's admin API. Set
**one** of these — the script checks them in the order below.

### Option A — browser session cookie

Fastest for a one-off manual run: log into the Portal, open dev tools → Network, copy the `Cookie`
header value from any request, and set it directly.

```shell
GATEWAY_HOST=gateway.example.com
GATEWAY_COOKIE='.AspNetCore.Cookies=CfDJ8...'
```

### Option B — pre-obtained bearer token

If you already have a token (e.g. from a prior `curl` against your identity provider), skip the
OAuth2 round-trip and use it directly.

```shell
GATEWAY_HOST=gateway.example.com
GATEWAY_TOKEN=eyJhbGciOi...
```

### Option C — OAuth2 client credentials (recommended for automation)

The script fetches a fresh token itself on every run — best for CI or scheduled use where a pasted
cookie/token would go stale.

```shell
GATEWAY_HOST=gateway.example.com
TOKEN_URL=https://idp.example.com/oauth2/token
OIDC_CLIENT_ID=markmonitor-gateway-admin
OIDC_CLIENT_SECRET=***
# Optional, defaults to keyfactor-anyca-gateway:
GATEWAY_SCOPE=keyfactor-anyca-gateway
```

## Putting it in a `.env` file

The script auto-sources a `.env` in the repo root, so for repeated local use it's easiest to drop
whichever option's variables in there instead of exporting them every session:

```shell
# .env (repo root, gitignored)
GATEWAY_HOST=gateway.example.com
TOKEN_URL=https://idp.example.com/oauth2/token
OIDC_CLIENT_ID=markmonitor-gateway-admin
OIDC_CLIENT_SECRET=***
```

## 3. Run it

Via `just` (from the repo root):

```shell
just register-gateway-profiles 1        # dry run — preview only, no gateway calls
just register-gateway-profiles          # create/update one profile per product
just register-gateway-profiles 0 1      # apply, then list the resulting profiles
```

Or invoke the script directly with the same env vars exported:

```shell
DRY_RUN=1 ./scripts/register-gateway-profiles.sh
./scripts/register-gateway-profiles.sh
CHECK=1 ./scripts/register-gateway-profiles.sh
```

Dry runs are fully offline — no token is fetched and no gateway calls are made — so `DRY_RUN=1` works
even before any auth variables are set.

## Customizing key algorithms

By default, profiles allow RSA 2048/3072/4096 and ECDSA P-256/P-384/P-521 (the curves MarkMonitor's
CSR validation accepts). Override with `KEY_ALGS_JSON` if you need a narrower or wider set:

```shell
KEY_ALGS_JSON='{"rsa": {"bit_lengths": [2048, 4096]}, "ecdsa": {"curves": ["1.2.840.10045.3.1.7"]}}' \
  just register-gateway-profiles
```

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `ERROR: required env var 'GATEWAY_HOST' is not set` | `GATEWAY_HOST` (or the auth-method variables for the option you picked) isn't exported and isn't in `.env`. |
| `ERROR: no access_token in response` | `TOKEN_URL`/`OIDC_CLIENT_ID`/`OIDC_CLIENT_SECRET` are set but wrong, or the IdP rejected the scope — check the printed response body. |
| `ERROR: unexpected response listing certificate profiles` | `GATEWAY_BASE_PATH` likely doesn't match this gateway instance's actual mount path, or the token/cookie was rejected. |
| TLS errors from `curl` | Set `CURL_INSECURE=0` to enforce certificate verification (default is `1`, i.e. `curl -k`, for lab/self-signed gateways). |
