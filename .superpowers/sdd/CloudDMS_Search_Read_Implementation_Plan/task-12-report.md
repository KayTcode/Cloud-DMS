# Task 12 — nhật ký kiểm chứng

BASE: 434bd7f; nhánh feat/search-read. Chỉ tài nguyên test riêng, chưa merge/publish.

## Bằng chứng đang thực hiện

- `dotnet build dotnet-clean-architecture-cqrs-starter.sln --no-restore`: RED 2 lỗi constructor fixture upload; sau sửa constructor GREEN 0 warning/0 error (incremental).
- `dotnet test tests/CloudDms.UnitTests/CloudDms.UnitTests.csproj --no-restore --verbosity quiet`: 38 pass.
- Legacy lần đầu: 5 lỗi constructor upload/download; sau cập nhật còn lỗi dependency AWSSDK.S3 do assets cũ. `dotnet restore tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj --verbosity quiet` giải quyết assets. Sau cập nhật ACL/session và assertion: 47 pass/0 fail/0 skip.
- Integration RED: 25 fail/17 pass do migration AddMultipartSessionState tạo LegacyUploadIntents GUID không có ascii_general_ci, FK Tenants.Id incompatible. Sửa toàn bộ GUID mới cùng collation model/parent; đang chạy lại.
- Full integration sau migration: vượt bước migration nhưng quota fixture cũ bắt đầu upload mới trong lúc purge global security pending; bị từ chối đúng failclosed. Cần điều chỉnh phép thử quota writer để không giả định authorization vượt pending.
- `docker build -t cloud-dms-embedding-task12 services/embedding`: thành công, CPU torch pinned tải từ requirements.lock; model local có thật, không commit model.
- `docker build -t cloud-dms-preview-task12 services/preview`: thành công.
- `docker run --rm --cpus 1 --memory 512m --pids-limit 64 --network none cloud-dms-preview-task12 bwrap --unshare-user --unshare-net --unshare-pid --ro-bind / / -- true`: exit 1, No permissions to create new namespace. Không dùng privileged/unconfined. Conversion/layout thành công chưa kiểm chứng.

Log tạm cục bộ: `%TEMP%/cloud-dms-task12-*.log`; không chứa credentials/signed URL.

## Cập nhật runtime

- Focused GREEN: 22/22 (migration sạch/nâng cấp, keyword SQL thực, quota race/rollback, extraction/RRF). RED keyword: Pomelo không translate GroupBy/ordered First; sửa aggregate max-score join rồi min ordinal CHỈ trong tập best-score. Thêm kiểm best matching ordinal thực.
- Strict BOM GREEN 13/13: UTF8 có/không BOM, UTF16LE/BE, UTF32LE/BE và byte lẻ/surrogate/UTF8 hỏng. RED ban đầu StreamReader tự thay strict decoder khi nhận BOM, tạo replacement text; sửa chọn decoder strict theo BOM.
- MinIO multipart/private/Range/resume/URL hết hạn/abort GREEN. RED signed PUT dùng HTTPS mặc dù endpoint loopback HTTP; sửa Protocol lấy scheme endpoint đã validate, GET tương tự. Không thay khả năng endpoint ngoài loopback.
- Core GREEN 11/11: actual E5/Qdrant/MySQL/Redis/S3 full search handler; query cursors, text read, content replacement, session revoke; worker claim/fairness/coverage pending/exhausted lease thực. Peer ClamAV protocol chỉ kiểm adapter, KHÔNG là engine AV thực.
- 50 query x3 mode, corpus10 tài liệu, concurrency1; upload/download/extract/tokenizer/embed/vector stage thực nhưng scan và SQL publication generation seed tin cậy. Lần đầu 2026-10-14T04:26Z: index-stage1487.62ms; keyword Recall@5 .96, nDCG@10 .935237, p95103.9198ms; semantic .90/.892619/136.3739ms; hybrid1.00/.936944/133.6873ms. Không suy thành chất lượng/tải production.
- Hardware: Windows; Intel Core i7-13650HX,14 core/20 logical; Docker Desktop Linux20CPU,12428939264byteRAM; E5 container2CPU/1200MiB. API .NET8.0.23, SDK9.0.309. Mẫu CPU/RAM bổ sung trong lượt cuối; không bịa peak/CPU-seconds.
- API host Production riêng đã khởi động với PFX/keyring/secrets/database test riêng; login runtime đang trả500, đang chẩn đoán exception type qua log đã scrub, chưa coi đạt.

## Sửa HTTP/transaction và kiểm tra tập trung cuối

