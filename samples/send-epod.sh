#!/usr/bin/env bash
# Usage: bash samples/send-epod.sh <shipmentReference> [host=http://localhost:5080] [secret] [provider]
# Signs a sample proof-of-delivery event exactly like a provider would and posts it.
set -euo pipefail
REF="${1:?shipment reference required}"
HOST="${2:-http://localhost:5080}"
SECRET="${3:-CHANGE-ME-inbound-secret}"
PROVIDER="${4:-demo-epod}"
EVENT="evt-$(date +%s%N)"
BODY="{\"eventId\":\"$EVENT\",\"shipmentReference\":\"$REF\",\"receivedBy\":\"Test Receiver\",\"signedAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"latitude\":-29.85,\"longitude\":31.02}"
TS="$(date +%s)"
SIG="sha256=$(printf '%s' "$TS.$BODY" | openssl dgst -sha256 -hmac "$SECRET" -hex | awk '{print $NF}')"
curl -sS -X POST "$HOST/webhooks/epod/$PROVIDER" \
  -H "Content-Type: application/json" -H "X-Timestamp: $TS" -H "X-Signature: $SIG" -d "$BODY"
echo
