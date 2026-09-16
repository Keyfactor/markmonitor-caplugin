#!/usr/bin/env bash
# Shared OAuth2 + REST helpers for talking to the AnyCA REST Gateway's admin API
# (config/certificateprofile, etc.) - not the MarkMonitor vendor API (see the
# root justfile for that).
#
# Usage:
#   . "$(dirname "$0")/lib/gateway-auth.sh"
#   tok=$(gateway_token)
#   gw_curl "$tok" GET /config/certificateprofile
#
# Required env (export before sourcing, or set in a root .env - see justfile):
#   GATEWAY_HOST         gateway ingress host (no scheme)
# Auth - one of:
#   GATEWAY_COOKIE       a pasted browser session cookie (Portal UI auth)
#   GATEWAY_TOKEN        a pre-obtained bearer token
#   TOKEN_URL + OIDC_CLIENT_ID + OIDC_CLIENT_SECRET   OAuth2 client_credentials
# Optional env (defaults shown):
#   GATEWAY_SCHEME       https
#   GATEWAY_BASE_PATH    /AnyGatewayREST   (the gateway *instance* mount path -
#                        on a multi-instance gateway this is instance-specific,
#                        e.g. /markmonitor-0 - check the Portal/Swagger URL)
#   GATEWAY_SCOPE        keyfactor-anyca-gateway
#   CURL_INSECURE        1  (pass -k; set 0 to verify TLS)

GATEWAY_SCHEME="${GATEWAY_SCHEME:-https}"
GATEWAY_BASE_PATH="${GATEWAY_BASE_PATH:-/AnyGatewayREST}"
GATEWAY_SCOPE="${GATEWAY_SCOPE:-keyfactor-anyca-gateway}"
CURL_INSECURE="${CURL_INSECURE:-1}"

_gw_require() {
    local missing=0 v
    for v in "$@"; do
        if [ -z "${!v:-}" ]; then
            echo "ERROR: required env var '$v' is not set" >&2
            missing=1
        fi
    done
    [ "$missing" -eq 0 ] || return 1
}

# Base curl flags shared by every call (bash 3.2 compatible - global array).
GW_CURL_OPTS=(-sS)
[ "$CURL_INSECURE" = "1" ] && GW_CURL_OPTS+=(-k)

# oauth_token [scope] - fetch a client_credentials bearer token.
# Echoes the raw access_token. Exits non-zero (and prints the body) on failure.
oauth_token() {
    _gw_require TOKEN_URL OIDC_CLIENT_ID OIDC_CLIENT_SECRET || return 1
    local scope="${1:-}"
    local -a form=(
        --data-urlencode "grant_type=client_credentials"
        --data-urlencode "client_id=${OIDC_CLIENT_ID}"
        --data-urlencode "client_secret=${OIDC_CLIENT_SECRET}"
    )
    [ -n "$scope" ] && form+=(--data-urlencode "scope=${scope}")

    local resp tok
    resp=$(curl "${GW_CURL_OPTS[@]}" -X POST "$TOKEN_URL" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        "${form[@]}") || { echo "ERROR: token request failed" >&2; return 1; }
    tok=$(printf '%s' "$resp" | jq -r '.access_token // empty')
    if [ -z "$tok" ]; then
        echo "ERROR: no access_token in response:" >&2
        printf '%s\n' "$resp" >&2
        return 1
    fi
    printf '%s' "$tok"
}

# Auth resolution order:
#   1. GATEWAY_COOKIE - a pasted browser session cookie (gw_curl sends it
#      directly; this function returns empty and callers skip Authorization).
#   2. GATEWAY_TOKEN - an explicit pre-obtained bearer token.
#   3. OAuth2 client_credentials via oauth_token.
gateway_token() {
    if [ -n "${GATEWAY_COOKIE:-}" ]; then return 0; fi
    if [ -n "${GATEWAY_TOKEN:-}" ]; then printf '%s' "$GATEWAY_TOKEN"; return 0; fi
    oauth_token "$GATEWAY_SCOPE"
}

gw_base() {
    _gw_require GATEWAY_HOST || return 1
    printf '%s://%s%s' "$GATEWAY_SCHEME" "$GATEWAY_HOST" "$GATEWAY_BASE_PATH"
}

gw_show() { if [ -n "${GATEWAY_HOST:-}" ]; then gw_base; else printf '(GATEWAY_HOST unset)'; fi; }

# gw_curl <token> <method> <path> [data] - hits the gateway admin API. <path>
# is relative to GATEWAY_BASE_PATH (e.g. /config/certificateprofile). Echoes
# the response body.
gw_curl() {
    local tok="$1" method="$2" path="$3" data="${4:-}"
    local rw="APIClient"
    [ -n "${GATEWAY_COOKIE:-}" ] && rw="XMLHttpRequest"
    local -a args=("${GW_CURL_OPTS[@]}" -X "$method" "$(gw_base)$path"
        -H "x-keyfactor-requested-with: $rw"
        -H "Content-Type: application/json")
    if [ -n "${GATEWAY_COOKIE:-}" ]; then
        args+=(-H "Cookie: ${GATEWAY_COOKIE}" -H "x-requested-with: XMLHttpRequest")
    fi
    [ -n "$tok" ] && args+=(-H "Authorization: Bearer $tok")
    [ -n "$data" ] && args+=(-d "$data")
    curl "${args[@]}"
}

# manifest_product_ids [manifest-path] - emit product_ids one per line.
manifest_product_ids() {
    local manifest="${1:-$REPO_ROOT/integration-manifest.json}"
    jq -r '.about.carest.product_ids[]' "$manifest"
}