- API Production fixture dùng PFX/keyring và secrets sinh mới trong thư mục tạm, DB/Redis/MinIO riêng. Các lỗi fixture password>20, JSON RefreshTokenValue và dùng Bearer client cho signed storage URL đã được sửa, KHÔNG gắn nhãn production bug.
- Validation RED: `dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj --filter FullyQualifiedName~ApiRuntimeIntegrationTests --no-restore --verbosity minimal` → invalid login21 ký tự expected400/actual500. Fix middleware chỉ FluentValidation.ValidationException→400, chi tiết chung/no-store; không blanket ArgumentException.
- Multipart RED: cùng API host, Begin trả400 vì explicit transaction ngoài retry execution strategy đang bật trong DI. Fix SQL-only Begin/Abort/recovery; external S3 Begin/abort/delete không đặt trong retry SQL. Begin giữ ID session server-generated và kiểm row đã commit trước retry; Abort/recovery CAS trạng thái để không refund lặp.
- API cuối focused GREEN1/1: `dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj --filter FullyQualifiedName~ApiRuntimeIntegrationTests --no-restore --verbosity minimal`, log `%TEMP%/cloud-dms-task12-api-verified.log`,5 giây scenario (fixture startup ngoài duration này). Bao gồm login/refresh, read-access+actualS3GET, legacydownload, keywordHTTP,401/400/404/429/503,no-store/nosniff; multipart Begin→presignedPUT→Complete202 Pending/unreadable; actual MySQL post-commit TimeoutException injection/retry/replay/quota once/Abort once; retry-enabled recovery expiry + actual legacy orphan deletion.
- Converter transport focused2 tests run: API scenario lúc đó chưa hoàn thành, PreviewTransport pass với observed503; không coi toàn lệnh pass. Full run sau sửa fixture xác minh lại.
- Đã dùng `dotnet format whitespace dotnet-clean-architecture-cqrs-starter.sln --include src/CleanArchCqrs.Application/Features/Files/Commands/BeginMultipartUpload/MultipartUploadService.cs src/CleanArchCqrs.Infrastructure/Workers/MultipartUploadRecoveryWorker.cs --no-restore --verbosity quiet` chỉ hai file transaction.

## Lượt full cuối (sau mọi thay đổi code)

- `dotnet build dotnet-clean-architecture-cqrs-starter.sln --no-restore --verbosity minimal`:0 lỗi,2 warning GoogleCredential obsolete có sẵn.
- `dotnet build tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj --no-restore --verbosity minimal`: thành công.
- `dotnet test tests/CloudDms.UnitTests/CloudDms.UnitTests.csproj --no-build --verbosity minimal`:38 pass/0fail/0skip.
- `dotnet test tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj --no-build --verbosity minimal`:47 pass/0fail/0skip.
- `dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj --no-build --verbosity minimal`:81 pass/0fail/0skip,3 phút55 giây. Tổng166/166 pass.

## Artifact và phạm vi sửa

Production: middleware validation mapping; multipart SQL execution strategy/commit verification/recovery; GUID collation migration; strict BOM decoder; keyword aggregate translation; S3 presign scheme. Không đổi kiến trúc bốn tầng hoặc đưa dependency ngược.

Tests: nâng fixture legacy constructor/session/ACL và quota SQL writer; thêm Files/*Regression/Integration/EndToEnd tests, SearchServicesFixture và RedisFixture endpoint; integration reference API để build host thật. Corpus50 query và benchmark runner/results; tài liệu docs/search-read-validation.md cùng trạng thái kiểm chứng ARCHITECTURE.md/SECURITY.md. Không sửa/commit task-2-report.md đang modified từ trước.

Full report nghiệm thu theo AC, metric cuối, hardware, điều kiện chạy lại và inventory chưa kiểm nằm tại docs/search-read-validation.md. Cần whole-branch review sau handoff; không coi namespace failclosed hoặc peer ClamAV là thành công conversion/AV engine. Không merge/publish.

## Kết luận handoff

DONE_WITH_CONCERNS: mã/test/artifact đã kiểm và giữ nhánh review; không tuyên bố đạt mọi AC hoặc production ready. Benchmark cuối trong tools/search-read-benchmark/results-2026-10-14.json; E5 mẫu CPU63,46%,RAM522,6MiB; index-stage1355,4588ms; keyword/semantic/hybrid p9592,1843/133,2940/122,4237ms. Namespace blocked, ClamAV engine/pipeline clean full, model migration/load/chaos còn chưa nghiệm thu. Toàn bộ lý do và AC matrix ở validation doc.
