# Task 11 — tích hợp và vận hành production

## Kết quả

Đã triển khai code/deploy/docs trên `feat/search-read`, BASE `5e9c566`. Đọc brief/shared-context, handoff8/9/10, ARCHITECTURE/SECURITY và phần design/spec liên quan. Không tạo/chạy test, image, dịch vụ, DB, migration hoặc benchmark theo yêu cầu code trước; chỉ biên dịch API và review tĩnh. Không sửa/in `appsettings.Development.json`, không push/deploy/merge.

- Startup kiểm scope/DI, security Redis options, CORS origins, production AllowedHosts/HTTPS PublicApiOrigin, key ring/certificate/app isolation. Data Protection bền vững dùng namespace deployment chung replica, key ring mã hóa PFX/certificate at rest, kiểm private key/hạn cert/quyền ghi; thiếu cấu hình ngoài Development fail closed. Cert/key ring provision/backup/rotation được nêu rõ.
- Mặc định không tự migration/seed; opt-in `Database:InitializeOnStartup=true` migrate rồi seed và fail closed. Xóa startup DDL vá bảng/nuốt lỗi. Seeder mẫu vẫn cần quy trình provisioning được duyệt, chưa chạy startup thật.
- Trusted proxy chỉ For/Proto, một hop, peer IP cụ thể; không tin forwarded Host. CORS mặc định rỗng, API nosniff; lỗi chung/no-store, logging loại lỗi không exception payload. Limiter có options30search/10auth/10upload mỗi phút, giới hạn mỗi process được ghi rõ.
- Migration additive `20261014010000_RepairRoleTenantCompatibility`: thêm nullable TenantId/index composite/FK restrict vào Roles; giữ rows/IDs và unique Name cũ (bổ sung mapping tương ứng). Giữ Department physical legacy columns/default, shadow names snapshot khớp runtime. Bỏ Product phantom khỏi snapshot, không sinh DROP Products; không thay migration lịch sử. Down giữ schema additive để không mất dữ liệu.
- PDF native parser chỉ chạy Linux qua prlimit rồi bwrap user/PID/network/mount namespace riêng, cap-drop ALL, env sạch và bind chỉ runtime/input read-only. Giới hạn address-space512 MiB/CPU115 giây/core0/nofile128 kế thừa; timeout/cancel/output bound giữ nguyên. Thiếu sandbox hoặc namespace/parser lỗi trả metadata-only với reason rõ, không fallback direct parser. Runtime image thêm Poppler/bubblewrap/util-linux.
- Index worker cảnh báo disabled/coverage-unconfirmed, không giả ready. Preview mặc định false vì sandbox chưa được chứng minh trên host; transport/converter private. ClamAV mẫu25 MiB, AlertExceedsMax yes, coverage ackfalse; file upload50 GiB vẫn stored pending ngoài coverage, không đọc/clean index. Không giả coverage50 GiB.
- Production compose/env mẫu riêng: private backend, API loopback sau TLS proxy, secrets yêu cầu bên ngoài repo, Qdrant1.19.2 exact digest, E5 CPU/offline identity SHA pin, nonroot/read-only/caps/resources. MinIO Dockerfile source release/Go1.24.8 được copy từ ledger sang `deploy/minio`, không phụ thuộc đường ignored. Docker context loại ledger/worktree/tests/model/cache và development config.
- S3 Abort chỉ coi explicit NoSuchUpload idempotent; không nuốt arbitrary404. Bỏ duplicate Hosting.Abstractions8.0.0, giữ8.0.1. GoogleCredential obsolete giữ nguyên vì đổi credential loader cần xác minh SDK/format ngoài phạm vi tích hợp này.

## Files

`API/{Program.cs,ProductionSecurity.cs,Middleware/GlobalExceptionHandlerMiddleware.cs}`; `Application/Common/Behaviors/LoggingBehavior.cs`; `Infrastructure/{DependencyInjection/DependencyInjection.cs,CleanArchCqrs.Infrastructure.csproj,Persistence/Configurations/RoleConfiguration.cs,Migrations/20261014010000_RepairRoleTenantCompatibility.cs,Migrations/AppDbContextModelSnapshot.cs,Search/Extraction/FileTextExtractor.cs,Services/Storage/S3StorageService.cs,Workers/DocumentIndexWorker.cs}`; `Dockerfile`, `.dockerignore`; `deploy/{compose.search-read.yml,search-read.env.example,minio/Dockerfile,clamav/clamd.conf}`; `docs/search-read-runbook.md`, `ARCHITECTURE.md`, `SECURITY.md`; plan append của controller về code-first/ClamAV và report này. Không đưa `task-2-report.md` vào commit.

