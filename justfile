# Ad-hoc MarkMonitor API helpers for manual cleanup/inspection during development.
#
# Requires: just, curl, jq
# Requires env vars (loaded automatically from a root .env, see TestConsole/README.md):
#   MARKMONITOR_BASE_URL, MARKMONITOR_API_TOKEN, MARKMONITOR_USERNAME, MARKMONITOR_PASSWORD
#
# Order IDs throughout are MarkMonitor's GUID resourceId (the `id` field from the API), NOT the
# short order number from confirmation emails - that number isn't exposed anywhere in this API.
# Use find-order/list-orders to go from a common name to the real GUID first.

set dotenv-load := true

# List available commands
default:
    @just --list

# Fetch a fresh bearer token (used internally by the other recipes)
[private]
_auth:
    #!/usr/bin/env bash
    set -euo pipefail
    curl -s -X POST "$MARKMONITOR_BASE_URL/auth/v1/auth/authenticate" \
      -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Content-Type: application/json" \
      -d "{\"username\":\"$MARKMONITOR_USERNAME\",\"password\":\"$MARKMONITOR_PASSWORD\"}" \
      | jq -r .token

# List every organization visible to this API key
list-orgs:
    #!/usr/bin/env bash
    set -euo pipefail
    BEARER=$(just _auth)
    curl -s "$MARKMONITOR_BASE_URL/certs/v1/organization" \
      -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER" \
      | jq -r '["ORG_ID","NAME"], (.content[] | [.id, .name]) | @tsv'

# List orders for an organization GUID, optionally comma-separated statuses (e.g. DIGI_PENDING,CREATED)
list-orders org_id statuses="":
    #!/usr/bin/env bash
    set -euo pipefail
    BEARER=$(just _auth)
    URL="$MARKMONITOR_BASE_URL/certs/v1/order?organizationId={{org_id}}&size=100"
    if [ -n "{{statuses}}" ]; then URL="$URL&statuses={{statuses}}"; fi
    curl -s "$URL" -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER" \
      | jq -r '["ORDER_ID","STATUS","CREATED","COMMON_NAME"], (.content[] | [.id, .status, .dateCreated, .cert.commonName]) | @tsv'

# Sweep every organization for orders still in a pending/billable state
list-pending:
    #!/usr/bin/env bash
    set -euo pipefail
    BEARER=$(just _auth)
    PENDING_STATUSES="CREATED,DIGI_PENDING,DIGI_PROCESSING,DIGI_NEEDS_CSR,DIGI_NEEDS_APPROVAL,DIGI_WAITING_PICKUP,REISSUE_PENDING,DIGI_REISSUE_PENDING,REISSUE_REQUEST_PENDING"
    curl -s "$MARKMONITOR_BASE_URL/certs/v1/organization" -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER" \
      | jq -r '.content[] | [.id, .name] | @tsv' \
      | while IFS=$'\t' read -r org_id org_name; do
          curl -s "$MARKMONITOR_BASE_URL/certs/v1/order?organizationId=$org_id&statuses=$PENDING_STATUSES&size=100" \
            -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER" \
            | jq -r --arg org "$org_name" '.content[] | [$org, .id, .status, .dateCreated, .cert.commonName] | @tsv'
        done \
      | (echo -e "ORG\tORDER_ID\tSTATUS\tCREATED\tCOMMON_NAME"; cat) | column -t -s $'\t'

# Find orders by exact common name
find-order common_name:
    #!/usr/bin/env bash
    set -euo pipefail
    BEARER=$(just _auth)
    curl -s "$MARKMONITOR_BASE_URL/certs/v1/order?commonNames={{common_name}}" \
      -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER" \
      | jq -r '["ORDER_ID","STATUS","CREATED","COMMON_NAME"], (.content[] | [.id, .status, .dateCreated, .cert.commonName]) | @tsv'

# Get full order detail by GUID
get-order order_id:
    #!/usr/bin/env bash
    set -euo pipefail
    BEARER=$(just _auth)
    curl -s "$MARKMONITOR_BASE_URL/certs/v1/order/{{order_id}}" \
      -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER" | jq .

# Cancel an order by GUID (valid for orders that haven't issued yet)
cancel order_id:
    #!/usr/bin/env bash
    set -euo pipefail
    BEARER=$(just _auth)
    curl -s -X PATCH "$MARKMONITOR_BASE_URL/certs/v1/order/{{order_id}}/cancel" \
      -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER" -H "Content-Type: application/json" -d '{}' \
      | jq -r '.status // .'

# Revoke an order by GUID (only valid for already-issued certs)
revoke order_id:
    #!/usr/bin/env bash
    set -euo pipefail
    BEARER=$(just _auth)
    curl -s -X PATCH "$MARKMONITOR_BASE_URL/certs/v1/order/{{order_id}}/revoke" \
      -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER" -H "Content-Type: application/json" -d '{}' \
      | jq -r '.status // .'

# Find an order by common name and cancel it in one step (fails if there's more than one match)
cancel-by-name common_name:
    #!/usr/bin/env bash
    set -euo pipefail
    BEARER=$(just _auth)
    MATCHES=$(curl -s "$MARKMONITOR_BASE_URL/certs/v1/order?commonNames={{common_name}}" \
      -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER")
    COUNT=$(echo "$MATCHES" | jq -r '.content | length')
    if [ "$COUNT" -eq 0 ]; then echo "No order found for {{common_name}}"; exit 1; fi
    if [ "$COUNT" -gt 1 ]; then
      echo "More than one order matches {{common_name}} - use 'just cancel <order_id>' directly:"
      echo "$MATCHES" | jq -r '.content[] | [.id, .status, .dateCreated] | @tsv'
      exit 1
    fi
    ORDER_ID=$(echo "$MATCHES" | jq -r '.content[0].id')
    echo "Cancelling $ORDER_ID ({{common_name}})..."
    curl -s -X PATCH "$MARKMONITOR_BASE_URL/certs/v1/order/$ORDER_ID/cancel" \
      -H "X-API-KEY: $MARKMONITOR_API_TOKEN" -H "Authorization: Bearer $BEARER" -H "Content-Type: application/json" -d '{}' \
      | jq -r '.status // .'

# Preview the docs/ GitHub Pages site at http://localhost:4000/markmonitor-caplugin/ (needs Docker)
docs-preview:
    docker rm -f markmonitor-docs-preview 2>/dev/null || true
    docker run -d --name markmonitor-docs-preview \
      -v "{{justfile_directory()}}/docs:/srv/jekyll" \
      -p 4000:4000 \
      jekyll/jekyll:latest \
      bash -c "bundle install && bundle exec jekyll serve --host 0.0.0.0"
    @echo "Building... tail with 'docker logs -f markmonitor-docs-preview', then open http://localhost:4000/markmonitor-caplugin/"

# Stop the docs preview container started by `just docs-preview`
docs-preview-stop:
    docker rm -f markmonitor-docs-preview
