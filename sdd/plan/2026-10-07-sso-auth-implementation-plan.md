# Kế hoạch triển khai SSO và phân quyền Cloud DMS

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tách Auth Service, xác thực JWT tại Kong OSS và thực thi RBAC/PBAC/ReBAC bằng Redis, kèm Google login, quota phòng ban và recycle bin.

**Architecture:** Auth và DMS là hai service .NET 8, mỗi service giữ API/Application/Domain/Infrastructure. Auth sở hữu credential/phiên; DMS sở hữu hồ sơ/quyền/nghiệp vụ; DB là nguồn bền vững và Redis là đường đọc duy nhất khi kiểm tra quyền/phiên. Cập nhật có marker chặn, version và transactional outbox; lỗi Redis đóng truy cập, không fallback DB.

**Tech Stack:** .NET 8, ASP.NET Core, MediatR 12.5.0, FluentValidation 11.11.0, EF Core/Pomelo/MySQL, StackExchange.Redis, Kong OSS 3.9.3, BCrypt, JWT RS256, OIDC Code + PKCE, xUnit, Docker Compose, Playwright cho browser test.

**Spec:** [sso-auth-spec.md](../spec/sso-auth-spec.md).

Ngày: 07/10/2026. Trạng thái: đang triển khai theo yêu cầu của người dùng; chưa đạt spec.

Tiến độ hiện tại: Task 1 đã có hợp đồng/evaluator thuần; Task 2 đã thêm schema quyền, quota, lifecycle và migration backfill; Task 3 đã thêm Redis transport/readiness fail-closed; Task 4 đã nối evaluator với snapshot Redis atomically và kiểm tra version trước side effect. Kiểm chứng hiện tại: 22 unit tests, 12 integration tests (Redis + MySQL), API build pass; còn 2 cảnh báo GoogleCredential cũ. Toàn solution chưa build được do còn 5 project AuthService được khai báo nhưng chưa có file dự án. DMS endpoints/use cases chưa được nối vào IAuthorizationService; Task 5–6 và các giai đoạn Auth, nghiệp vụ DMS, Kong/client/end-to-end còn chờ. Chưa áp dụng migration vào DB của người dùng; chưa đạt spec.

## Global Constraints

- “Mỗi service .NET 8 giữ bốn tầng: API, Application, Domain, Infrastructure.”
- “Use case là command/query và handler trong Application; interface ở Application/Common/Interfaces; entity ở Domain/Entities”.
- “JWT RS256, 5 phút, giữ trong RAM trình duyệt.”
- “Cookie HttpOnly/Secure; rotation; phiên tối đa 7 ngày.”
- “5 lần trong 30 phút thì khóa mật khẩu 30 phút từ lần sai thứ 5; Google vẫn dùng được nếu tài khoản hoạt động.”
- “Chỉ giữa người dùng cùng tenant, kể cả khác phòng ban.”
- “DB của DMS; Redis phục vụ kiểm tra quyền, không fallback DB.”
- “Chặn phạm vi đang cập nhật; chỉ báo thành công khi Redis đã áp dụng.”
- “Thuộc phòng ban sở hữu file; vào bin vẫn chiếm quota, quyền chia sẻ tạm vô hiệu.”
- Tổng quota người dùng <= quota phòng ban; tổng quota phòng ban <= quota tenant. Bin được tính vào UsedBytes.
- Tài liệu tiếng Việt; không ghi bí mật vào mã nguồn/tài liệu/log. Khoá, cookie/token, OTP và password không vào Git.
- Giới hạn ở auth, permission, tenant isolation và nghiệp vụ quota/file cần thiết; không thay OAuth kết nối storage thành Google login.
- Không thêm Redis Cluster, inheritance quyền thư mục, chia sẻ chéo tenant, dashboard hay broker riêng trong bản đầu.
- SystemAdmin/TenantAdmin không có quyền nội dung. Không gán hai vai trò này cùng User/DepartmentAdmin; DepartmentAdmin bao gồm khả năng User.
- Mục 14 của spec được dùng làm giả định triển khai cho plan: mTLS, sliding window, Redis primary, owner chia sẻ/khôi phục, pending/idempotency. Review plan là cơ hội sửa các giả định này.

## Review Focus

1. Retry request sau timeout không được tạo user/file/share lặp hoặc trừ quota hai lần — task 5, 11, 15.
2. Restart Redis có AOF chứa quyền cũ không được tự mở readiness hoặc hồi sinh phiên đã thu hồi — task 3, 6, 16.
3. Mất kết nối sau DB commit không được báo thành công hoặc gỡ marker của operation mới hơn — task 5, 8.
4. User chuyển phòng ban hoặc bị xóa không làm file đổi bin/quota hay biến quyền quản trị thành quyền nội dung — task 11, 15.
5. Hai tab refresh đồng thời, callback Google replay hoặc client giả header không được vượt ranh giới danh tính — task 9, 10, 17.

## Phạm vi, thứ tự và hiện trạng đã đọc

Một kế hoạch tích hợp vì UserId, session Redis, readiness và version quyền là hợp đồng chung; các phần không thể rollout độc lập vào hệ thống thật. Chia thành bốn giai đoạn kiểm chứng:

| Giai đoạn | Task | Đầu ra có thể kiểm chứng riêng |
|---|---|---|
| A. Nền tảng quyền | 1–6 | Policy engine và DB → Redis hoạt động với identity/phiên fixture. |
| B. Auth | 7–10 | Auth API chạy riêng với DB/Redis và Google provider giả lập. |
| C. Nghiệp vụ DMS | 11–15 | Nhân sự, quyền file, quota, bin có test use case. |
| D. Tích hợp/chuyển đổi | 16–19 | Gateway, browser client, migration và end-to-end. |

Phụ thuộc: 1 → 2 → 3 → 4 → 5 → 6; 7 dùng 1/3; 8 dùng 7/3; 9 dùng 8; 10 dùng 8/9; 11 dùng 5/8; 12 dùng 4/11; 13 dùng 2/4; 14 dùng 5/12/13; 15 dùng 5/13/14; 16 dùng 6/9/12; 17 dùng 9/10/16; 18 dùng 11–16; 19 dùng tất cả. Triển khai tuần tự trước khi cân nhắc chia task cho agent.

Mã hiện tại:
- DMS vẫn có HS256, policy claim permission và login tại hai controller.
- AcceptInvitationCommand.cs vừa có command vừa có handler, đang phát JWT.
- DeleteFileCommand.cs xóa vật lý, bỏ qua lỗi storage và hoàn quota ngay.
- UploadFileCommandHandler.cs có fallback sang user active và override danh tính.
- DbInitializer.cs cấp toàn bộ permission cho SystemAdmin và quyền nội dung cho TenantAdmin.
- AppDbContext áp dụng cả hai thư mục Configurations và Configuations; cần loại mapping trùng đúng phạm vi entity được thay đổi.
- tests/AuthService.UnitTests là thư mục chưa có project/test; repo chưa có frontend.
- Working tree có thay đổi README.md, xóa sso-auth-spec.md ở root và tài liệu chưa tracked; không stage/hoàn tác các thay đổi này trong task triển khai.

### Quy ước file và lệnh

Các prefix dưới chỉ rút gọn bảng file; nối prefix với đường dẫn con là đường dẫn chính xác từ root:

| Prefix | Đường dẫn |
|---|---|
| DA | src/CleanArchCqrs.Application |
| DD | src/CleanArchCqrs.Domain |
| DI | src/CleanArchCqrs.Infrastructure |
| API | src/CleanArchCqrs.API |
| AA | src/CloudDms.Auth.Application |
| AD | src/CloudDms.Auth.Domain |
| AI | src/CloudDms.Auth.Infrastructure |
| AAPI | src/CloudDms.Auth.API |
| UT | tests/CloudDms.UnitTests |
| AT | tests/AuthService.UnitTests |
| IT | tests/CloudDms.IntegrationTests |
| BT | tests/CloudDms.BrowserTests |

Mỗi file ghi Create phải được tạo, Modify phải tồn tại; command/handler/validator liệt kê riêng khi use case mới. Existing command chứa handler được tách chỉ khi task đó sửa hành vi.

Lệnh UT/AT: dotnet test <project-path>.csproj --filter FullyQualifiedName~<ClassName>.
Lệnh IT: dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj --filter FullyQualifiedName~<ClassName>.
Chạy từ root PowerShell. Mọi task có test: ghi case trước → chạy fail vì hành vi chưa có → code tối thiểu → chạy cùng filter pass → commit đúng file task. Lỗi compile do type chưa tạo có thể là red ban đầu; trước green phải kiểm thử assertion hành vi, không chỉ build.

Integration chạy MySQL/Redis thật trong môi trường riêng qua compose task 3; Google/storage dùng HTTP stub hoặc fault-injecting adapter. Không dùng EF InMemory để chứng minh transaction/concurrency. Unit dùng TimeProvider giả lập và stub có đếm call DB/storage.

### Hợp đồng Redis và cách tách service

Các key dùng GUID dạng N viết thường, UTC và JSON schemaVersion=1; Redis là primary riêng cho security. Key dưới là protocol chung, không phải thư viện tham chiếu chéo Application:

