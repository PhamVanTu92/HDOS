#!/usr/bin/env bash
# ─── HDOS Full Deploy Script ──────────────────────────────────────────────────
# Deploy toàn bộ HDOS stack: migrate DB → build images → restart services.
#
# Usage:
#   ./scripts/deploy.sh                  # full deploy (migrate + build all + restart)
#   ./scripts/deploy.sh --migrate-only   # chỉ chạy migration, không rebuild
#   ./scripts/deploy.sh --build-only     # chỉ rebuild + restart, không migrate
#   ./scripts/deploy.sh --services "request-api frontend"  # chỉ rebuild service chỉ định
#   ./scripts/deploy.sh --dry-run        # preview không làm gì
#
# Requirements:
#   • docker, docker compose v2
#   • .env file đã có HDOS_HOST
#   • Chạy từ thư mục gốc của project (E:\Project\HDOS hoặc tương đương trên server)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${PROJECT_DIR}"

# ── Config ────────────────────────────────────────────────────────────────────

# Services cần rebuild khi code thay đổi (Phase 3–5 chạm vào request-api và frontend)
DEFAULT_SERVICES=("request-api" "frontend")

DRY_RUN=false
MIGRATE_ONLY=false
BUILD_ONLY=false
SERVICES=("${DEFAULT_SERVICES[@]}")

# ── Parse args ────────────────────────────────────────────────────────────────

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run)      DRY_RUN=true ;;
    --migrate-only) MIGRATE_ONLY=true ;;
    --build-only)   BUILD_ONLY=true ;;
    --services)     shift; IFS=' ' read -ra SERVICES <<< "$1" ;;
    --help|-h)
      sed -n '2,12p' "$0"
      exit 0
      ;;
  esac
  shift
done

# ── Colors ────────────────────────────────────────────────────────────────────

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
BLUE='\033[0;34m'; BOLD='\033[1m'; NC='\033[0m'
CYAN='\033[0;36m'

step()  { echo -e "\n${CYAN}${BOLD}[$(date +%H:%M:%S)] ▶ $*${NC}"; }
ok()    { echo -e "${GREEN}✓${NC}  $*"; }
info()  { echo -e "${BLUE}ℹ${NC}  $*"; }
warn()  { echo -e "${YELLOW}⚠${NC}  $*"; }
err()   { echo -e "${RED}✗${NC}  $*" >&2; }

run() {
  if [[ "${DRY_RUN}" == "true" ]]; then
    echo -e "${YELLOW}[DRY]${NC} $*"
  else
    eval "$@"
  fi
}

# ── Preflight checks ──────────────────────────────────────────────────────────

preflight() {
  step "Preflight checks"

  if ! command -v docker &>/dev/null; then
    err "docker không tìm thấy. Cài Docker Engine trước."
    exit 1
  fi

  if ! docker compose version &>/dev/null; then
    err "docker compose v2 không tìm thấy."
    exit 1
  fi

  if [[ ! -f ".env" ]]; then
    err ".env không tìm thấy. Copy từ .env.example và điền HDOS_HOST."
    exit 1
  fi

  # Load HDOS_HOST từ .env
  # shellcheck disable=SC2046
  export $(grep -v '^#' .env | xargs) 2>/dev/null || true

  info "HDOS_HOST : ${HDOS_HOST:-<chưa set>}"
  info "Project   : ${PROJECT_DIR}"
  info "Services  : ${SERVICES[*]}"
  [[ "${DRY_RUN}" == "true" ]] && warn "DRY RUN mode — không có thay đổi thực sự"
  ok "Preflight OK"
}

# ── Step 1: Apply DB migrations ───────────────────────────────────────────────

step_migrate() {
  step "DB Migrations (V016–V023)"

  if [[ "${DRY_RUN}" == "true" ]]; then
    run "${SCRIPT_DIR}/apply-migrations.sh --dry-run"
  else
    bash "${SCRIPT_DIR}/apply-migrations.sh"
  fi
}

# ── Step 2: Build Docker images ───────────────────────────────────────────────

