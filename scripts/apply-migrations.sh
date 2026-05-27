#!/usr/bin/env bash
# ─── HDOS Migration Runner ────────────────────────────────────────────────────
# Áp dụng các migration SQL mới vào postgres container đang chạy.
# Có tracking table để bỏ qua migration đã apply (idempotent).
#
# Usage:
#   ./scripts/apply-migrations.sh                  # apply tất cả migration chưa chạy
#   ./scripts/apply-migrations.sh --dry-run        # chỉ show, không apply
#   ./scripts/apply-migrations.sh --status         # show trạng thái tất cả migrations
#
# Requirements: docker running, postgres container healthy
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

# ── Config ────────────────────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
MIGRATIONS_DIR="${PROJECT_DIR}/db/Migrations"

# Docker container name (docker compose v2 format: <project>-<service>-1)
# Project dir name = hdos → container = hdos-postgres-1
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-hdos-postgres-1}"
DB_USER="${DB_USER:-hdos}"
DB_NAME="${DB_NAME:-hdos}"

DRY_RUN=false
STATUS_ONLY=false

# ── Parse args ────────────────────────────────────────────────────────────────

for arg in "$@"; do
  case "$arg" in
    --dry-run)    DRY_RUN=true ;;
    --status)     STATUS_ONLY=true ;;
    --help|-h)
      sed -n '2,12p' "$0"
      exit 0
      ;;
  esac
done

# ── Colors ────────────────────────────────────────────────────────────────────

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
BLUE='\033[0;34m'; BOLD='\033[1m'; NC='\033[0m'

info()  { echo -e "${BLUE}ℹ${NC}  $*"; }
ok()    { echo -e "${GREEN}✓${NC}  $*"; }
warn()  { echo -e "${YELLOW}⚠${NC}  $*"; }
err()   { echo -e "${RED}✗${NC}  $*" >&2; }
bold()  { echo -e "${BOLD}$*${NC}"; }

# ── Helpers ───────────────────────────────────────────────────────────────────

psql_exec() {
  docker exec "${POSTGRES_CONTAINER}" psql -U "${DB_USER}" -d "${DB_NAME}" "$@"
}

psql_pipe() {
  docker exec -i "${POSTGRES_CONTAINER}" psql -U "${DB_USER}" -d "${DB_NAME}" \
    --no-psqlrc -v ON_ERROR_STOP=1 "$@"
}

check_container() {
  if ! docker ps --format '{{.Names}}' | grep -q "^${POSTGRES_CONTAINER}$"; then
    err "Container '${POSTGRES_CONTAINER}' không tìm thấy hoặc không chạy."
    echo "   Kiểm tra: docker ps"
    echo "   Thử set: POSTGRES_CONTAINER=<tên container> ./scripts/apply-migrations.sh"
    exit 1
  fi
}

ensure_tracking_table() {
  psql_exec -c "
    CREATE TABLE IF NOT EXISTS _schema_migrations (
      version     VARCHAR(20)  NOT NULL PRIMARY KEY,
      filename    VARCHAR(200) NOT NULL,
      applied_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
      checksum    VARCHAR(64)
    );
    COMMENT ON TABLE _schema_migrations IS
      'HDOS migration tracking — managed by scripts/apply-migrations.sh';
  " -q 2>/dev/null
}

is_applied() {
  local version="$1"
  psql_exec -tAc \
    "SELECT COUNT(*) FROM _schema_migrations WHERE version = '${version}'" 2>/dev/null \
    | tr -d '[:space:]'
}

mark_applied() {
  local version="$1"
  local filename="$2"
  local checksum="$3"
  psql_exec -c \
    "INSERT INTO _schema_migrations (version, filename, checksum)
     VALUES ('${version}', '${filename}', '${checksum}')
     ON CONFLICT (version) DO NOTHING;" -q
}

file_checksum() {
  sha256sum "$1" | awk '{print $1}'
}