| Key | Chủ ghi | Nội dung |
|---|---|---|
| security:meta | recovery coordinator | generation, state=closed/ready, serverRunId, schemaVersion. |
| dms:user:{userId} | DMS projection | active, tenantId?, departmentId?, roles, permissions, version, complete=true. |
| dms:scope:{scopeKey} | DMS projection | version, operationId?, updating, complete. ScopeKey=global/tenant:{id}/department:{id}. |
| dms:file:{fileId} | DMS projection | ownerId, tenantId, owningDepartmentId, lifecycle, version, complete. |
| dms:grant:{fileId}:{userId} | DMS projection | flags đọc/sửa/xóa, expiresAt?, resourceVersion. |
| dms:index:{userId}:{action} | DMS projection | tập ID được phép và indexVersion để phân trang; thay cùng generation/version. |
| auth:identity:{userId} | Auth projection | active, ready, identityVersion; DMS kiểm tra cùng dms:user. |
| auth:session:{sessionId} | Auth projection | userId, active, blocked, absoluteExpiresAt, version, complete. |
| auth:attempt:{emailHmac} / auth:lock:{emailHmac} | Auth login | sorted timestamps trong 30 phút / thời điểm hết khóa. |
| auth:oidc:{stateHash} / auth:link:{challengeId} | Auth Google | dữ liệu giao dịch một lần, TTL 5 phút. |

Auth có ISecurityRedis/ISecurityReadiness và exception riêng trong AA, implementation riêng trong AI, cùng signature task 3 và cùng protocol. Auth không tham chiếu DA/DI hay entity DMS. AA định nghĩa SessionState/LoginAttemptState trong task 7 theo task 8–9; DMS đọc wire contract bằng adapter của mình.

DMS never write auth:session/identity; Auth never write dms:user/file/grant. Recovery DMS và Auth phối hợp qua API nội bộ, một coordinator duy nhất mở security:meta khi cả hai đã xác nhận watermark. Session expired/revoked trả 401; snapshot/session chưa có complete khi dữ liệu đang phục hồi trả 503. Missing grant trên resource snapshot đầy đủ là thiếu quyền (403), không cache miss để truy vấn DB.

Chỉ runtime Redis connection epoch/run_id được xác minh mới cho phép dùng generation; key security:meta tồn tại trong AOF không tự chứng minh ready. Publication nhiều key và read policy phải nguyên tử tại primary, script bounded; worker build staging trước CAS. Upload resource mới không được trả “sẵn sàng” trước khi Redis có resource/index snapshot.

Snapshot quyền dùng JSON UTF-8 với tên thuộc tính camelCase, GUID có thể ở dạng D nhưng key dùng dạng N viết thường; timestamp dùng Unix milliseconds UTC. Một lần đọc atomically lấy user, session, resource, grant và các scope global/tenant/department liên quan. Mọi scope phải `complete=true`; thiếu snapshot là unavailable, thiếu grant là deny.

## Task 1: Hợp đồng quyền và test harness

**Files**
- Create: UT/CloudDms.UnitTests.csproj; UT/Architecture/LayerDependencyTests.cs; UT/Authorization/PolicyContractTests.cs.
- Create: IT/CloudDms.IntegrationTests.csproj; IT/Support/DatabaseProbe.cs; IT/Support/StorageProbe.cs; IT/Support/TestIds.cs.
- Create: DA/Common/Authorization/AuthorizationModels.cs; DD/Enums/FileLifecycle.cs.
- Modify: dotnet-clean-architecture-cqrs-starter.sln.

**Interfaces**
- Produces: ActorIdentity(Guid UserId, Guid SessionId); AuthorizationTarget(string Kind, Guid? Id); AuthorizationDecision { Allow, Deny, Unavailable, Unauthenticated }.
- Produces: AuthorizationStamp(string Generation, long UserVersion, long ScopeVersion, long ResourceVersion).
- Produces: AuthorizationResult(AuthorizationDecision Decision, AuthorizationStamp? Stamp).
- Produces: UserAccessSnapshot(UserId, TenantId?, DepartmentId?, IsActive, Version, Roles, Permissions); ResourceAccessSnapshot(Id, TenantId, DepartmentId, OwnerId, Lifecycle, Version); ShareGrant(UserId, CanRead, CanWrite, CanDelete, ExpiresAt?).
- Produces: FileLifecycle { Active, Binned, Purging, Purged }. Roles/Permissions là IReadOnlySet<string>, thời gian DateTimeOffset UTC.
- TestIds tạo GUID cố định khác nhau cho TenantA/B, DepartmentA/B, Owner/Reader/Admin; DatabaseProbe đếm command chạm bảng quyền; StorageProbe đếm upload/download/delete và cấu hình lỗi.

- [x] Ghi LayerDependencyTests.DomainDoesNotReferenceOuterLayers và PolicyContractTests.ContentAndMetadataAreDifferentActions; assertions Assert.DoesNotContain("Infrastructure", domainReferences) và Assert.NotEqual(PermissionNames.FileRead, PermissionNames.FileMetadataRead).

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.NotEqual(PermissionNames.FileRead, PermissionNames.FileMetadataRead);
~~~

- [x] Tạo UT/IT xUnit .NET 8, add solution và references đúng tầng; chạy UT filter PolicyContractTests, xác nhận red trước khi tạo models.
- [x] Tạo các record/enum trên và test doubles; không tham chiếu Redis/HTTP từ Domain.
- [ ] Chạy UT và dotnet build dotnet-clean-architecture-cqrs-starter.sln; unit test pass nhưng solution build đang bị chặn bởi 5 project AuthService được khai báo nhưng chưa tồn tại. Build API, unit test và integration test project riêng đều pass.
- [ ] Commit riêng task: test: establish authorization contracts and harness.

## Task 2: Dữ liệu quyền bền vững, quota và lifecycle

**Files**
- Modify: DD/Entities/User.cs; DD/Entities/Department.cs; DD/Entities/FileEntry.cs; DD/Entities/UserStorageQuota.cs; DA/Common/Interfaces/IApplicationDbContext.cs; DI/Persistence/AppDbContext.cs.
- Create: DD/Entities/AuthorizationOperation.cs; DD/Entities/OutboxMessage.cs; DD/Entities/AuthorizationScope.cs; DD/Entities/QuotaReservation.cs.
- Modify: DI/Persistence/Configurations/UserConfiguration.cs; DI/Persistence/Configurations/DepartmentConfiguration.cs; DI/Persistence/Configurations/FileEntryConfiguration.cs; DI/Persistence/Configurations/UserStorageQuotaConfiguration.cs.
- Create: DI/Persistence/Configurations/AuthorizationOperationConfiguration.cs; DI/Persistence/Configurations/OutboxMessageConfiguration.cs; DI/Persistence/Configurations/AuthorizationScopeConfiguration.cs; DI/Persistence/Configurations/QuotaReservationConfiguration.cs.
- Create: DI/Migrations/<EF-generated>_AddAuthorizationStateAndDepartmentQuota.cs và Designer; Modify: DI/Migrations/AppDbContextModelSnapshot.cs.
- Create: IT/Support/MySqlFixture.cs; DI/Persistence/DesignTimeAppDbContextFactory.cs.
- Test: IT/Persistence/AuthorizationSchemaTests.cs.

**Interfaces**
- Produces: User.AuthorizationVersion:long, ProvisioningState; Department.QuotaBytes/UsedBytes:long; FileEntry.OwningDepartmentId:Guid?, Lifecycle:FileLifecycle, AuthorizationVersion:long, QuotaReleasedAt:DateTimeOffset?.
- Produces: UserStorageQuota.DepartmentId:Guid, unique (UserId, DepartmentId); quota cũ giữ theo phòng ban để file cũ không đổi bucket.
- Produces: operation có Id, ScopeKey, IdempotencyKey, PayloadHash, Version, Status, CommittedAt; outbox có Id, AggregateKey, Version, Payload, PublishedAt. Unique idempotency và aggregate/version.
- Produces: QuotaReservation gồm Id, IdempotencyKey, UserId, DepartmentId, TenantId, SizeBytes, StorageKey?, State; không chứa stream/password/token.

- [x] Ghi AuthorizationSchemaTests.UniqueOperationRejectsDuplicatePayload; FileOwnershipSurvivesDepartmentChange; QuotaBytesCannotBeNegative; assert unique conflict, OwningDepartmentId cũ không đổi, giá trị âm bị từ chối; kiểm thử thêm backfill file/quota cũ.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(originalDepartmentId, file.OwningDepartmentId);
~~~

- [x] MySqlFixture khởi động MySQL test bằng Docker với mật khẩu CSPRNG trong file tạm được xóa sau khi tạo container, không cần Redis. Integration test phát hiện và sửa migration legacy: index tạo trùng, thiếu bảng quota, tên bảng file số nhiều chưa được đổi; không có thư mục mapping `Configuations` trùng trong repo hiện tại.
- [x] Thêm entity/index/concurrency và DbSet; giữ `FileEntry.IsDeleted` để tương thích. File cũ có `IsDeleted=true` chuyển `Purging`, không giả định file vật lý đã bị xóa.
- [x] Chạy MySQL integration test thật; 4 pass. DesignTimeAppDbContextFactory và SQL migration script đã được kiểm tra; migration không áp dụng vào DB người dùng.
- [ ] Commit: feat: add durable authorization and quota state.

**Điều kiện dữ liệu legacy trước khi áp dụng migration:** quota người dùng và file phải gắn được với phòng ban; tổng nhu cầu quota theo phòng ban không vượt tenant. Migration dừng trước DDL nếu các kiểm tra thất bại. File legacy được gắn với phòng ban hiện tại của chủ sở hữu vì schema cũ không lưu lịch sử chuyển phòng ban; cần đối chiếu dữ liệu nghiệp vụ trước khi áp dụng nếu đã có thay đổi phòng ban. File chưa có phòng ban không được tự gán.
- [ ] Commit: feat: add durable authorization and quota state.

## Task 3: Redis transport, readiness và môi trường test