step_build() {
  step "Build Docker images: ${SERVICES[*]}"

  info "Pulling base images mới nhất..."
  run docker compose pull --ignore-pull-failures "${SERVICES[@]}" 2>/dev/null || true

  info "Building images..."
  run docker compose build --no-cache --parallel "${SERVICES[@]}"
  ok "Build xong"
}

# ── Step 3: Restart services ──────────────────────────────────────────────────

step_restart() {
  step "Restart services: ${SERVICES[*]}"

  # --no-deps: chỉ restart services chỉ định, không khởi động lại dependencies
  run docker compose up -d --no-deps "${SERVICES[@]}"

  ok "Services đã được restart"

  # Wait và show health
  info "Đợi services healthy (30s)..."
  sleep 5

  for svc in "${SERVICES[@]}"; do
    local container
    container="hdos-${svc}-1"
    local status
    status=$(docker inspect --format='{{.State.Health.Status}}' "${container}" 2>/dev/null || echo "no healthcheck")
    echo "   ${svc}: ${status}"
  done
}

# ── Step 4: Smoke test ────────────────────────────────────────────────────────

step_smoke() {
  step "Smoke test"

  # Load HOST từ .env
  local host="${HDOS_HOST:-localhost}"

  # Test gateway health
  local gateway_url="http://localhost:5500/health"
  if curl -sf --max-time 5 "${gateway_url}" &>/dev/null; then
    ok "Gateway health: OK  (${gateway_url})"
  else
    warn "Gateway health: không trả lời tại ${gateway_url}"
    warn "   Kiểm tra: docker compose logs gateway --tail 50"
  fi

  # Test request-api health
  local api_url="http://localhost:5000/healthz/live"
  if curl -sf --max-time 5 "${api_url}" &>/dev/null; then
    ok "Request API health: OK  (${api_url})"
  else
    warn "Request API health: không trả lời tại ${api_url}"
    warn "   Kiểm tra: docker compose logs request-api --tail 50"
  fi

  echo ""
  info "Frontend (cần auth, mở browser):"
  echo "   https://${host}"
  info "Keycloak Admin:"
  echo "   https://${host}:8443  (admin / Admin123!)"
  info "RabbitMQ UI:"
  echo "   http://${host}:15672  (guest / guest)"
}

# ── Summary ───────────────────────────────────────────────────────────────────

print_summary() {
  echo ""
  echo -e "${GREEN}${BOLD}═══════════════════════════════════════${NC}"
  echo -e "${GREEN}${BOLD}  HDOS Deploy hoàn thành ✓${NC}"
  echo -e "${GREEN}${BOLD}═══════════════════════════════════════${NC}"
  echo ""
  echo "  Thay đổi trong deploy này:"
  echo "  • V016–V023 DB migrations (widget catalog, modules, tabs, widgets)"
  echo "  • Request.Api: ModuleController, AdminModuleController, SchemaController"
  echo "  • Frontend: Widget renderer (11 types), LiveWidget, DashboardDesigner"
  echo "  • Sidebar config-driven từ DB (GET /api/v1/modules)"
  echo ""
  echo "  Bước tiếp theo:"
  echo "  1. Mở https://\${HDOS_HOST}/admin/modules  → Tạo module test"
  echo "  2. Click 'Design Canvas' → Drag widget từ catalog"
  echo "  3. Save → Mở /m/<slug> → Widget render live data"
  echo ""
}

# ── Main ──────────────────────────────────────────────────────────────────────

main() {
  echo -e "${BOLD}"
  echo "  ██╗  ██╗██████╗  ██████╗ ███████╗"
  echo "  ██║  ██║██╔══██╗██╔═══██╗██╔════╝"
  echo "  ███████║██║  ██║██║   ██║███████╗"
  echo "  ██╔══██║██║  ██║██║   ██║╚════██║"
  echo "  ██║  ██║██████╔╝╚██████╔╝███████║"
  echo "  Deploy Script v3.0"
  echo -e "${NC}"

  preflight

  if [[ "${BUILD_ONLY}" == "false" ]]; then
    step_migrate
  fi

  if [[ "${MIGRATE_ONLY}" == "false" ]]; then
    step_build
    step_restart
    [[ "${DRY_RUN}" == "false" ]] && step_smoke
  fi

  print_summary
}

main "$@"
