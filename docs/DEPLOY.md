# DEPLOY.md — Hướng dẫn triển khai hệ thống
> Cập nhật: 2026-05-20 | Môi trường: Docker Compose (dev/staging) + production notes

---

## Mục lục

1. [Yêu cầu hệ thống](#1-yêu-cầu-hệ-thống)
2. [Cấu trúc Dockerfiles](#2-cấu-trúc-dockerfiles)
3. [Biến môi trường bắt buộc](#3-biến-môi-trường-bắt-buộc)
4. [Triển khai với Docker Compose (dev/staging)](#4-triển-khai-với-docker-compose-devstaging)
5. [Kiểm tra stack sau khi deploy](#5-kiểm-tra-stack-sau-khi-deploy)
6. [Chạy test tích hợp sau deploy](#6-chạy-test-tích-hợp-sau-deploy)
7. [Triển khai provider mẫu](#7-triển-khai-provider-mẫu)
8. [Troubleshooting thường gặp](#8-troubleshooting-thường-gặp)
9. [Checklist trước khi lên production](#9-checklist-trước-khi-lên-production)
10. [Cập nhật / Rolling deploy](#10-cập-nhật--rolling-deploy)

---

## 1. Yêu cầu hệ thống

| Thành phần | Phiên bản tối thiểu | Ghi chú |
|---|---|---|
| Docker Engine | 24.x+ | `docker --version` |
| Docker Compose | v2.20+ | `docker compose version` |
| RAM | 4 GB (dev), 8 GB (staging) | Cả 10 service + 3 infra đang chạy |
| Disk | 10 GB | Images + volumes |
| OS | Linux (Ubuntu 22.04+), macOS, Windows WSL2 | |

---

## 2. Cấu trúc Dockerfiles

Mỗi service có Dockerfile riêng trong thư mục service của nó. **Build context là root của repo** (`E:\Project\HDOS`), vì tất cả service dùng thư viện chung từ `Shared/`.

```
HDOS/
├── .dockerignore                               ← loại trừ bin/, obj/, tests/, docs/
├── Services/
│   ├── Gateway/Dockerfile                      ← port 5500
│   ├── Request.Api/Dockerfile                  ← port 5000
│   ├── Ingestion.Api/Dockerfile                ← port 5100
│   ├── Realtime.Hub/Dockerfile                 ← port 5200
│   ├── Provider.Bridge/Dockerfile              ← port 5400 (gRPC)
│   ├── Operation.Router.Worker/Dockerfile
│   ├── Response.Dispatcher.Worker/Dockerfile
│   ├── Progress.Dispatcher.Worker/Dockerfile
│   └── Event.Processor.Worker/Dockerfile
└── docker-compose.yml                          ← full stack
```

**Mỗi Dockerfile dùng multi-stage build:**
- **Stage 1 (build)**: `mcr.microsoft.com/dotnet/sdk:9.0` — copy `.csproj` → restore → publish release
- **Stage 2 (runtime)**: `mcr.microsoft.com/dotnet/aspnet:9.0` — chỉ chứa output đã publish (~80 MB/image)

---

## 3. Biến môi trường bắt buộc

Tạo file `.env` tại root repo trước khi chạy compose:

```bash
# .env — KHÔNG commit file này vào git
AUTH_AUTHORITY=https://your-oidc-provider.com
AUTH_AUDIENCE=reporting-platform
CORS_ORIGIN_0=https://your-frontend.com
CORS_ORIGIN_1=http://localhost:3000
```

| Biến | Bắt buộc | Mô tả |
|---|---|---|
| `AUTH_AUTHORITY` | ✅ Production | OIDC issuer URL. Để trống = tắt xác thực JWT (chỉ dev) |
| `AUTH_AUDIENCE` | ✅ Production | Giá trị `aud` trong JWT. Default: `reporting-platform` |
| `CORS_ORIGIN_0` | ✅ Production | Origin frontend được phép |
| `CORS_ORIGIN_1` | ⬜ Optional | Origin thứ 2 (Vite dev server, etc.) |
| `ML_FRAUD_CLIENT_ID` | ⬜ Provider | Client ID của provider .NET mẫu |
| `ML_FRAUD_CLIENT_SECRET` | ⬜ Provider | Secret của provider .NET mẫu |
| `FORECAST_CLIENT_ID` | ⬜ Provider | Client ID của provider Python mẫu |
| `FORECAST_CLIENT_SECRET` | ⬜ Provider | Secret của provider Python mẫu |

---

## 4. Triển khai với Docker Compose (dev/staging)

### 4.1 Lần đầu — Build và khởi động

```bash
# 1. Clone repo về server
git clone <repo-url> hdos && cd hdos

# 2. Tạo file .env
cp .env.example .env          # (nếu có) hoặc tạo thủ công
nano .env                     # điền AUTH_AUTHORITY, etc.

# 3. Build tất cả 9 service images
docker compose build

# Hoặc build song song (nhanh hơn trên máy nhiều core):
docker compose build --parallel

# 4. Khởi động toàn bộ stack
docker compose up -d

# 5. Xem logs khởi động
docker compose logs -f --tail=50
```

> **Lưu ý**: PostgreSQL chạy migration tự động từ `db/Migrations/*.sql`
> khi container khởi động lần đầu (mount read-only vào `/docker-entrypoint-initdb.d`).

### 4.2 Build từng service riêng lẻ

```bash
# Build chỉ 1 service (sau khi sửa code)
docker compose build gateway
docker compose build request-api

# Rebuild và restart không ảnh hưởng service khác
docker compose up -d --no-deps --build gateway
```

### 4.3 Build với tag để push lên registry

```bash
# Build với tag cụ thể
docker build \
  -f Services/Gateway/Dockerfile \
  -t your-registry.io/hdos/gateway:1.0.0 \
  .

# Push lên registry
docker push your-registry.io/hdos/gateway:1.0.0
```

---

## 5. Kiểm tra stack sau khi deploy

### 5.1 Kiểm tra trạng thái containers

```bash
docker compose ps
```

**Kết quả mong đợi** (tất cả 10 container `running` / `healthy`):

```
NAME                        STATUS          PORTS
hdos-postgres-1             healthy         0.0.0.0:5432->5432/tcp
hdos-redis-1                healthy         0.0.0.0:6379->6379/tcp
hdos-rabbitmq-1             healthy         0.0.0.0:5672->5672/tcp, 0.0.0.0:15672->15672/tcp
hdos-request-api-1          healthy         0.0.0.0:5000->5000/tcp
hdos-ingestion-api-1        running         0.0.0.0:5100->5100/tcp
hdos-realtime-hub-1         running         0.0.0.0:5200->5200/tcp
hdos-provider-bridge-1      running         0.0.0.0:5400->5400/tcp
hdos-gateway-1              healthy         0.0.0.0:5500->5500/tcp
hdos-operation-router-1     running
hdos-response-dispatcher-1  running
hdos-progress-dispatcher-1  running
hdos-event-processor-1      running
```

### 5.2 Health check từng endpoint

```bash
# Gateway (public entry point)
curl -sf http://localhost:5500/health && echo " ✓ gateway"

# Request API
curl -sf http://localhost:5000/healthz/live && echo " ✓ request-api"

# Ingestion API
curl -sf http://localhost:5100/health && echo " ✓ ingestion-api"

# Realtime Hub
curl -sf http://localhost:5200/healthz/live && echo " ✓ realtime-hub"

# Provider Bridge
curl -sf http://localhost:5400/healthz && echo " ✓ provider-bridge"
```

### 5.3 Kiểm tra database migrations đã chạy

```bash
docker compose exec postgres psql -U hdos -d hdos -c \
  "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;"
```

**Kết quả mong đợi** (8 table tương ứng V001–V008):
```
dashboard_definitions
event_subscriptions
operation_registry
provider_credentials_audit
provider_registry
queryable_sources
schema_definitions
signing_keys
```

### 5.4 Kiểm tra RabbitMQ queues

```bash
# Mở management UI
open http://localhost:15672
# user: guest / pass: guest

# Hoặc dùng CLI
docker compose exec rabbitmq rabbitmqctl list_queues
```

### 5.5 Kiểm tra Redis

```bash
docker compose exec redis redis-cli ping
# Kết quả: PONG
```

---

## 6. Chạy test tích hợp sau deploy

Sau khi stack đang chạy, un-skip các test cần Docker và chạy:

```bash
# Un-skip T7, T8, PH4, IN12, SI2 trong source code (xóa [Fact(Skip=...)])
# Sau đó build và chạy:

dotnet test tests/Providers.Tests    --filter "RequiresDocker=true"
dotnet test tests/Resolver.Tests     --filter "RequiresDocker=true"
dotnet test tests/Ingestion.Tests    --filter "RequiresDocker=true"
dotnet test tests/Adapters.Tests     --filter "RequiresDocker=true"
```

Hoặc chạy toàn bộ suite kể cả Docker tests:
```bash
dotnet test
```

---

## 7. Triển khai provider mẫu

```bash
# Khởi động cả provider .NET và Python mẫu
docker compose \
  -f docker-compose.yml \
  -f docker-compose.providers.yml \
  up -d

# Xem logs provider
docker compose logs -f dotnet-provider-sample python-provider-sample
```

Provider mẫu kết nối tới `provider-bridge:5400` (gRPC). Cần có `CLIENT_ID` và `CLIENT_SECRET` đã được đăng ký qua admin API trước.

---

## 8. Troubleshooting thường gặp

### Container service crash ngay khi start

```bash
# Xem logs chi tiết
docker compose logs request-api --tail=100

# Restart đơn lẻ
docker compose restart request-api
```

**Nguyên nhân thường gặp:**
- `AUTH_AUTHORITY` chưa điền trong `.env` → set thành chuỗi rỗng khi dev
- PostgreSQL chưa ready → service restart tự động (healthcheck)
- RabbitMQ chưa ready → tương tự, retry tự động

### Lỗi migration PostgreSQL

```bash
# Xem log init postgres
docker compose logs postgres | grep -E "ERROR|FATAL|applying"

# Reset database (XÓA TOÀN BỘ DỮ LIỆU)
docker compose down -v        # xóa volumes
docker compose up -d postgres
```

### Port đã bị chiếm

```bash
# Kiểm tra port đang dùng
netstat -tlnp | grep -E "5000|5100|5200|5400|5500|5432|6379|5672"

# Sửa port trong docker-compose.yml nếu xung đột
```

### Gateway trả về 502 Bad Gateway

```bash
# Kiểm tra backend service còn running không
docker compose ps request-api realtime-hub ingestion-api

# Kiểm tra YARP cluster config
docker compose exec gateway env | grep ReverseProxy
```

### Xem logs realtime

```bash
# Tất cả services
docker compose logs -f

# Chỉ worker logs
docker compose logs -f operation-router-worker response-dispatcher-worker \
  progress-dispatcher-worker event-processor-worker
```

---

## 9. Checklist trước khi lên production

| # | Mục | Lệnh kiểm tra |
|---|---|---|
| 1 | Tất cả images build thành công | `docker compose build` → exit 0 |
| 2 | 10 container healthy | `docker compose ps` |
| 3 | 5 health endpoints trả 200 | Xem §5.2 |
| 4 | 8 tables tồn tại trong DB | Xem §5.3 |
| 5 | `AUTH_AUTHORITY` đã set (≠ rỗng) | `grep AUTH_AUTHORITY .env` |
| 6 | CORS origin đã cấu hình đúng | `grep CORS_ORIGIN .env` |
| 7 | JWT validation hoạt động | `curl -I http://localhost:5500/api/v1/requests` → 401 |
| 8 | Rate limiting hoạt động | Gửi >100 req/phút → 429 |
| 9 | Zero CVE | `dotnet list package --vulnerable --include-transitive` |
| 10 | 240 tests passing | `dotnet test` → 240/0 |
| 11 | §12.2 scenario 2 SignalR fan-out E2E | `bash scripts/smoke-tests.sh` |

---

## 10. Cập nhật / Rolling deploy

### Cập nhật một service

```bash
# Pull code mới
git pull

# Rebuild chỉ service thay đổi
docker compose build gateway

# Restart không ảnh hưởng service khác
docker compose up -d --no-deps gateway
```

### Cập nhật toàn bộ stack

```bash
git pull
docker compose build --parallel
docker compose up -d
```

### Rollback

```bash
# Xem commit trước
git log --oneline | head -5

# Rollback về commit cụ thể
git checkout <commit-hash>
docker compose build --parallel
docker compose up -d
```

---

## Thông tin thêm

- Kiến trúc hệ thống: [`README.md`](../README.md)
- Quyết định thiết kế: [`docs/DECISIONS.md`](DECISIONS.md)
- Trạng thái ship: [`docs/SHIP_STATUS.md`](SHIP_STATUS.md)
- Tích hợp frontend: [`docs/PROTOCOL.md`](PROTOCOL.md)
- Onboarding provider: [`docs/PROVIDER_ONBOARDING.md`](PROVIDER_ONBOARDING.md)
