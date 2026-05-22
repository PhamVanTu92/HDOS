#!/usr/bin/env bash
# Chẩn đoán đường đi của 1 request qua toàn bộ pipeline.
# Tự lấy token qua password grant (cần Direct Access Grants bật cho hdos-web).
#   ./diag-flow.sh                      # auto-fetch token
#   TOKEN='<jwt>' ./diag-flow.sh        # dùng token có sẵn
set -uo pipefail

HOST="${HOST:-https://hdosfoxai.foxai.com.vn}"
USERNAME="${USERNAME:-admin}"
PASSWORD="${PASSWORD:-Admin123!}"
RID=$(cat /proc/sys/kernel/random/uuid)

# ── Auto-fetch token nếu chưa có ──────────────────────────────────────────
TOKEN="${TOKEN:-}"
if [[ -z "$TOKEN" ]]; then
    echo "── Lấy token cho user '$USERNAME' qua password grant ──"
    TOKEN=$(curl -s -X POST "$HOST/realms/hdos/protocol/openid-connect/token" \
        -d "grant_type=password" \
        -d "client_id=hdos-web" \
        -d "username=$USERNAME" \
        -d "password=$PASSWORD" \
        -d "scope=openid" \
        | grep -o '"access_token":"[^"]*"' | cut -d'"' -f4)
    if [[ -z "$TOKEN" ]]; then
        echo "✗ Không lấy được token. Direct Access Grants có thể chưa bật cho hdos-web."
        echo "  Chạy lệnh kcadm bật directAccessGrantsEnabled (xem hướng dẫn)."
        exit 1
    fi
    echo "✓ Token mới (sống 15 phút)"
fi

echo "════════ STATE BEFORE SUBMIT ════════"
echo "── q.provider consumers (excel-provider session sống?) ──"
sudo docker compose exec -T rabbitmq rabbitmqctl list_queues name messages consumers 2>/dev/null | grep -E "q.provider|op-request"

echo
echo "════════ SUBMIT requestId=$RID ════════"
SUBMIT_CODE=$(curl -s -o /tmp/submit-body.txt -w "%{http_code}" -X POST "$HOST/api/v1/requests" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"requestId\":\"$RID\",\"operation\":\"report.dashboard.summary\",\"params\":{},\"tenantId\":\"tenant-001\",\"userId\":\"user-admin-hdos\",\"options\":{\"priority\":\"Normal\",\"timeoutMs\":30000}}")
echo "HTTP $SUBMIT_CODE: $(cat /tmp/submit-body.txt)"
if [[ "$SUBMIT_CODE" == "401" ]]; then
    echo "✗ 401 Unauthorized — token hết hạn. Lấy token mới (bỏ biến TOKEN để auto-fetch)."
    exit 1
fi

echo
echo "════════ +2s: QUEUE DEPTH (message kẹt ở đâu?) ════════"
sleep 2
sudo docker compose exec -T rabbitmq rabbitmqctl list_queues name messages messages_unacknowledged consumers 2>/dev/null | grep -E "q.provider|op-request"

echo
echo "════════ request-api: publish log ════════"
sudo docker compose logs request-api --since=15s 2>/dev/null | grep -iE "$RID|Queued"

echo
echo "════════ provider-bridge: forward log ════════"
sudo docker compose logs provider-bridge --since=15s 2>/dev/null | grep -iE "$RID|Forward|terminal|Error|Connect"

echo
echo "════════ excel-provider: receive log ════════"
sudo docker logs excel-provider-excel-provider-1 --since=15s 2>/dev/null | tail -30

echo
echo "════════ +5s: kết quả cuối ════════"
sleep 5
curl -s "$HOST/api/v1/requests/$RID/result" -H "Authorization: Bearer $TOKEN"
echo