## Kiểm chứng đã thực hiện

`dotnet build src/CleanArchCqrs.API/CleanArchCqrs.API.csproj --no-restore --verbosity quiet`: hai lần khi code đổi; lần cuối exit0, **0 errors/2 warnings**,2,18 giây. Hai CS0618 baseline tại GoogleDriveStorageService35/62 về GoogleCredential.FromStream; NU1504 duplicate package đã loại bỏ. Không gọi dotnet run/test/ef hoặc database.

`git diff --check`: exit0, chỉ cảnh báo Git LF/CRLF. Tự đọc diff paths sandbox/timeout/exit, startup fail closed, migration không drop/copy dữ liệu, option/env thực tế và secret logging. Compile không chứng minh DI resolve/startup/SQL/sandbox runtime.

## Ma trận và runbook

Ma trận đầy đủ tên key/default/range có trong `docs/search-read-runbook.md` mục Ma trận cấu hình; env dùng `__`. Bao gồm DP app/key/cert, Database initialize, Redis, ReadAccess TTL/public origin, AllowedHosts/CORS/trusted peers, SearchIndex scan/analysis/tokenizer/PDF, Embedding/Qdrant, Preview, ClamAV, Upload và RateLimiting. Phân biệt giá trị configurable với giới hạn hardcode search/analysis. Runbook mô tả setup model/MinIO tái lập, SQL review/apply trên bản sao, bucket private/Range/CORS/nosniff, proxy/APM scrub query, chuyển client, TTL/revoke, retry/dead-letter/reconcile và rollback generation. Sửa cutoff48 giờ lỗi thời thành fair revisit vô thời hạn đúng code.

## Rủi ro và nghĩa vụ hoãn

- Chưa có runtime/image/HTTP/DB test. Bắt buộc DB sạch/upgrade/seed/Role materialization/name uniqueness/FK/quota, DI lifetime, startup missing config/cert/permission/Redis; HTTP401/404/409/202/422/429/503/no-store/nosniff và regression login/refresh/upload/delete/restore/download sau khi code xong.
- Cần chứng minh PDF bwrap không ra DB/Redis/Internet/loopback/AF_UNIX, parser/descendants bị dọn khi cancel/timeout; Office sandbox/seccomp, bombs/macro/XXE/external links, memory/output/deadline. Host Docker/AppArmor có thể chặn unprivileged namespaces: giữ fail closed, tuyệt đối không privileged/unconfined. PDF có network namespace và runtime seccomp kế thừa, không có BPF socket-deny riêng như converter; memory512 MiB là address-space prlimit, cgroup PDF riêng chưa có (toàn API2 GiB).
- Qdrant digest pin đã có; full image/package digest lock/SBOM (API/base/converter/embedding/MinIO/ClamAV) chưa hoàn tất. ClamAV image digest do deployer cấp, signatures backend phải preload/mirror; không bịa digest chưa resolve.
- DP certificate rotation chưa tự động; provision `/keys` writable UID1654 và certificate đọc riêng. Host trusted proxy IP và public storage origin/bucket policy cần chỉnh theo deployment, không dùng ví dụ DNS/IP nguyên trạng.
- ClamAV coverage ackfalse mặc định nên tài liệu mới chưa sẵn sàng đọc; chỉ bật sau test coverage daemon/limits thật, luôn<2 GiB. File lớn pending là lựa chọn người dùng, không hứa read50 GiB.
- Startup không tự sửa DB cũ; DB vá tay phải reconcile schema/history trước migrate. Migration Down cố ý không tháo cột/index/FK, rollback app giữ schema. AES hiệu lực cũ phải giữ trước quy trình tái mã hóa.
- Rate limit mỗi process chưa distributed; chưa benchmark resource/latency/Recall. Test full deferred của Task4–10 và Task12 phải thực hiện sau code complete; không có kết quả pass/runtime readiness để công bố.

## Sửa review Task11 — hai góp ý nhỏ

Đã đối chiếu `task-11-review.md` và consumer AES/ProductionSecurity: comment env đổi thành `Encryption__SecretKey`, nhắc giữ khóa hiệu lực cũ khi nâng cấp trước quy trình tái mã hóa. Bỏ đúng block Role.Tenant lặp trong snapshot, giữ mapping gốc và toàn bộ schema/migration. Hai warning GoogleCredential baseline giữ theo dõi riêng.

Chỉ sửa comment/snapshot/report; `git diff --check` đạt, không build lại, không chạy test/runtime/image/service/DB/migration theo yêu cầu. Các nghĩa vụ kiểm thử và giới hạn đã ghi ở trên giữ nguyên.