# ── Extract version from filename (e.g. V016 from V016__foo.sql) ──────────────

extract_version() {
  basename "$1" | grep -oP '^V\d+' || true
}

# ── Collect all migration files in sorted order ───────────────────────────────

collect_migrations() {
  find "${MIGRATIONS_DIR}" -maxdepth 1 -name 'V*.sql' | sort -V
}

# ── Status command ────────────────────────────────────────────────────────────

cmd_status() {
  bold "\n═══ HDOS Migration Status ═══════════════════════════════════"
  printf "%-10s %-55s %s\n" "VERSION" "FILE" "STATUS"
  echo "──────────────────────────────────────────────────────────────────"

  while IFS= read -r filepath; do
    local filename version applied
    filename="$(basename "${filepath}")"
    version="$(extract_version "${filepath}")"
    applied="$(is_applied "${version}")"

    if [[ "${applied}" == "1" ]]; then
      printf "${GREEN}%-10s${NC} %-55s ${GREEN}applied${NC}\n" "${version}" "${filename}"
    else
      printf "${YELLOW}%-10s${NC} %-55s ${YELLOW}pending${NC}\n" "${version}" "${filename}"
    fi
  done < <(collect_migrations)
  echo ""
}

# ── Apply command ─────────────────────────────────────────────────────────────

cmd_apply() {
  local applied_count=0 skipped_count=0 error_count=0

  bold "\n═══ HDOS Migration Runner ════════════════════════════════════"
  [[ "${DRY_RUN}" == "true" ]] && warn "DRY RUN — không có gì được apply thực sự\n"

  while IFS= read -r filepath; do
    local filename version checksum applied
    filename="$(basename "${filepath}")"
    version="$(extract_version "${filepath}")"

    if [[ -z "${version}" ]]; then
      warn "Bỏ qua (không parse được version): ${filename}"
      continue
    fi

    applied="$(is_applied "${version}")"

    if [[ "${applied}" == "1" ]]; then
      ok "${version}  ${filename}  (đã apply — bỏ qua)"
      (( skipped_count++ )) || true
      continue
    fi

    checksum="$(file_checksum "${filepath}")"

    if [[ "${DRY_RUN}" == "true" ]]; then
      echo -e "${YELLOW}→${NC}  ${version}  ${filename}  [DRY RUN — sẽ apply]"
      (( applied_count++ )) || true
      continue
    fi

    echo -e "${BLUE}→${NC}  Đang apply ${BOLD}${version}${NC}: ${filename}"

    # Apply trong transaction, dừng ngay khi có lỗi
    if psql_pipe < "${filepath}"; then
      mark_applied "${version}" "${filename}" "${checksum}"
      ok "${version} applied thành công"
      (( applied_count++ )) || true
    else
      err "${version} FAILED — dừng migration"
      (( error_count++ )) || true
      break
    fi

  done < <(collect_migrations)

  echo ""
  bold "── Kết quả ──────────────────────────────────────────────────"
  [[ "${applied_count}" -gt 0 ]]  && ok "Applied:  ${applied_count}"
  [[ "${skipped_count}" -gt 0 ]]  && info "Skipped:  ${skipped_count} (đã apply trước)"
  [[ "${error_count}" -gt 0 ]]    && err "Failed:   ${error_count}"
  echo ""

  [[ "${error_count}" -gt 0 ]] && exit 1
}

# ── Main ──────────────────────────────────────────────────────────────────────

main() {
  bold "\nHDOS Migration Runner"
  info "Container : ${POSTGRES_CONTAINER}"
  info "DB        : ${DB_NAME} (user: ${DB_USER})"
  info "Dir       : ${MIGRATIONS_DIR}"
  echo ""

  check_container
  ensure_tracking_table

  if [[ "${STATUS_ONLY}" == "true" ]]; then
    cmd_status
  else
    cmd_apply
    [[ "${DRY_RUN}" == "false" ]] && cmd_status
  fi
}

main "$@"
