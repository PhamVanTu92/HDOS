#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# HDOS end-to-end smoke test
#
# Usage:
#   TOKEN=<jwt> ./smoke-test.sh
#   ./smoke-test.sh --token <jwt>
#   ./smoke-test.sh                       # will prompt for token
#
# Lấy TOKEN từ browser console (đã đăng nhập ở https://hdosfoxai.foxai.com.vn):
#   const k = Object.keys(sessionStorage).find(x => x.startsWith('oidc.user'));
#   console.log(JSON.parse(sessionStorage.getItem(k)).access_token);
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

HOST="${HOST:-https://hdosfoxai.foxai.com.vn}"
OPERATION="${OPERATION:-report.dashboard.summary}"
TENANT_ID="${TENANT_ID:-tenant-001}"
USER_ID="${USER_ID:-user-admin-hdos}"
PROVIDER_ID="${PROVIDER_ID:-excel-provider}"

# ── Colors ────────────────────────────────────────────────────────────────
RED=$'\033[31m'; GRN=$'\033[32m'; YLW=$'\033[33m'; BLU=$'\033[34m'; RST=$'\033[0m'
ok()   { echo "${GRN}✓${RST} $*"; }
warn() { echo "${YLW}!${RST} $*"; }
fail() { echo "${RED}✗${RST} $*"; exit 1; }
step() { echo; echo "${BLU}── $* ──${RST}"; }

# ── Parse args ────────────────────────────────────────────────────────────
TOKEN="${TOKEN:-}"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --token) TOKEN="$2"; shift 2 ;;
        --host)  HOST="$2"; shift 2 ;;
        --op)    OPERATION="$2"; shift 2 ;;
        *) fail "Unknown arg: $1" ;;
    esac
done
if [[ -z "$TOKEN" ]]; then
    read -rp "Paste access token (from browser sessionStorage): " TOKEN
fi
[[ -z "$TOKEN" ]] && fail "TOKEN is required"

# ── Helpers ───────────────────────────────────────────────────────────────
auth=(-H "Authorization: Bearer $TOKEN")
json=(-H "Content-Type: application/json")

# Generate UUIDv4 — works without uuidgen
gen_uuid() {
    if command -v uuidgen >/dev/null; then uuidgen | tr 'A-Z' 'a-z'
    else cat /proc/sys/kernel/random/uuid; fi
}

# Pretty JSON if jq is around
pretty() { command -v jq >/dev/null && jq . || cat; }

# ── 1. Token sanity ───────────────────────────────────────────────────────
step "1/5  Verify token has admin role"
payload=$(echo "$TOKEN" | cut -d. -f2 | tr '_-' '/+' | { read t; printf '%s' "$t"; printf '%*s' $(((4 - ${#t} % 4) % 4)) | tr ' ' '='; } | base64 -d 2>/dev/null || true)
echo "$payload" | grep -q '"admin"' \
    && ok "Token chứa role 'admin'" \
    || warn "Token KHÔNG có role admin — admin probe sẽ fail nhưng /requests vẫn chạy"

# ── 2. Probe gRPC connectivity ────────────────────────────────────────────
step "2/5  Probe gRPC tới $PROVIDER_ID"
probe=$(curl -fsS -X POST "$HOST/api/v1/admin/providers/$PROVIDER_ID/probe" "${auth[@]}" "${json[@]}" -d '{}' 2>&1 || true)
echo "$probe" | pretty
if echo "$probe" | grep -q '"welcomeReceived":true'; then
    ok "gRPC connection: TLS ✓ JWT ✓ Welcome ✓"
else
    warn "Welcome=false — excel-provider có thể chưa connect vào bridge"
fi

# ── 3. Submit request ─────────────────────────────────────────────────────
step "3/5  Submit operation '$OPERATION'"
REQ_ID=$(gen_uuid)
envelope=$(cat <<EOF
{
  "requestId": "$REQ_ID",
  "operation": "$OPERATION",
  "params": {},
  "tenantId": "$TENANT_ID",
  "userId":   "$USER_ID",
  "options": {
    "priority":   "Normal",
    "timeoutMs":  30000
  }
}
EOF
)
ack=$(curl -fsS -X POST "$HOST/api/v1/requests" "${auth[@]}" "${json[@]}" -d "$envelope")
echo "$ack" | pretty
ok "Submitted: requestId=$REQ_ID"

# ── 4. Poll result ────────────────────────────────────────────────────────
step "4/5  Poll kết quả (timeout 60s)"
start=$(date +%s)
final=""
for i in $(seq 1 30); do
    code_body=$(curl -sS -o /tmp/smoke-result.json -w "%{http_code}" "$HOST/api/v1/requests/$REQ_ID/result" "${auth[@]}")
    body=$(cat /tmp/smoke-result.json)
    status=$(echo "$body" | (command -v jq >/dev/null && jq -r .status || grep -oP '"status":"\K[^"]+'))

    elapsed=$(($(date +%s) - start))
    printf "  [%2ds] HTTP=%s status=%-12s\n" "$elapsed" "$code_body" "${status:-?}"

    case "$status" in
        Completed|Failed|Cancelled) final="$status"; break ;;
    esac
    sleep 2
done

[[ -z "$final" ]] && fail "Timeout — request không terminal sau 60s"

# ── 5. Show result + verdict ──────────────────────────────────────────────
step "5/5  Kết quả cuối"
echo "$body" | pretty

case "$final" in
    Completed) ok "END-TO-END PASS — provider trả data thành công ($((elapsed))s)" ;;
    Failed)    fail "Request failed — xem 'error' field ở response" ;;
    Cancelled) fail "Request cancelled" ;;
esac