**Files**
- Create: DA/Common/Interfaces/ISecurityRedis.cs; DA/Common/Interfaces/ISecurityReadiness.cs.
- Create: DA/Common/Exceptions/SecurityUnavailableException.cs; DI/Security/RedisSecurityClient.cs; DI/Security/RedisReadiness.cs; DI/Security/RedisOptions.cs.
- Create: deploy/compose.integration.yml; deploy/redis/redis.conf; deploy/.env.example; scripts/New-TestSecrets.ps1.
- Create: IT/Support/RedisFixture.cs; IT/Security/RedisAvailabilityTests.cs. MySqlFixture giữ riêng cho migration/schema tests.
- Modify: DI/CleanArchCqrs.Infrastructure.csproj; DI/DependencyInjection/DependencyInjection.cs; .gitignore.

**Interfaces**
- Produces: ISecurityRedis.ExecuteAsync(string scriptName, IReadOnlyList<string> keys, IReadOnlyList<string> args, CancellationToken ct) → Task<string>.
- Produces: ISecurityReadiness.AssertReadyAsync(CancellationToken ct) → Task<string> generation; SetReadyAsync(string generation, string serverRunId, CancellationToken ct) → Task.
- Transport failures dùng SecurityUnavailableException ở DA/Common/Exceptions/SecurityUnavailableException.cs; không chuyển lỗi thành empty permissions.
- RedisFixture cung cấp Redis test cô lập, RestartAsync(), StopAsync(), StartAsync(); MySqlFixture hiện có riêng và không dùng dữ liệu thật.

- [x] RedisAvailabilityTests.DownFailsWithinConfiguredTimeout và RestartDoesNotTrustReadinessRestoredFromAof; assertions elapsed < 3 giây, throws SecurityUnavailableException, generation cũ không được chấp nhận.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.True(elapsed < TimeSpan.FromSeconds(3));
Assert.False(readinessIsOpen);
~~~

- [x] Tạo .env.example và script sinh secret test ngẫu nhiên vào deploy/.local (ignored); Compose định nghĩa MySQL + Redis trên internal network, không public port. Script đã parse hợp lệ nhưng không chạy được trực tiếp do PowerShell ExecutionPolicy của môi trường; chưa tạo file secret cục bộ.
- [x] Dùng Redis standalone primary, noeviction, AOF, không fallback; command timeout 1 giây, connect timeout 2 giây, circuit mở 5 giây sau 3 lỗi, backlog fail-fast.
- [x] Readiness metadata gắn run_id/generation/schema; gate kiểm tra run_id trước/sau, restart và AOF cũ readiness không được tin cậy.
- [x] docker compose config hợp lệ; toàn bộ 6 integration tests hiện pass, gồm Redis outage/restart và MySQL schema/migration regression.
- [ ] Commit: feat: add fail-closed security Redis transport.

## Task 4: Policy engine chỉ đọc Redis

**Files**
- Create: DA/Common/Interfaces/IAuthorizationService.cs; DA/Common/Interfaces/IAccessSnapshotStore.cs.
- Create: DA/Common/Authorization/AccessPolicyEvaluator.cs; DA/Common/Authorization/PermissionNames.cs; DA/Common/Authorization/AuthorizationService.cs.
- Create: DI/Security/RedisAccessSnapshotStore.cs; DI/Security/Scripts/evaluate-access.lua.
- Create: DA/Common/Exceptions/AuthorizationChangedException.cs; UT/Authorization/AccessPolicyEvaluatorTests.cs; IT/Authorization/RedisAuthorizationTests.cs.
- Modify: DI/DependencyInjection/DependencyInjection.cs; API/Middleware/GlobalExceptionHandlerMiddleware.cs; DA/Common/Authorization/AuthorizationModels.cs.

**Interfaces**
- Consumes: task 1 models, task 3 transport/readiness và protocol Redis ở trên.
- Produces: IAuthorizationService.CheckAsync(ActorIdentity actor, string action, AuthorizationTarget target, CancellationToken ct) → Task<AuthorizationResult>.
- Produces: IAuthorizationService.AssertStillValidAsync(ActorIdentity actor, AuthorizationStamp stamp, CancellationToken ct) → Task.
- Produces: IAccessSnapshotStore.ReadAsync(ActorIdentity actor, AuthorizationTarget target, CancellationToken ct) → Task<AccessEvaluationInput>.
- AccessEvaluationInput chứa user, session, resource?, grants, mọi scope liên quan và server time; stamp giữ session/resource/target/action cùng scope versions để kiểm tra lại trước side effect.
- AccessPolicyEvaluator.Evaluate(AccessEvaluationInput input, string action) → AuthorizationResult.

- [x] AccessPolicyEvaluatorTests bao gồm admin không có quyền nội dung từ role, cross-tenant denial, grant hết hạn/đọc riêng, metadata, bin purge và phạm vi DepartmentAdmin; 17 unit test tương ứng pass.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(AuthorizationDecision.Deny, result.Decision);
Assert.Equal(0, databaseProbe.PermissionReads);
~~~

- [x] RedisAuthorizationTests xác nhận Redis outage, thiếu snapshot và scope updating trả Unavailable; snapshot đầy đủ thiếu grant trả Deny. Service không nhận DbContext/repository/storage nên không có fallback DB hoặc storage.
- [x] AccessPolicyEvaluator áp dụng policy cho file.read/update/delete/share, file.metadata.read, bin.purge, user.manage, department.manage, quota.department.allocate, quota.user.allocate, audit.read và system.manage. Lua đọc snapshot + mọi scope liên quan atomically; không đọc bảng quyền từ CheckAsync.
- [x] AssertStillValidAsync so sánh generation, user/session/resource và toàn bộ scope versions; re-evaluate action theo thời gian Redis, nên grant hết hạn hoặc thay đổi quyền chặn side effect. ChangedVersionPreventsSideEffect và ExpiredGrantPreventsSideEffect pass.
- [x] Chạy AccessPolicyEvaluatorTests (15 pass), RedisAuthorizationTests (6 pass), integration regression đầy đủ (12 pass) và API build thành công; còn 2 warning GoogleCredential cũ.
- [ ] Commit: feat: enforce Redis-backed RBAC PBAC and ReBAC.

## Task 5: Mutation coordinator, outbox và pending API

**Files**
- Create: DA/Common/Interfaces/IAuthorizationMutationCoordinator.cs; DA/Common/Interfaces/IAuthorizationProjectionWriter.cs.
- Create: DA/Common/Authorization/MutationModels.cs; DI/Security/AuthorizationMutationCoordinator.cs; DI/Security/AuthorizationProjectionWriter.cs.
- Create: DI/Workers/AuthorizationOutboxWorker.cs; DI/Security/Scripts/begin-change.lua; DI/Security/Scripts/publish-change.lua.
- Create: API/Controllers/OperationsController.cs; IT/Authorization/AuthorizationMutationTests.cs.
- Modify: DI/DependencyInjection/DependencyInjection.cs.

**Interfaces**
- Produces: MutationRequest(string ScopeKey, string IdempotencyKey, string PayloadHash); MutationReceipt(Guid OperationId, long Version, MutationStatus Status).
- Produces: IAuthorizationMutationCoordinator.ExecuteAsync(MutationRequest request, Func<CancellationToken,Task> persistChange, CancellationToken ct) → Task<MutationReceipt>; GetAsync(Guid operationId, CancellationToken ct) → Task<MutationReceipt>.
- persistChange chạy trong transaction của coordinator với cùng scoped AppDbContext; nó không tự commit.
- Produces: IAuthorizationProjectionWriter.PublishAsync(Guid operationId, CancellationToken ct) → Task; đọc DB chủ động ở worker, không trên CheckAsync.
- MutationStatus = Pending/Applied/Failed; receipt Applied chỉ khi Redis acknowledgement. Version tăng đơn điệu, không giảm.

- [x] Đã thêm coordinator với advisory lock theo scope, idempotency theo payload, ghi operation/scope/outbox trong transaction và giữ Redis marker khi kết quả commit không rõ; duplicate trả cùng receipt, payload khác bị conflict.
- [x] Lua begin/cancel/publish dùng operation + version làm CAS; projection writer dựng lại snapshot user/file/grant của scope trong khi marker còn khóa, xóa key stale theo inventory rồi chỉ mở scope sau Redis ACK.
- [x] Đã đăng ký outbox worker; operation/outbox chỉ đánh Applied/Published sau ACK, còn retry sau Redis publish trước DB ACK là idempotent.
- [x] Integration kiểm tra idempotency, hash mismatch conflict, snapshot projections, operation/outbox Applied và Lua không cho event cũ xóa marker mới.
- [x] Integration mô phỏng Redis dừng sau khi DB commit: operation vẫn Pending và marker còn khóa; sau Redis restart, worker writer dựng lại projection, nhận ACK và đánh Applied/Published.
- [x] Thêm `GET /api/operations/{id}`; operation lưu user khởi tạo, truy vấn chỉ trả trạng thái cho đúng user đó (người khác nhận 404, không lộ operation tồn tại).
- [ ] Còn thiếu API mutation trả 202 và hợp đồng atomic nhiều scope cho thay đổi toàn cục/di chuyển tenant-department; Task 5 chưa hoàn thành.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(MutationStatus.Pending, receipt.Status);
Assert.Equal(newOperationId, activeMarkerOperationId);
~~~

