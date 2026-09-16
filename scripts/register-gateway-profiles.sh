#!/usr/bin/env bash
# Register AnyCA REST Gateway certificate profiles for this plugin.
#
# Creates (or updates) one gateway certificate profile per MarkMonitor product,
# driven by .about.carest.product_ids in integration-manifest.json. Idempotent:
# existing profiles (matched by name) are PUT-updated, new ones are POSTed.
#
# This only touches gateway certificate profiles (/config/certificateprofile).
# It does not register a CA connection or import Command templates - do that
# by hand (or with equivalent scripting) once profiles exist.
#
# Env: see scripts/lib/gateway-auth.sh for the auth/host contract.
# Optional:
#   KEY_ALGS_JSON   override the key_algs object (default: RSA + P-256/P-384/P-521 below)
#   MANIFEST        path to integration-manifest.json (default: repo root)
#   CHECK           1 = after applying, list the resulting profile names
#   DRY_RUN         1 = print intended actions, make no write calls
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
export REPO_ROOT

# shellcheck disable=SC1090
[ -f "$REPO_ROOT/.env" ] && . "$REPO_ROOT/.env"
# shellcheck source=lib/gateway-auth.sh
. "$SCRIPT_DIR/lib/gateway-auth.sh"

MANIFEST="${MANIFEST:-$REPO_ROOT/integration-manifest.json}"
DRY_RUN="${DRY_RUN:-0}"
CHECK="${CHECK:-0}"

# MarkMonitor orders are enrolled with RSA or named-curve ECC CSRs (see
# MarkMonitorClient.ValidateEccCsrUsesNamedCurve) - P-256/P-384/P-521 cover
# what TestConsole/CSRGenerator exercises.
DEFAULT_KEY_ALGS_JSON='{
  "rsa":   { "bit_lengths": [2048, 3072, 4096] },
  "ecdsa": { "curves": ["1.2.840.10045.3.1.7", "1.3.132.0.34", "1.3.132.0.35"] }
}'
KEY_ALGS_JSON="${KEY_ALGS_JSON:-$DEFAULT_KEY_ALGS_JSON}"

if ! echo "$KEY_ALGS_JSON" | jq -e . >/dev/null 2>&1; then
    echo "ERROR: KEY_ALGS_JSON is not valid JSON" >&2
    exit 1
fi

echo "== MarkMonitor gateway certificate profiles =="
echo "   gateway : $(gw_show)"
echo "   manifest: $MANIFEST"
[ "$DRY_RUN" = "1" ] && echo "   DRY_RUN : no write calls will be made"

PRODUCTS=()
while IFS= read -r _p; do
    [ -n "$_p" ] && PRODUCTS+=("$_p")
done < <(manifest_product_ids "$MANIFEST")
[ "${#PRODUCTS[@]}" -gt 0 ] || { echo "ERROR: no product_ids in manifest" >&2; exit 1; }
echo "   products: ${#PRODUCTS[@]}"

if [ "$DRY_RUN" = "1" ]; then
    # Fully offline preview: no token, no listing.
    echo "   (dry run) would upsert ${#PRODUCTS[@]} profiles with key_algs:"
    echo "$KEY_ALGS_JSON" | jq -c .
    for name in "${PRODUCTS[@]}"; do
        printf '   [DRY ] %s\n' "$name"
    done
    echo "== done (dry run): no calls made =="
    exit 0
fi

TOK="$(gateway_token)"

# Snapshot existing profiles once: name -> id.
EXISTING="$(gw_curl "$TOK" GET /config/certificateprofile)"
if ! echo "$EXISTING" | jq -e 'type == "array"' >/dev/null 2>&1; then
    echo "ERROR: unexpected response listing certificate profiles:" >&2
    printf '%s\n' "$EXISTING" >&2
    exit 1
fi

created=0 updated=0
for name in "${PRODUCTS[@]}"; do
    existing_id="$(echo "$EXISTING" | jq -r --arg n "$name" \
        '.[] | select(.name == $n) | .id' | head -n1)"

    body="$(jq -n --arg name "$name" --argjson algs "$KEY_ALGS_JSON" \
        '{name: $name, key_algs: $algs}')"

    if [ -n "$existing_id" ] && [ "$existing_id" != "null" ]; then
        body="$(echo "$body" | jq --argjson id "$existing_id" '. + {id: $id}')"
        printf '   [PUT ] %-40s (id=%s)\n' "$name" "$existing_id"
        resp="$(gw_curl "$TOK" PUT /config/certificateprofile "$body")"
        echo "$resp" | jq -e 'has("error") or has("Message")' >/dev/null 2>&1 \
            && { echo "       ! update failed: $resp" >&2; }
        updated=$((updated + 1))
    else
        printf '   [POST] %-40s (new)\n' "$name"
        resp="$(gw_curl "$TOK" POST /config/certificateprofile "$body")"
        echo "$resp" | jq -e 'has("error") or has("Message")' >/dev/null 2>&1 \
            && { echo "       ! create failed: $resp" >&2; }
        created=$((created + 1))
    fi
done

echo "== done: $created created, $updated updated =="

if [ "$CHECK" = "1" ]; then
    echo "== CHECK: profiles now on the gateway =="
    gw_curl "$TOK" GET /config/certificateprofile | jq -r '.[].name' | sort
fi
