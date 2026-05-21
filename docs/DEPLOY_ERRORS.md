# Docker Deployment — Error Log

Ghi lại toàn bộ lỗi phát sinh trong quá trình triển khai Docker, nguyên nhân và cách khắc phục.
Cập nhật lần cuối: 2026-05-21

---

## ERR-01 · Redis port conflict

**Triệu chứng**

```
Error response from daemon: failed to set up container networking:
Bind for 0.0.0.0:6379 failed: port is already allocated
```

**Nguyên nhân**  
Host đã chạy một Redis instance trên port 6379 (ví dụ cài local để dev).

**Khắc phục**  
Đổi port mapping trong `docker-compose.yml`:

```yaml
# Trước
redis:
  ports: ["6379:6379"]

# Sau (dùng port 6380 trên host)
redis:
  ports: ["6380:6379"]
```

Cập nhật biến môi trường nếu cần:
```
Redis__ConnectionString=localhost:6380
```

---

## ERR-02 · DI: `IDatasourceMetadataRepository` not resolved

**Triệu chứng** — `request-api` container exit code 139

```
System.InvalidOperationException: Error while validating the service descriptor
  'ServiceType: ... IOperationHandler
   ImplementationType: ... DashboardListHandler':
  Unable to resolve service for type
  'ReportingPlatform.Metadata.Abstractions.IDatasourceMetadataRepository'
```

**Nguyên nhân**  
`Request.Api/Program.cs` thiếu lời gọi `builder.Services.AddPlatformMetadata()`.

**Khắc phục**  
Thêm vào `Program.cs` (sau khi đã đăng ký `NpgsqlDataSource`):

```csharp
// Shared/Metadata/Extensions/MetadataExtensions.cs
builder.Services.AddPlatformMetadata();
```

---

## ERR-03 · DI: `WidgetCacheService` not resolved (nhiều service thiếu cùng lúc)

**Triệu chứng** — `request-api` container exit code 139

```
System.InvalidOperationException: ...
  Unable to resolve service for type
  'ReportingPlatform.Resolver.Cache.WidgetCacheService'
```

**Nguyên nhân**  
`Program.cs` chưa đăng ký toàn bộ platform stack mà các operation handlers phụ thuộc vào:

- `AddPlatformQueryBuilder()` → `SqlKataQueryBuilder`
- `AddPlatformTransformers()` → `TransformerRegistry`, `IComputedColumnEngine`
- `AddPlatformAdapters()` → `DatasourceAdapterFactory`
- `AddPlatformResolver()` → `WidgetCacheService`, `IDashboardResolver`

Ngoài ra, 3 extension method trên chưa có fallback sang `ConnectionStrings:Postgres`
nên ném `InvalidOperationException` khi connection string không được cấu hình theo
tên riêng của chúng.

**Khắc phục**

1. Thêm fallback `ConnectionStrings:Postgres` vào:
   - `Shared/QueryBuilder/Extensions/QueryBuilderExtensions.cs`
   - `Shared/Adapters/Extensions/AdaptersExtensions.cs`
   - `Shared/Resolver/Extensions/ResolverExtensions.cs`

2. Thêm đầy đủ các lời gọi vào `Program.cs` theo đúng thứ tự:

```csharp
builder.Services.AddPlatformQueryBuilder(builder.Configuration);
builder.Services.AddPlatformTransformers();
builder.Services.AddPlatformAdapters(builder.Configuration);
builder.Services.AddPlatformResolver(builder.Configuration);
```

---

## ERR-04 · DI: `IEventSubscriptionRepository` not resolved

**Triệu chứng** — `request-api` container exit code 139

```
System.InvalidOperationException: ...
  Unable to resolve service for type
  'ReportingPlatform.Metadata.Abstractions.IEventSubscriptionRepository'
  while attempting to activate
  'ReportingPlatform.Metadata.Services.EventSubscriptionSyncService'
```

**Nguyên nhân**  
`AddPlatformMetadata()` đăng ký 3 repository nhưng bỏ sót `IEventSubscriptionRepository`.

**Khắc phục** — `Shared/Metadata/Extensions/MetadataExtensions.cs`:

```csharp
services.AddSingleton<IEventSubscriptionRepository, PostgresEventSubscriptionRepository>();
```

---

## ERR-05 · DI: `IDatasourceAdapterFactory` not resolved

**Triệu chứng** — (phát hiện cùng lúc với ERR-04) `request-api` container exit code 139

```
Unable to resolve service for type
'ReportingPlatform.Adapters.Abstractions.IDatasourceAdapterFactory'
```

**Nguyên nhân**  
`AddPlatformAdapters()` đăng ký `DatasourceAdapterFactory` (concrete) nhưng không đăng ký
interface `IDatasourceAdapterFactory` — DI container không tự map concrete → interface.

