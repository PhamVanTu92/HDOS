# HDOS Deploy Guide — v3.0 (Phase 3–6)

## Cần deploy những gì

| Thành phần | Thay đổi |
|------------|----------|
| **DB** | V016–V023: `result_chart_type`, widget catalog, modules/tabs/widgets tables + seed |
| **request-api** | ModuleController, AdminModuleController, SchemaController (mới hoàn toàn) |
| **frontend** | Widget engine, LiveWidget, DashboardDesigner, Sidebar config-driven |

---

## 1 lệnh — Full Deploy (khuyến nghị)

```bash
# Trên server, từ thư mục /opt/hdos (hoặc nơi project được clone)
git pull origin main

chmod +x scripts/apply-migrations.sh scripts/deploy.sh
./scripts/deploy.sh
```

Script tự động làm theo thứ tự:
1. ✅ Preflight (check docker, .env, HDOS_HOST)
2. ✅ Apply V016–V023 (skip nếu đã apply)
3. ✅ `docker compose build request-api frontend`
4. ✅ `docker compose up -d --no-deps request-api frontend`
5. ✅ Smoke test health endpoints

---

## Từng bước nếu cần kiểm soát thủ công

### Bước 1: Xem trạng thái migration

```bash
./scripts/apply-migrations.sh --status
```

Output mẫu:
```
VERSION    FILE                                        STATUS
──────────────────────────────────────────────────────────────
V001       V001__create_operation_registry.sql         applied
...
V015       V015__extend_audit_action_constraint.sql    applied
V016       V016__add_result_chart_type_to_operations.sql  pending ← cần apply
V017       V017__create_widget_type_catalog.sql            pending
...
V023       V023__seed_default_widgets.sql                  pending
```

### Bước 2: Apply migrations

```bash
# Preview trước
./scripts/apply-migrations.sh --dry-run

# Apply thực
./scripts/apply-migrations.sh
```

### Bước 3: Rebuild chỉ request-api (nếu chỉ backend thay đổi)

```bash
docker compose build --no-cache request-api
docker compose up -d --no-deps request-api
docker compose logs -f request-api --tail 50
```

### Bước 4: Rebuild chỉ frontend (nếu chỉ UI thay đổi)

```bash
docker compose build --no-cache frontend
docker compose up -d --no-deps frontend
```

### Bước 5: Rebuild cả 2

```bash
./scripts/deploy.sh --build-only
# hoặc
docker compose build --parallel request-api frontend
docker compose up -d --no-deps request-api frontend
```

---

## Xử lý sự cố

### Container name không đúng

Migration script mặc định dùng `hdos-postgres-1`. Nếu khác:

```bash
POSTGRES_CONTAINER=my-postgres-container ./scripts/apply-migrations.sh
```

Tìm tên đúng:
```bash
docker ps --format "table {{.Names}}\t{{.Image}}" | grep postgres
```

### Migration bị lỗi giữa chừng

```bash
# Xem chi tiết lỗi
./scripts/apply-migrations.sh --status

# Kết nối thẳng vào postgres để debug
docker exec -it hdos-postgres-1 psql -U hdos -d hdos

# Chạy thủ công 1 file
docker exec -i hdos-postgres-1 psql -U hdos -d hdos \
  -v ON_ERROR_STOP=1 \
  < db/Migrations/V016__add_result_chart_type_to_operations.sql
```

### request-api không start

```bash
docker compose logs request-api --tail 100
# Thường do: DB chưa có table mới → phải apply migration trước
```

### Frontend 404 / blank page

```bash
docker compose logs frontend --tail 50
# Kiểm tra build args VITE_GATEWAY_URL trong .env
```

---

## Verify sau deploy

```bash
# 1. Health checks
curl http://localhost:5000/healthz/live      # request-api
curl http://localhost:5500/health            # gateway

# 2. Module API hoạt động
curl -s http://localhost:5000/api/v1/modules | jq '.[] | .label'

# 3. Widget catalog seeded
docker exec hdos-postgres-1 psql -U hdos -d hdos \
  -c "SELECT COUNT(*) FROM widget_type_catalog;"
# → phải trả về 32

# 4. Modules seeded
docker exec hdos-postgres-1 psql -U hdos -d hdos \
  -c "SELECT COUNT(*) FROM module_groups; SELECT COUNT(*) FROM modules;"
# → 5 groups, 21 modules

# 5. Widgets seeded
docker exec hdos-postgres-1 psql -U hdos -d hdos \
  -c "SELECT COUNT(*) FROM widgets;"
# → 10 widgets (M01 tabs + M02 tab)
```

---

## Rollback khẩn cấp

Nếu cần rollback nhanh về version cũ:

```bash
# Rollback container về image cũ (nếu đã tag)
docker compose stop request-api frontend
docker tag hdos-request-api:latest hdos-request-api:v3-broken
docker tag hdos-request-api:stable hdos-request-api:latest
docker compose up -d --no-deps request-api frontend

# Rollback DB (nếu migrations gây ra vấn đề)
# V016–V023 chỉ ADD column và CREATE table mới → safe to keep
# Để xóa sạch nếu cần:
docker exec -i hdos-postgres-1 psql -U hdos -d hdos << 'SQL'
  DROP TABLE IF EXISTS widgets, module_tabs, modules, module_groups, widget_type_catalog CASCADE;
  ALTER TABLE operation_registry DROP COLUMN IF EXISTS result_chart_type;
  DELETE FROM _schema_migrations WHERE version IN ('V016','V017','V018','V019','V020','V021','V022','V023');
SQL
```
