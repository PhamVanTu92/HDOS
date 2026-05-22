#!/usr/bin/env bash
# Chẩn đoán đường đi của 1 request qua toàn bộ pipeline.
# Usage: TOKEN='<jwt>' ./diag-flow.sh
set -uo pipefail

HOST="${HOST:-https://hdosfoxai.foxai.com.vn}"
TOKEN="${TOKEN:?TOKEN required}"
RID=$(cat /proc/sys/kernel/random/uuid)

echo "════════ STATE BEFORE SUBMIT ════════"
echo "── q.provider consumers (excel-provider session sống?) ──"
sudo docker compose exec -T rabbitmq rabbitmqctl list_queues name messages consumers 2>/dev/null | grep -E "q.provider|op-request"

echo
echo "════════ SUBMIT requestId=$RID ════════"
curl -s -X POST "$HOST/api/v1/requests" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"requestId\":\"$RID\",\"operation\":\"report.dashboard.summary\",\"params\":{},\"tenantId\":\"tenant-001\",\"userId\":\"user-admin-hdos\",\"options\":{\"priority\":\"Normal\",\"timeoutMs\":30000}}"
echo

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