**Khắc phục** — `Shared/Adapters/Extensions/AdaptersExtensions.cs`:

```csharp
services.AddSingleton<DatasourceAdapterFactory>();
services.AddSingleton<IDatasourceAdapterFactory>(sp =>
    sp.GetRequiredService<DatasourceAdapterFactory>());
```

---

## ERR-06 · DI: `IResultReader` not resolved

**Triệu chứng** — (phát hiện cùng lúc với ERR-04) `request-api` container exit code 139

```
Unable to resolve service for type
'ReportingPlatform.Contracts.Store.IResultReader'
```

**Nguyên nhân**  
`AddPlatformCaching()` đăng ký `ResultStore` (concrete) nhưng không đăng ký `IResultReader`.

**Khắc phục** — `Shared/Caching/CachingExtensions.cs`:

```csharp
using ReportingPlatform.Contracts.Store;   // thêm using

// ...
services.AddSingleton<ResultStore>();
services.AddSingleton<IResultReader>(sp => sp.GetRequiredService<ResultStore>());
```

---

## ERR-07 · DI: `INestedRequestSubmitter` not resolved

**Triệu chứng** — (phát hiện cùng lúc với ERR-04) `request-api` container exit code 139

```
Unable to resolve service for type
'ReportingPlatform.Contracts.Operations.INestedRequestSubmitter'
```

**Nguyên nhân**  
`AddPlatformOperations()` đăng ký `RequestSubmissionService` (concrete) nhưng không đăng ký
interface `INestedRequestSubmitter`.

**Khắc phục** — `Shared/Operations/Extensions/OperationsExtensions.cs`:

```csharp
using ReportingPlatform.Contracts.Operations;   // thêm using

// ...
services.AddSingleton<RequestSubmissionService>();
services.AddSingleton<INestedRequestSubmitter>(sp =>
    sp.GetRequiredService<RequestSubmissionService>());
```

---

## ERR-08 · CORS wildcard crash (Gateway)

**Triệu chứng** — `gateway` container crash ngay khi khởi động

```
System.InvalidOperationException: The CORS protocol does not allow specifying
a wildcard (any) origin and credentials at the same time.
```

**Nguyên nhân**  
`.env` có `CORS_ORIGIN_0=*`. Gateway dùng `AllowCredentials()` cho SignalR.
ASP.NET Core ném exception khi kết hợp `WithOrigins("*")` + `AllowCredentials()`.

**Khắc phục**  
Dùng origin cụ thể trong `.env`:

```dotenv
# Sai
CORS_ORIGIN_0=*

# Đúng
CORS_ORIGIN_0=http://localhost:3000
CORS_ORIGIN_1=http://localhost:5173
# Production:
# CORS_ORIGIN_0=https://your-app.example.com
```

---

## Thứ tự đăng ký DI đúng cho `Request.Api`

Quan trọng: các extension phải được gọi đúng thứ tự vì có phụ thuộc ngầm định.

```
1. AddPlatformCaching()          → IConnectionMultiplexer, IDatabase,
                                   ResultStore, IResultReader,
                                   IdempotencyStore, ProgressRingBuffer

2. AddPlatformMetadata()         → IDashboardMetadataRepository,
                                   IDatasourceMetadataRepository,
                                   ISchemaMetadataRepository,
                                   IEventSubscriptionRepository

3. AddPlatformQueryBuilder()     → SqlKataQueryBuilder,
                                   IQueryableSourceRepository

4. AddPlatformTransformers()     → TransformerRegistry,
                                   IComputedColumnEngine

5. AddPlatformAdapters()         → DatasourceAdapterFactory,
                                   IDatasourceAdapterFactory

6. AddPlatformResolver()         → WidgetCacheService,
                                   IDashboardResolver

7. AddPlatformProviders*()       → IProviderRegistry,
                                   IOperationRegistry

8. AddSigningKeyService()        → ISigningKeyService

9. AddPlatformOperations()       → 17 IOperationHandler,
                                   RequestSubmissionService,
                                   INestedRequestSubmitter,
                                   OperationDispatcher
```

---

## Checklist debug khi container exit code 139

1. `docker logs <container> 2>&1 | grep "Unable to resolve"` — xem service nào bị thiếu
2. Tìm interface đó trong codebase: `grep -r "IFooBar" Shared/ --include="*.cs" -l`
3. Tìm implementation: `grep -r "class.*IFooBar" Shared/ --include="*.cs"`
4. Tìm extension method đăng ký nó, kiểm tra có đăng ký interface hay chỉ concrete
5. Nếu chỉ đăng ký concrete → thêm `services.AddSingleton<IFoo>(sp => sp.GetRequiredService<Foo>())`
6. Nếu extension method chưa được gọi → thêm vào `Program.cs`
7. Rebuild: `docker compose build <service> && docker compose up -d --no-deps <service>`