- [x] Chạy integration filter Authorization; các test idempotency, payload mismatch, Redis outage/recovery và event cũ đều pass.
- [x] Serialize theo scope qua DB advisory lock cùng connection; tạo operation bền vững, marker Redis không TTL tự mở, transaction change/version/outbox; publish idempotent CAS operation/version. Nếu kết quả commit không rõ, giữ chặn tới worker hòa giải.
- [x] Gán role/chuyển tenant hoặc department được serialize qua global scope: evaluator luôn đọc global scope nên toàn bộ quyền bị fail-closed trong lúc writer dựng snapshot user/file/grant từ DB rồi publish version mới. Cách này an toàn nhưng chặn tạm thời mọi authorization check; chưa tối ưu bằng nhiều scope.
- [x] GET /api/operations/{id} chỉ trả trạng thái cho user khởi tạo operation; operation không tồn tại và operation của user khác đều trả 404.
- [x] PATCH /api/admin/users/{id}/status yêu cầu Idempotency-Key, serialize thay đổi trạng thái user bằng global scope, rollback nếu handler từ chối, tăng AuthorizationVersion khi trạng thái đổi; Pending → 202 + operationId, Applied → 200. Key được hash theo user để phân vùng idempotency.
- [x] PUT /api/admin/users/{id} cũng qua coordinator do payload hiện cho phép đổi IsActive; yêu cầu `user.manage` + Idempotency-Key và trả receipt Pending/Applied.
- [x] PUT /api/admin/users/{id}/roles yêu cầu Idempotency-Key và `user.manage`, chuẩn hóa role IDs để tính payload hash, chạy handler trong transaction coordinator và trả 202/200 theo trạng thái operation; đổi role/scope tăng AuthorizationVersion.
- [ ] Chuyển các mutation quyền còn lại qua coordinator (tạo/xóa user, department, file share/bin); thiết kế scope hẹp hơn để tránh global barrier cho tất cả authorization check.
- [x] Chạy IT authorization gồm Redis outage sau commit, retry sau restart và kiểm tra event đảo thứ tự; pass.
- [ ] Commit: feat: coordinate durable authorization changes.

## Task 6: Worker khôi phục quyền và bootstrap

**Files**
- Create: DA/Common/Interfaces/ISecurityRebuilder.cs; DI/Security/SecurityRebuilder.cs; DI/Workers/SecurityRecoveryWorker.cs.
- Create: IT/Authorization/SecurityRecoveryTests.cs.
- Modify: DI/DependencyInjection/DependencyInjection.cs; API/Program.cs.

**Interfaces**
- Produces: ISecurityRebuilder.RebuildAsync(CancellationToken ct) → Task<string> generation.
- Consumes: task 3 readiness; task 5 operations/outbox. Giai đoạn A kiểm chứng với fixture session; task 8 bổ sung recovery Auth trước khi mở production gate.
- Worker là đường DB → Redis chủ động; API người dùng không gọi RebuildAsync.

- [ ] Ghi SecurityRecoveryTests.CacheMissDoesNotTriggerRebuild; CrashMarkerIsReconciled; RestoreOldSnapshotDoesNotReopen; assertions rebuild call=0 từ CheckAsync, pending scope được hòa giải, readiness đóng tới hoàn tất.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(0, rebuildCallsTriggeredByUserRequest);
Assert.False(readinessAfterOldSnapshotRestore);
~~~

- [ ] Chạy IT filter SecurityRecoveryTests, red.
- [ ] Rebuild khi maintenance/startup/reconnect, batch 500 bản ghi, tối đa 1 batch/100 ms; chặn thay đổi cùng scope hoặc ghi nhận/replay watermark tới điểm ổn định trước publish generation. Giữ readiness đóng nếu DB lỗi.
- [ ] Không phục hồi access theo AOF quyền cũ; worker kiểm tra DB versions/outbox và session Auth đã thu hồi qua recovery API nội bộ task 8. Giữ thống kê generation, pending count và recovery duration, không log dữ liệu nhạy cảm.
- [ ] Chạy IT filter SecurityRecoveryTests; gate Auth ở task 8 phải phối hợp cùng gate DMS trước khi bật.
- [ ] Commit: feat: rebuild security state behind readiness gate.

## Task 7: Auth Service và provision identity

**Files**
- Create: AD/CloudDms.Auth.Domain.csproj; AA/CloudDms.Auth.Application.csproj; AI/CloudDms.Auth.Infrastructure.csproj; AAPI/CloudDms.Auth.API.csproj.
- Create: AD/Entities/AuthIdentity.cs; AD/Entities/ExternalIdentity.cs; AD/Entities/AuthSession.cs; AD/Entities/RefreshCredential.cs; AD/Entities/AuthOutboxMessage.cs; AD/Entities/Invitation.cs.
- Create: AA/Common/Interfaces/ISecurityRedis.cs; AA/Common/Interfaces/ISecurityReadiness.cs; AA/Common/Exceptions/SecurityUnavailableException.cs; AI/Security/RedisSecurityClient.cs; AI/Security/RedisReadiness.cs; AA/Common/Interfaces/IAuthDbContext.cs; AA/Common/Interfaces/IPasswordHasher.cs; AA/Common/Interfaces/IAccessTokenIssuer.cs; AA/Common/Interfaces/ISessionStateStore.cs; AA/Common/Interfaces/ILoginAttemptStore.cs; AA/Common/Models/LoginAttemptState.cs; AA/Common/Models/SessionState.cs.
- Create: AA/Features/Identities/ProvisionIdentityCommand.cs; AA/Features/Identities/ProvisionIdentityCommandHandler.cs; AA/DependencyInjection.cs.
- Create: AI/Persistence/AuthDbContext.cs; AI/Persistence/AuthEntityConfigurations.cs; AI/Services/BCryptPasswordHasher.cs; AI/DependencyInjection.cs.
- Create: AAPI/Program.cs; AAPI/Controllers/InternalIdentitiesController.cs; AAPI/Authentication/ServiceCertificateAuthentication.cs; AAPI/appsettings.json.
- Create: AT/AuthService.UnitTests.csproj; AT/IdentityProvisioningTests.cs; IT/Auth/IdentityProvisioningIntegrationTests.cs.
- Modify: dotnet-clean-architecture-cqrs-starter.sln; deploy/compose.integration.yml.

**Interfaces**
- Produces: ProvisionIdentityCommand(Guid OperationId, Guid UserId, string NormalizedEmail, bool IsActive, long Version) → IRequest<IdentityProvisionResult>.
- IdentityProvisionResult(Guid UserId, long Version, bool Ready); provisioning không tự tạo mật khẩu mặc định.
- IAuthDbContext có DbSet cho sáu entity và SaveChangesAsync(ct).
- IPasswordHasher.Hash(string password) → string; Verify(string password,string hash) → bool.
- Tách Auth DB và credential; UserId không FK xuyên service.

- [ ] Ghi IdentityProvisioningTests.SameOperationIsIdempotent; OlderVersionCannotReactivateIdentity; assertions cùng UserId và Version không giảm; inactive không thành active bởi event cũ.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(first.UserId, retry.UserId);
Assert.False(identity.IsActive);
~~~

- [ ] Tạo project/test references bốn tầng, chạy AT filter IdentityProvisioningTests red.
- [ ] Implement provisioning nội bộ mTLS/service allowlist; unique UserId/email, unique issuer/sub; identity Ready=false trước invitation acceptance/credential xác minh. Tạo Auth migrations trong AI/Migrations, pin EF 8.0.25/Pomelo 8.0.3 theo Infrastructure hiện có.
- [ ] Dùng BCrypt hiện có, cấu hình bắt buộc, không default secret; tạo các interface LoginAttempt/Session/Token signature theo task 8–9 để tránh type drift.
- [ ] Chạy AT/IT provisioning và build solution; pass; test LayerDependencyTests thêm Auth.
- [ ] Commit: feat: introduce isolated Auth service and provisioning.

## Task 8: Phiên bền vững, RS256 và refresh/logout

**Files**
- Create: AA/Common/Models/TokenModels.cs; AA/Features/Sessions/RefreshSessionCommand.cs; AA/Features/Sessions/RefreshSessionCommandHandler.cs; AA/Features/Sessions/LogoutSessionCommand.cs; AA/Features/Sessions/LogoutSessionCommandHandler.cs.
- Create: AI/Services/Rs256AccessTokenIssuer.cs; AI/Security/RedisSessionStateStore.cs; AI/Workers/AuthOutboxWorker.cs; AI/Services/AuthSessionService.cs.
- Create: AA/Common/Interfaces/IAuthSessionService.cs; AAPI/Controllers/InternalSessionsController.cs.
- Test: AT/SessionPolicyTests.cs; IT/Auth/SessionRotationTests.cs.
- Modify: AI/DependencyInjection.cs; AA/DependencyInjection.cs.

**Interfaces**
- IAccessTokenIssuer.Issue(Guid userId,Guid sessionId,DateTimeOffset now) → AccessTokenResult(string AccessToken,DateTimeOffset ExpiresAt).
- IAuthSessionService.CreateAsync(Guid userId,CancellationToken ct) → Task<SessionTokens>; RotateAsync(string refreshToken,CancellationToken ct) → Task<SessionTokens>; RevokeAsync(string refreshToken,CancellationToken ct) → Task.
- SessionTokens(AccessTokenResult Access,string RefreshToken,DateTimeOffset SessionExpiresAt) chỉ nội bộ Application, không serialize trực tiếp làm HTTP response.
- ISessionStateStore.BlockAsync(Guid sessionId,Guid operationId,CancellationToken ct) → Task; PublishAsync(SessionState state,CancellationToken ct) → Task; SessionState(UserId,SessionId,IsActive,ExpiresAt,Version).
- InternalSessionsController export session recovery snapshot phân trang theo cursor dưới maintenance, không expose refresh hash.

- [ ] Ghi SessionPolicyTests.TokenLifetimeIsFiveMinutes với Assert.Equal(TimeSpan.FromMinutes(5), expiresAt-now); SessionRotationTests.RotationDoesNotExtendSevenDayDeadline, UsedRefreshRevokesFamily, RedisFailureAfterCommitBlocksSession.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(TimeSpan.FromMinutes(5), access.ExpiresAt - now);
Assert.Equal(createdAt.AddDays(7), rotated.SessionExpiresAt);
~~~

- [ ] Chạy AT/IT filter tương ứng, red.
- [ ] Refresh token 32 byte CSPRNG, DB lưu SHA-256; mỗi session là một family, token-used tombstone giữ tới absolute expiry. DB row lock/unique đảm bảo chỉ một rotation thắng; dùng lại token thu hồi family.
- [ ] Trước mutation chặn session Redis, transaction state/outbox, publish version trước trả token; refresh/logout cùng session serialize, không resurrect sau logout. RS256 iss cấu hình, aud=cloud-dms-api, auth_key định danh credential, các claim task spec, không role/permission.
- [ ] Internal snapshot recovery phối hợp task 6; phiên inactive/expired không được activate. Test logout/refresh race, Auth DB commit lỗi, worker retry và restore AOF cũ. Pass cả AT/IT.
- [ ] Commit: feat: rotate and revoke durable Auth sessions.

## Task 9: Password login, cookie và CSRF

**Files**
- Create: AA/Features/Login/LoginCommand.cs; AA/Features/Login/LoginCommandHandler.cs; AA/Features/Login/LoginCommandValidator.cs.
- Create: AI/Security/RedisLoginAttemptStore.cs; AI/Security/Scripts/complete-login.lua.
- Create: AAPI/Controllers/AuthController.cs; AAPI/Security/RefreshCookieWriter.cs; AAPI/Security/CsrfFilter.cs; AAPI/Middleware/AuthExceptionHandler.cs.
- Test: AT/LoginPolicyTests.cs; IT/Auth/LoginLockoutTests.cs; IT/Auth/CookieSecurityTests.cs.

**Interfaces**
- LoginCommand(string Email,string Password) → IRequest<SessionTokens>.
- ILoginAttemptStore.GetAsync(string emailKey,CancellationToken ct) → Task<LoginAttemptState>; CompleteAsync(string emailKey,bool passwordValid,CancellationToken ct) → Task<LoginAttemptState>.
- LoginAttemptState(bool Allowed,int Failures,DateTimeOffset? LockedUntil); CompleteAsync atomic recheck khóa sau BCrypt, sliding window theo Redis TIME.
- CookieWriter.Write(HttpResponse response,SessionTokens tokens) → void; Clear(HttpResponse response) → void.

- [ ] Ghi LoginLockoutTests.FifthFailureLocksForThirtyMinutes, WindowSlides, LockedRequestDoesNotExtendExpiry; assertions failures=5, remaining 30 phút tại lần 5, TTL không reset bởi request bị khóa.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(5, state.Failures);
Assert.Equal(now.AddMinutes(30), state.LockedUntil);
~~~

- [ ] Ghi CookieSecurityTests.RefreshCookieIsHttpOnlyAndAbsentFromJson; CrossOriginRefreshRejected; assertions HttpOnly/Secure/Lax/Path=/api/auth, JSON không refreshToken, request thiếu/sai CSRF bị 403.
- [ ] Chạy AT/IT red; implement login email chuẩn hóa chung (trim + invariant case, không bỏ dấu chấm/+alias), key HMAC-SHA256 bằng secret cấu hình, dummy BCrypt khi unknown email.
- [ ] Sau mật khẩu đúng phải recheck trạng thái identity/Redis; hoàn tất thành công xóa count rồi tạo phiên task 8. Khóa chỉ mật khẩu; IsActive=false chặn cả phương thức. 429 có Retry-After, lỗi chung không lộ email.
- [ ] GET /api/auth/csrf cấp antiforgery request token, POST refresh/logout kiểm tra Origin + token; JSON chỉ accessToken/expiresAt/userId. Login endpoint cũng kiểm tra Origin và CSRF để chặn login CSRF.
- [ ] Test 5 request đồng thời, DB/Redis lỗi, successful login reset; chạy filter LoginLockoutTests/CookieSecurityTests, pass.
- [ ] Commit: feat: add password lockout and secure Auth cookies.

## Task 10: Google OIDC và liên kết lần đầu

**Files**
- Create: AA/Common/Interfaces/IGoogleOidcClient.cs; AA/Common/Interfaces/IGoogleLinkStore.cs.
- Create: AA/Features/Google/StartGoogleLoginQuery.cs; AA/Features/Google/StartGoogleLoginQueryHandler.cs; AA/Features/Google/CompleteGoogleLoginCommand.cs; AA/Features/Google/CompleteGoogleLoginCommandHandler.cs; AA/Features/Google/ConfirmGoogleLinkCommand.cs; AA/Features/Google/ConfirmGoogleLinkCommandHandler.cs.
- Create: AI/Google/GoogleOidcClient.cs; AI/Google/RedisGoogleLinkStore.cs; AI/Services/SmtpEmailProofSender.cs; AA/Common/Interfaces/IEmailProofSender.cs; AAPI/Controllers/GoogleAuthController.cs.
- Test: AT/GoogleLinkPolicyTests.cs; IT/Auth/GoogleOidcTests.cs; IT/Support/OidcProviderStub.cs.

**Interfaces**
- StartGoogleLoginQuery() → IRequest<GoogleStartResult(string AuthorizationUrl)>.
- CompleteGoogleLoginCommand(string Code,string State,string CorrelationCookie) → IRequest<GoogleCompletionResult>.
- GoogleCompletionResult(SessionTokens? Tokens,string? LinkChallengeId); không cả hai cùng có giá trị.
- IGoogleOidcClient.ExchangeAndVerifyAsync(string code,string verifier,string nonce,CancellationToken ct) → Task<GoogleIdentity(string Issuer,string Sub,string Email,bool EmailVerified,string? HostedDomain)>.
- ConfirmGoogleLinkCommand(string ChallengeId,string? Password,string? EmailCode) → IRequest<SessionTokens>; IGoogleLinkStore giữ challenge/state/nonce/verifier một lần, không trả cho client.

- [ ] Ghi GoogleLinkPolicyTests.ThirdPartyEmailRequiresChallenge; ExistingSubWinsOverChangedEmail; DifferentSubCannotOverwriteLink; assert challenge, đúng UserId cũ, conflict.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.NotNull(completion.LinkChallengeId);
Assert.Null(completion.Tokens);
~~~

- [ ] Ghi GoogleOidcTests.ReplayedStateFails, WrongIssuerAudienceNonceFails, MissingAccountDoesNotRegister; assert 401, user count không tăng. Chạy red với stub OIDC, không gọi Google thật.
- [ ] Code+PKCE S256, correlation cookie Secure/HttpOnly/Lax, state/nonce TTL 5 phút và consume nguyên tử. Validate token signature qua thư viện IdentityModel, discovery/JWKS cache có giới hạn; aud Google client-id, không aud DMS.
- [ ] Gmail cần email_verified; Workspace cần email_verified và hd phù hợp; email bên thứ ba yêu cầu proof task 9 password (tôn trọng khóa) hoặc OTP 8 chữ số CSPRNG, 5 phút, tối đa 5 lần, resend cooldown 60 giây. Store hash proof ràng buộc challenge; gửi email qua SMTP adapter Auth.
- [ ] Unique issuer/sub xử lý race; không auto-register; inactive từ chối. Google thành công không reset lock/count mật khẩu. Callback đặt cookie, redirect đường frontend cố định, không token trong URL.
- [ ] Chạy AT/IT filters; dùng provider stub bao gồm lỗi mạng/JWKS đổi khóa và callback hai lần; pass.
- [ ] Commit: feat: add verified Google login and account linking.

## Task 11: Nhân sự, vai trò và đồng bộ Auth

**Files**
- Modify: DA/Features/SystemAdmin/Commands/CreateUser/CreateUserCommandHandler.cs; DA/Features/SystemAdmin/Commands/UpdateUser/UpdateUserCommandHandler.cs; DA/Features/SystemAdmin/Commands/AssignUserRoles/AssignUserRolesCommandHandler.cs; DA/Features/SystemAdmin/Commands/ChangeUserStatus/ChangeUserStatusCommandHandler.cs; DA/Features/SystemAdmin/Commands/DeleteUser/DeleteUserCommandHandler.cs; DA/Features/SystemAdmin/Commands/ResetUserPassword/ResetUserPasswordCommandHandler.cs.
- Modify: DA/Features/TenantAdmin/Commands/CreateDepartmentAdmin/CreateDepartmentAdminCommandHandler.cs; DA/Users/Commands/CreateUser/CreateUserCommandHandler.cs; DA/Users/Commands/UpdateUser/UpdateUserCommandHandler.cs; DA/Users/Commands/DeactiveUser/DeactiveUserHandler.cs.
- Create: DA/Common/Interfaces/IAuthIdentityClient.cs; DI/Auth/AuthIdentityClient.cs; DI/Workers/IdentityProvisioningWorker.cs.
- Create: DA/Features/Personnel/Commands/ManagePersonnel/ManagePersonnelCommand.cs; DA/Features/Personnel/Commands/ManagePersonnel/ManagePersonnelCommandHandler.cs; API/Controllers/PersonnelController.cs.
- Test: IT/Personnel/PersonnelScopeTests.cs; IT/Auth/IdentitySynchronizationTests.cs.

**Interfaces**
- IAuthIdentityClient.ProvisionAsync(Guid operationId,Guid userId,string email,bool active,long version,CancellationToken ct) → Task; ResetPasswordAsync(Guid operationId,Guid userId,string newPassword,CancellationToken ct) → Task.
- ManagePersonnelCommand(Guid? UserId,string Email,Guid? TenantId,Guid? DepartmentId,string Operation,string IdempotencyKey) → IRequest<MutationReceipt>; Operation là create/update/deactivate; validator từ chối string khác.
- Reset password được gọi đồng bộ nội bộ TLS, không ghi plaintext password vào outbox. Nếu timeout không rõ dùng operationId để truy vấn trạng thái; payload hash sử dụng HMAC, không plain SHA password.

- [ ] Ghi PersonnelScopeTests.DepartmentAdminCannotManageOtherDepartmentOrElevateRoles; TenantAdminCannotGrantSystemAdmin; DeletionPreservesOwnedFilesAndQuota; assert Deny và dữ liệu không đổi.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(AuthorizationDecision.Deny, result.Decision);
Assert.Equal(1, provisionedIdentityCount);
~~~

- [ ] Ghi IdentitySynchronizationTests.ProvisionRetryDoesNotDuplicate; DisableBlocksBeforeAuthAcknowledges; assertions một identity, request tiếp theo bị chặn dù worker chậm. Chạy red.
- [ ] Mọi đường sửa nhân sự dùng coordinator task 5 và scope từ Redis task 4; bỏ hash/password writes tại DMS. Tạo user PendingProvision, provision Auth idempotent rồi publish quyền.
- [ ] Role effective mặc định User ánh xạ Employee cũ; Manager cũ không tự thành DepartmentAdmin. Gán role và đổi phòng ban tăng version, không cho actor nâng quyền vượt phạm vi.
- [ ] Deactivate/delete là tombstone khi còn file/quota, giữ UserId; email thay đổi phải đồng bộ identity version và xác minh, không đổi Google sub. Chuyển phòng ban giữ quota bucket cũ; chỉ mở scope mới khi tổng allocation và usage hợp lệ.
- [ ] Chạy IT filters personnel/synchronization và AT provisioning; pass.
- [ ] Commit: feat: scope personnel administration and synchronize identities.

## Task 12: Bảo vệ use case và danh tính trong DMS

**Files**
- Create: DA/Common/Behaviors/AuthorizationBehavior.cs; DA/Common/Authorization/IAuthorizedRequest.cs; DA/Common/Authorization/AuthorizationRequirementResolver.cs.
- Create: API/Authentication/KongTokenAuthenticationHandler.cs; API/Authentication/GatewayCertificateValidator.cs; API/Authentication/GatewayIdentityOptions.cs.
- Modify: API/CleanArchCqrs.API.csproj; API/Services/CurrentUserService.cs; DA/Common/Interfaces/ICurrentUserService.cs; API/Program.cs; API/Authorization/PermissionPolicyProvider.cs; API/Middleware/GlobalExceptionHandlerMiddleware.cs; DA/DependencyInjection/ApplicationServiceExtensions.cs.
- Modify: API/Controllers/Admin/UsersController.cs; API/Controllers/Admin/StorageProvidersController.cs; API/Controllers/Admin/TenantQuotaController.cs; API/Controllers/Tenant/DepartmentsController.cs; API/Controllers/Tenant/DepartmentAdminsController.cs; API/Controllers/UserController.cs.
- Test: IT/Api/DefaultProtectionTests.cs; UT/Authorization/RequestCoverageTests.cs.

**Interfaces**
- IAuthorizedRequest.Requirements → IReadOnlyList<AuthorizationRequirement(string Action,AuthorizationTarget Target)>.
- AuthorizationRequirementResolver.Resolve(object request) → IReadOnlyList<AuthorizationRequirement>; unknown request mặc định reject, public/internal allowlist riêng.
- ICurrentUserService thêm SessionId:Guid?, DepartmentId:Guid?; TenantId/Role/Email lấy từ snapshot Redis đã kiểm tra, không JWT claims. Không fallback aliases id/userId.
- Gateway handler chỉ tạo ClaimsPrincipal từ sub/sid trong JWT đã được Kong kiểm tra qua mTLS pinned peer; audience/issuer/alg/required claims vẫn kiểm tra.

- [ ] Ghi DefaultProtectionTests.UnknownEndpointAndUnknownMediatrRequestDenied; WrongCertificateCannotCreatePrincipal; ForgedConsumerHeaderDoesNotChangeUser. RequestCoverageTests kiểm tra tất cả IRequest có requirement hoặc public/internal allowlist, không vô tình public use case cũ.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
Assert.Equal(0, handlerCalls);
~~~

- [ ] Chạy red; implement fallback policy authenticated và AuthorizationBehavior trước handler, sau validation. Public chỉ auth/invitation cần thiết; health liveness không trả dữ liệu.
- [ ] API nhận upstream TLS riêng, không dùng forwarded certificate header từ nguồn bất kỳ; service-call certificate khác Kong không được tạo user principal. Claims đọc từ original Authorization duy nhất.
- [ ] Map 401/403/429/503/202/409, không exception text; CORS allowlist, bỏ fallback JWT key và tự xác nhận email trong startup. Không seed demo credential trong production.
- [ ] Apply resolver cho mọi use case existing kể cả Product/storage/admin; use case ngoài phạm vi nghiệp vụ mới vẫn phải có quyền hợp lệ hoặc bị đóng.
- [ ] Chạy IT/UT filters; thiếu Redis không được chạy handler DB/storage, pass.
- [ ] Commit: feat: protect DMS use cases with trusted Kong identity.

## Task 13: Quota cấp phát và giữ chỗ upload

**Files**
- Create: DA/Common/Interfaces/IQuotaService.cs; DI/Quota/TransactionalQuotaService.cs.
- Create: DA/Features/Quota/Commands/AllocateDepartmentQuota/AllocateDepartmentQuotaCommand.cs; DA/Features/Quota/Commands/AllocateDepartmentQuota/AllocateDepartmentQuotaCommandHandler.cs; DA/Features/Quota/Commands/AllocateUserQuota/AllocateUserQuotaCommand.cs; DA/Features/Quota/Commands/AllocateUserQuota/AllocateUserQuotaCommandHandler.cs.
- Create: API/Controllers/QuotaController.cs; IT/Quota/QuotaConcurrencyTests.cs.
- Modify: DA/Features/SystemAdmin/Commands/AllocateTenantStorageQuota/AllocateTenantStorageQuotaCommand.cs; DA/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs; DI/DependencyInjection/DependencyInjection.cs.

**Interfaces**
- AllocateDepartmentQuotaCommand(Guid DepartmentId,long Bytes) và AllocateUserQuotaCommand(Guid UserId,long Bytes) → IRequest<Result<bool>>.
- IQuotaService.AllocateDepartmentAsync(Guid departmentId,long bytes,CancellationToken ct) → Task; AllocateUserAsync(Guid userId,Guid departmentId,long bytes,CancellationToken ct) → Task.
- ReserveAsync(Guid ownerId,Guid departmentId,long bytes,string idempotencyKey,CancellationToken ct) → Task<Guid> reservationId; CommitAsync(Guid reservationId,CancellationToken ct) → Task; CancelAsync(Guid reservationId,CancellationToken ct) → Task.
- Quota service chỉ enforcement số học DB, không quyết định quyền actor; task 4 kiểm tra quyền trước gọi.

- [ ] Ghi QuotaConcurrencyTests.ConcurrentAllocationsNeverExceedDepartment; CannotReduceBelowAllocatedOrUsed; ConcurrentUploadReservationsEnforceAllLevels; assertions tổng <= cap, một request thất bại khi vượt quota.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.True(allocatedBytes <= departmentQuotaBytes);
Assert.Equal(1, successfulReservationsWhenOnlyOneFits);
~~~

- [ ] Chạy red; lock hàng theo thứ tự tenant → department → user bucket → provider, transaction allocation/reservation; bytes checked arithmetic, từ chối âm/overflow.
- [ ] Upload giữ chỗ trước lưu vật lý, commit usage sau thành công; timeout lưu file không rõ giữ reservation để worker hòa giải, không cancel rồi cho upload khác dùng cùng dung lượng.
- [ ] Dùng cùng quota pipeline cho update file (delta dung lượng); shrink chỉ hoàn usage sau storage xác nhận; idempotency key không cho upload retry nhân đôi metadata.
- [ ] Chạy IT filter QuotaConcurrencyTests trên MySQL thật với barrier đồng thời; pass.
- [ ] Commit: feat: enforce hierarchical quotas transactionally.

## Task 14: File list/download/update/share chỉ theo quan hệ

**Files**
- Modify: DA/Features/Files/Commands/UploadFile/UploadFileCommand.cs; DA/Features/Files/Commands/UploadFile/UploadFileCommandHandler.cs; DA/Features/Files/Queries/DownloadFile/DownloadFileQuery.cs; DA/Features/Files/Queries/GetTenantFiles/GetTenantFilesQueryHandler.cs; DA/Features/Files/DTOs/FileEntryDto.cs; API/Controllers/FilesController.cs.
- Create: DA/Features/Files/Commands/UpdateFile/UpdateFileCommand.cs; DA/Features/Files/Commands/UpdateFile/UpdateFileCommandHandler.cs; DA/Features/Files/Commands/ShareFile/ShareFileCommand.cs; DA/Features/Files/Commands/ShareFile/ShareFileCommandHandler.cs; DA/Features/Files/Commands/RevokeShare/RevokeShareCommand.cs; DA/Features/Files/Commands/RevokeShare/RevokeShareCommandHandler.cs.
- Create: DA/Common/Interfaces/IAuthorizedFileIndex.cs; DI/Security/RedisAuthorizedFileIndex.cs; API/Controllers/FileSharesController.cs.
- Test: IT/Files/FileAccessTests.cs.

**Interfaces**
- ShareFileCommand(Guid FileId,Guid RecipientId,bool CanRead,bool CanWrite,bool CanDelete,DateTimeOffset? ExpiresAt,string IdempotencyKey) → IRequest<MutationReceipt>; RevokeShareCommand(Guid FileId,Guid RecipientId,string IdempotencyKey) → IRequest<MutationReceipt>.
- UpdateFileCommand(Guid FileId,Stream Content,string ContentType,long SizeBytes,string IdempotencyKey) → IRequest<MutationReceipt>; không đổi owner/department từ request.
- IAuthorizedFileIndex.PageAsync(ActorIdentity actor,string action,string? cursor,int limit,CancellationToken ct) → Task<AuthorizedFilePage>; AuthorizedFilePage gồm IDs, cursor, AuthorizationStamp. Limit 100.
- Redis index là projection phục vụ danh sách, cập nhật cùng version/share/lifecycle; không join DB FileShare để quyết định file nào được hiện.

- [ ] Ghi FileAccessTests.DepartmentAdminNeedsExplicitShare; ReadGrantCannotUpdateOrDelete; TenantMetadataCannotDownload; CrossDepartmentShareWithinTenantAllowed; assert đúng Allow/Deny và StorageProbe calls=0 khi deny.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(AuthorizationDecision.Deny, result.Decision);
Assert.Equal(0, storageProbe.DownloadCalls);
~~~

- [ ] Ghi ListAndCountsDoNotLeakUnsharedFiles; assertions IDs/count không gồm file chưa share, DTO không StorageKey hoặc provider credentials. Chạy red.
- [ ] Bỏ UserId/TenantId override và fallback user active trong upload; actor/scope từ Redis, owner là actor. Kiểm tra folder/provider thuộc đúng scope trước gọi storage.
- [ ] Phân biệt endpoint danh sách file có nội dung và endpoint metadata TenantAdmin; pagination IDs từ Redis rồi lấy DB metadata, recheck stamp trước trả trang nếu version đổi; không trả tenant-wide counts cho User.
- [ ] Owner mới được share/revoke; recipient cùng tenant active; quyền read/write/delete độc lập, expiry UTC; dùng coordinator task 5 cho share và update lifecycle/version.
- [ ] Chạy IT filter FileAccessTests cùng RedisAuthorizationTests; pass, không permission DB reads trong đường authorize.
- [ ] Commit: feat: enforce file ownership and explicit sharing.

## Task 15: Bin, purge, restore và audit

**Files**
- Modify: DA/Features/Files/Commands/DeleteFile/DeleteFileCommand.cs; API/Controllers/FilesController.cs.
- Create: DA/Features/Files/Commands/PurgeFile/PurgeFileCommand.cs; DA/Features/Files/Commands/PurgeFile/PurgeFileCommandHandler.cs; DA/Features/Files/Commands/RestoreFile/RestoreFileCommand.cs; DA/Features/Files/Commands/RestoreFile/RestoreFileCommandHandler.cs.
- Create: DA/Features/Files/Queries/GetDepartmentBin/GetDepartmentBinQuery.cs; DA/Features/Files/Queries/GetDepartmentBin/GetDepartmentBinQueryHandler.cs; DA/Features/Audit/Queries/GetAuditLogs/GetAuditLogsQuery.cs; DA/Features/Audit/Queries/GetAuditLogs/GetAuditLogsQueryHandler.cs.
- Create: DI/Workers/FilePurgeWorker.cs; DA/Common/Interfaces/IPhysicalFileDeletion.cs; DI/Storage/PhysicalFileDeletion.cs; API/Controllers/RecycleBinController.cs; API/Controllers/AuditLogsController.cs.
- Test: IT/Files/RecycleBinTests.cs; IT/Audit/AuditScopeTests.cs.
- Modify: DI/Services/Storage/LocalStorageService.cs; DI/Services/Storage/GoogleDriveStorageService.cs; DI/Services/Storage/OneDriveStorageService.cs (chỉ xử lý xóa idempotent).

**Interfaces**
- DeleteFileCommand giữ FileId, đổi ý nghĩa sang bin, trả MutationReceipt thay Result<bool>.
- PurgeFileCommand(Guid FileId,string IdempotencyKey), RestoreFileCommand(Guid FileId,string IdempotencyKey) → IRequest<MutationReceipt>.
- IPhysicalFileDeletion.DeleteAsync(Guid fileId,CancellationToken ct) → Task<PhysicalDeleteResult>; result Deleted/AlreadyAbsent/RetryableFailure, không coi timeout/lỗi quyền provider là AlreadyAbsent.
- Audit query chỉ trả metadata/action/actor/time scoped, pagination 100, không token/storage config.

- [ ] Ghi RecycleBinTests.SharedDeleterUsesOwnerDepartmentBin; SoftDeleteDoesNotReleaseQuotaOrCallStorage; PurgeFailureKeepsUsage; RepeatedPurgeReleasesExactlyOnce; assert owning department không đổi, delete calls=0 lúc bin, UsedBytes chỉ giảm một lần.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(originalUsedBytes, usedBytesAfterBin);
Assert.Equal(originalUsedBytes - fileSize, usedBytesAfterRepeatedPurge);
~~~

- [ ] Ghi OwnerRestoreDoesNotReactivateShares và AuditScopeTests.TenantAdminCannotReadOtherTenantLogs; assert grant cũ vẫn deny, log chéo tenant deny. Chạy red.
- [ ] Binned làm content/share unavailable; purge chỉ TenantAdmin cùng tenant, không cho SystemAdmin/DepartmentAdmin dựa vào role. Chặn version bằng coordinator; queue purging/outbox.
- [ ] Worker xác nhận physical delete, transaction kiểm tra QuotaReleasedAt và trừ owner-bucket/department/tenant/provider một lần; retry crash giữa delete/commit dùng AlreadyAbsent hợp lệ, không nuốt lỗi như mã cũ.
- [ ] Restore chỉ owner, file chưa purging/purged; xoá grants cũ; audit mọi upload/update/bin/restore/share/purge và outcome, actor lấy từ identity, không fallback owner làm người thực hiện.
- [ ] Chạy IT filter RecycleBinTests/AuditScopeTests; pass.
- [ ] Commit: feat: implement department recycle bins and safe quota release.

## Task 16: Kong OSS, mTLS và fault test end-to-end

**Files**
- Create: Gateway/kong.yml.template; Gateway/render-config.ps1; Gateway/README.md.
- Create: deploy/compose.security.yml; src/CloudDms.Auth.API/Dockerfile; src/CleanArchCqrs.API/Dockerfile.
- Create: IT/Gateway/KongBoundaryTests.cs; IT/Security/SecurityOutageTests.cs.
- Modify: deploy/.env.example; scripts/New-TestSecrets.ps1.

**Interfaces**
- render-config.ps1 nhận đường dẫn public key/cert mount và output .local/kong.yml; không nhúng private key vào file tracked.
- Kong OSS 3.9.3, DB-less, auth_key credential selector, RS256 public keys, claims_to_verify exp/nbf, chỉ Authorization; không anonymous ở /api nghiệp vụ.
- Public route /api/auth tới Auth; /api/files, nhân sự/quota/audit/bin tới DMS; internal service endpoints không có route public.

- [ ] Ghi KongBoundaryTests.DirectUpstreamAndForgedCertificateFail; WrongAudienceIssuerAlgorithmAndMissingClaimsFail; HeaderQueryCookieSmugglingFails; RotatedPublicKeysOverlapOnlyFiveMinutes. Chạy red trên compose.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
Assert.Equal(0, permissionDbReads);
~~~

- [ ] Pin image Kong 3.9.3 và digest sau pull xác minh task này; kiểm tra đúng schema plugin của phiên bản. Kong → upstream HTTPS với client cert riêng, upstream verify certificate; chỉ expose proxy TLS, không expose upstream/Admin API.
- [ ] Kong loại X-User-ID/tenant/role và consumer header client, giữ original Authorization; không transformer thay token sau JWT plugin. Rate limit login theo IP bổ sung, không thay bộ đếm theo tài khoản Redis.
- [ ] SecurityOutageTests tắt Redis giữa login/refresh/download/upload: 503 và không DB fallback quyền/storage side effect; restart AOF cũ phải đóng tới rebuild, revoked session vẫn denied.
- [ ] Chạy docker compose -f deploy/compose.security.yml config và IT filter KongBoundaryTests/SecurityOutageTests; tất cả pass với network thật, không chỉ WebApplicationFactory.
- [ ] Commit: feat: integrate Kong OSS and verify trust boundaries.

## Task 17: Client RAM và hợp đồng browser

**Files**
- Create: clients/browser/auth-client.js; clients/browser/README.md.
- Create: BT/CloudDms.BrowserTests.csproj; BT/AuthBrowserTests.cs; BT/Fixtures/client.html; BT/Support/BrowserHost.cs.

**Interfaces**
- createAuthClient({baseUrl}) → {login(email,password), bootstrap(), fetchAuthorized(path,init), logout(), hasSession()}; Promise async, access token đóng trong module, không export getter token.
- Client dùng credentials:include cho cookie; CSRF từ task 9; 401 refresh tối đa một lần, 403 không refresh, 503 trả lỗi không retry vòng lặp.
- Web Locks API khóa refresh theo origin; BroadcastChannel chỉ báo session epoch/thay đổi, không lưu/broadcast token. Sau lock tab có thể refresh bằng cookie hiện hành, không gửi token cũ chụp trước lock.

- [ ] Ghi AuthBrowserTests.ReloadBootstrapsWithoutPersistentToken; TwoTabsDoNotReplayRefresh; LogoutClearsMemory; ForbiddenAndUnavailableDoNotLoop; assert storage local/session/IndexedDB không token, cookie không đọc được bằng JS, số request refresh hữu hạn, logout làm call API bị 401.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.False(persistentStorageContainsToken);
Assert.Equal(0, refreshReplayResponses);
~~~

- [ ] Tạo project Playwright .NET 8 và browser fixture host HTTPS cùng origin; chạy red với Auth/Kong test environment.
- [ ] Implement module framework-neutral, UI fixture chỉ để test. README hướng dẫn frontend thật import module; repo hiện chưa có frontend nên không tuyên bố hoàn tất tích hợp với ứng dụng ngoài repo.
- [ ] Chạy dotnet build tests/CloudDms.BrowserTests/CloudDms.BrowserTests.csproj; pwsh tests/CloudDms.BrowserTests/bin/Debug/net8.0/playwright.ps1 install chromium; dotnet test tests/CloudDms.BrowserTests/CloudDms.BrowserTests.csproj; pass.
- [ ] Commit: feat: add in-memory browser auth client and contract tests.

## Task 18: Migrate credential, invitation và quyền cũ

**Files**
- Create: tools/CloudDms.Migration/CloudDms.Migration.csproj; tools/CloudDms.Migration/Program.cs; tools/CloudDms.Migration/CredentialImporter.cs; tools/CloudDms.Migration/OwnershipReconciler.cs; tools/CloudDms.Migration/RolePermissionMigrator.cs.
- Create: AA/Features/Invitations/ValidateInvitationQuery.cs; AA/Features/Invitations/ValidateInvitationQueryHandler.cs; AA/Features/Invitations/AcceptInvitationCommand.cs; AA/Features/Invitations/AcceptInvitationCommandHandler.cs; AAPI/Controllers/InvitationsController.cs.
- Modify: API/Controllers/AuthController.cs; API/Controllers/UserController.cs; DA/Users/Commands/AcceptInvitation/AcceptInvitationCommand.cs; DA/Users/Queries/ValidateInvitation/ValidateInvitationQuery.cs; DI/Persistence/DbInitializer.cs.
- Remove sau cutover: DA/Users/Commands/Login/LoginCommand.cs; DA/Users/Commands/Login/LoginCommandHandler.cs; DI/Services/JwtTokenService.cs; DA/Common/Interfaces/IJwtTokenService.cs.
- Test: IT/Migration/CutoverTests.cs; tools/CloudDms.Migration/README.md.
- Create: DI/Migrations/<EF-generated>_RemoveMigratedCredentials.cs và Designer; Modify: snapshot/User mappings/IApplicationDbContext.

**Interfaces**
- Tool CLI: dotnet run --project tools/CloudDms.Migration -- --dry-run; --apply chạy chỉ khi maintenance và snapshot backup đã xác nhận.
- CredentialImporter.ImportAsync(bool dryRun,CancellationToken ct) → Task<ImportReport>; report chỉ counts/error IDs, không hash/password/email raw.
- Invitation acceptance ở Auth xác minh token một lần, set BCrypt password, phát session qua task 8 sau provision Ready; không phát HS256 ở DMS.
- EF migration filename timestamp do EF tạo, không hardcode ngày để né trùng thứ tự.

- [ ] Ghi CutoverTests.PreservesUserIdAndBcryptHash; OldLoginCannotIssueTokens; InvitationRetryDoesNotDuplicateIdentity; LegacyDeletedFileDoesNotDoubleRelease; assert UserId/hash đúng, endpoint login cũ 404/410, quota giữ đúng.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(originalUserId, importedIdentity.UserId);
Assert.Equal(originalHash, importedIdentity.PasswordHash);
~~~

- [ ] Chạy red trên bản sao DB fixture; tool dry-run báo normalized-email collision, conflicting roles, owner không có department, file IsDeleted storage không rõ. Không tự gán department mặc định hoặc hoàn usage bằng phỏng đoán.
- [ ] Import hash qua kênh riêng một lần, không rehash password; giữ UserId. Tạo manifest mapping ownership do người vận hành cung cấp cho record không rõ; unresolved làm cutover fail.
- [ ] Migration role explicit: SystemAdmin bỏ mọi content permission, TenantAdmin chỉ metadata/log/bin/quota/personnel; Employee → User, Manager cần mapping explicit nếu scope không rõ.
- [ ] Maintenance → backup → additive migration/import → validate counts/checksum (không log credential) → rebuild Redis → switch routes → vô hiệu HS256/login cũ → smoke tests → mở readiness. Migration drop credential riêng chỉ sau cutover verified và backup; rollback giữ gate đóng, không mở auth cũ như fallback.
- [ ] Chạy dry-run + IT filter CutoverTests; xác nhận toàn bộ credential/invitation writers đã chuyển Auth; pass.
- [ ] Commit: feat: migrate identities and retire legacy authentication.

## Task 19: Kiểm chứng tích hợp, CI và tài liệu vận hành

**Files**
- Create: IT/EndToEnd/AuthAuthorizationJourneyTests.cs; IT/EndToEnd/QuotaBinJourneyTests.cs; scripts/Test-SecurityFlow.ps1; .github/workflows/security-tests.yml.
- Modify: ARCHITECTURE.md; SECURITY.md; sdd/spec/sso-auth-spec.md.
- Create: sdd/runbooks/security-recovery.md; sdd/runbooks/auth-cutover.md.
- Modify README.md chỉ khi thực sự cần link runbook, giữ mọi thay đổi có sẵn của người dùng.

**Interfaces**
- Test-SecurityFlow.ps1 gọi compose test + dotnet test theo project, fail fast exit nonzero khi gate fail; không dùng DB người dùng.
- Health liveness tách readiness; metrics security_unavailable, authz_pending, recovery_generation, session_reuse không labels email/token.

- [ ] Ghi AuthAuthorizationJourneyTests.LoginShareRevokeAndRedisOutage và QuotaBinJourneyTests.CrossDepartmentDeleteThenTenantPurge; kiểm tra permission revoke request tiếp theo denied, Redis down 503, recovery không revived session, soft delete giữ usage/purge giảm một lần.

Assertion chính (biến được arrange theo case và hợp đồng của task):

~~~csharp
Assert.Equal(HttpStatusCode.Forbidden, responseAfterShareRevocation.StatusCode);
Assert.Equal(originalUsedBytes - fileSize, usedBytesAfterPurge);
~~~

- [ ] Chạy đỏ nếu thiếu bất kỳ hành vi; nối toàn bộ fixture Kong/Auth/DMS/MySQL/Redis/storage stub. Thêm kiểm tra request denied không query bảng quyền, không storage call, không token/OTP/password trong captured logs.
- [ ] Chạy dotnet restore dotnet-clean-architecture-cqrs-starter.sln --locked-mode; dotnet build dotnet-clean-architecture-cqrs-starter.sln --no-restore; dotnet test dotnet-clean-architecture-cqrs-starter.sln --no-build; browser và compose fault test. Expected: 0 failed; không skip test bảo mật bắt buộc do dịch vụ chưa chạy.
- [ ] CI cung cấp secret/cert runtime và MySQL/Redis test, chạy cùng script; khóa dependencies bằng packages.lock.json từng project, image digest task 16; không commit file local secret.
- [ ] Cập nhật tài liệu “đã triển khai” chỉ theo kết quả thật; runbook có gate đóng/mở, pending reconcile, Redis rebuild rate, backup/restore và cutover. Liveness còn hoạt động khi security readiness đóng.
- [ ] Commit: test: validate and document the complete security flow.

## Lưu ý thực thi và tự review kế hoạch

- Dependencies mới được chọn bản tương thích net8.0, pin version cụ thể/lock khi tạo project, không tự nâng toàn bộ solution lên .NET 9. EF Application đang 8.0.11 và Infrastructure 8.0.25: đồng nhất 8.0.25 cho phần được sử dụng trước test migration; không dựa vào version SDK để đổi target.
- Test scaffolding tạo trong task cần deliverable, không task rỗng chỉ cài package. Unit/integration chưa chạy trong lượt viết plan.
- Mỗi task commit file được liệt kê; dùng git add đường dẫn cụ thể, không git add . và không stage README/root spec deletion có sẵn.
- Trước triển khai đọc lại AGENTS.md/ARCHITECTURE.md/SECURITY.md/spec; dùng worktree nếu cần isolation theo môi trường thực tế.
- Không tự triển khai/publish/cutover DB thật trong lượt viết kế hoạch.

| Mục spec | Task phủ |
|---|---|
| Hiện trạng, bốn tầng, ownership | 1, 2, 7, 11, 18 |
| Kong/danh tính/trust boundary | 12, 16 |
| Mật khẩu/Redis lock | 3, 9 |
| Google/proof/link | 10 |
| Token/cookie/refresh/logout | 8, 9, 17 |
| RBAC/PBAC/ReBAC | 1, 4, 11, 12, 14 |
| Redis-only/no fallback | 3–6, 12, 16 |
| DB/outbox/version/recovery | 2, 5, 6, 8 |
| Quota/bin/file ownership/log | 2, 13–15 |
| API errors/invitation/migration | 9–12, 18 |
| Kiểm chứng/triển khai/tài liệu | 16–19 |

Tự review: mỗi deliverable có file/contract/test; consumer dùng cùng UserId/sid, AuthorizationStamp, MutationReceipt; không có task đọc quyền DB trong request. Năm Review Focus đều có test chỉ định. Các đường dẫn migration sinh bởi EF và version package khóa lúc thực thi là giá trị do công cụ xác định, không phải chỗ trống nghiệp vụ.

## Handoff

Review kế hoạch trước thực thi. Đề xuất triển khai trực tiếp trong phiên (superpowers:executing-plans) vì các task phụ thuộc chặt vào contract/readiness/version; giảm bàn giao context. Có thể chọn subagent-driven-development nếu muốn review độc lập từng task. Chưa chọn phương thức và chưa bắt đầu triển khai.

Nguồn xác minh kỹ thuật: [Kong OSS 3.9.3](https://github.com/Kong/kong/releases/tag/3.9.3), [JWT plugin](https://developer.konghq.com/plugins/jwt/), [Redis Lua atomicity](https://redis.io/docs/latest/develop/programmability/eval-intro/), [ASP.NET Core certificate authentication](https://learn.microsoft.com/aspnet/core/security/authentication/certauth). Các nguồn hỗ trợ quyết định kỹ thuật; kết quả test thực tế là điều kiện mở hệ thống.
