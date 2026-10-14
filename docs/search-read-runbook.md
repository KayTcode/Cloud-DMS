# Vận hành search/read Release 1

Đã viết mã và kiểm tra biên dịch API; chưa chạy image, dịch vụ, DB, migration, HTTP regression hoặc benchmark theo yêu cầu làm code trước. Các bước dưới đây là quy trình dự kiến, chỉ thực hiện trong giai đoạn kiểm thử/triển khai được cho phép. Không coi container đang chạy là bằng chứng chức năng sẵn sàng.

## Chuẩn bị

Dùng `deploy/compose.search-read.yml` riêng; compose ở root là mẫu phát triển cũ, không dùng cho production. Cấp biến compose qua secret store: `CLOUD_DMS_ENV_FILE`, `DATA_PROTECTION_CERTIFICATE_FILE`, `MYSQL_ROOT_PASSWORD`, `REDIS_PASSWORD`, `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`, `QDRANT_API_KEY`, `CLAMAV_IMAGE` (image đã duyệt, pin digest). File env bí mật phải ở ngoài repository; bắt đầu từ `deploy/search-read.env.example`. Không chạy `docker compose config` lên log vì có thể render secrets. MySQL/Redis/ClamAV/Qdrant/embedding/converter/MinIO không publish cổng. API chỉ bind loopback, đặt sau reverse proxy TLS; cần thay binding khi proxy nằm trên host khác. Các node cùng backend vẫn thấy transport nội bộ; dùng network policy bổ sung khi cần tách service. API có egress cho storage/SMTP/identity; model và converter không có egress.

Cấp `ConnectionStrings__DefaultConnection` bằng user ứng dụng riêng, `SecurityRedis__ConnectionString`, bộ `JwtSettings__{SecretKey,Issuer,Audience}`, `Encryption__SecretKey`, `Qdrant__ApiKey`, `DataProtection__CertificatePassword` và SMTP/provider credentials. Không đổi khóa AES hiệu lực của kho cũ trước khi có quy trình tái mã hóa cấu hình provider. Không dùng khóa JWT thay khóa encryption mới. Giữ issuer/audience đúng client cũ. Redis dùng AOF always/noeviction và password; không public. Chuẩn bị backup và restore drill cho SQL, object, key ring/certificate và Redis bảo mật.

Data Protection bắt buộc ngoài Development: `DataProtection:ApplicationName` riêng cho deployment, `KeyRingPath` bền vững, `CertificatePath` PFX có private key và `CertificatePassword` từ secret store. App name, key ring và certificate giải mã phải thống nhất giữa replica; không dùng chung namespace/key ring với ứng dụng hoặc môi trường khác. Mount certificate read-only, chỉ UID ứng dụng được đọc; provision volume `/keys` cho UID runtime .NET (1654) ghi, quyền 0700. API kiểm ghi key ring khi khởi động; thiếu/sai cấu hình phải dừng. Khóa XML được mã hóa bằng certificate, không chỉ dựa vào quyền file. Sao lưu certificate/private key và key ring cùng nhau, giữ cert cũ trong kế hoạch rotation trước khi retire key; hiện không có tự động xoay certificate. Development không persist là ngoại lệ rõ ràng, không dùng cho nhiều replica.

## Build tài nguyên offline

Python 3.11 thật (Windows Store alias không đủ): `python services/embedding/preload.py` tải đúng revision `614241f622f53c4eeff9890bdc4f31cfecc418b3` và kiểm SHA256 từng file trong manifest. Hoặc `python services/embedding/preload.py --cache <thư-mục-cache-đã-cấp>`; không cần ledger `.superpowers`. Sau đó build service embedding bằng Dockerfile hiện có. Không commit weights. Runtime `HF_HUB_OFFLINE=1`, `TRANSFORMERS_OFFLINE=1`, tokenizer/model cục bộ, safetensors; không remote code/download. Contract `/chunk` và `/embed` cùng model identity, prefix do service thêm, 384 chiều/L2, tối đa 512 token bao gồm prefix/special.

MinIO tái lập từ source chính thức: `docker build -t cloud-dms-minio-tests:2025-10-15 -f deploy/minio/Dockerfile deploy/minio`. Dockerfile dùng Go 1.24.8, module release `RELEASE.2025-10-15T17-29-55Z`; compose production build trực tiếp `./minio`, không phụ thuộc image bên ngoài không tồn tại. Build cần Internet; chưa build lại trong Task11. Qdrant pin `v1.19.2@sha256:b7b0444c4c351c970b98e90a6f89c2ee4287c65b44e52b4cb503fa5b2aa927ad`. Các base/package khác còn pin version/tag, chưa chốt digest toàn chuỗi; pipeline release phải resolve/duyệt digest và lưu SBOM/artifact, không tuyên bố supply chain hoàn tất.

## Schema và khởi động

`Database:InitializeOnStartup=false` mặc định: API không tự migration/seed, không chạy DDL vá bảng. Đây là thay đổi hành vi so với startup cũ. Opt-in true chỉ dành cho DB kiểm thử/initial provisioning đã kiểm soát: migrate rồi seed, lỗi làm startup thất bại; seeder có tài khoản mẫu, không bật trên production trước khi rà soát danh tính/mật khẩu. Không có cơ chế nuốt lỗi để giả khởi động thành công.

Trên bản sao DB: backup trước; dùng `dotnet ef migrations script --idempotent --project src/CleanArchCqrs.Infrastructure --startup-project src/CleanArchCqrs.API` để xuất SQL vào artifact kín; review rồi áp dụng bằng tài khoản migration riêng. Sau đó kiểm migration history/schema, seed bằng quy trình provisioning được duyệt và khởi động API với initialize=false. Kiểm cả DB sạch từ đầu và DB upgrade; không dùng EnsureCreated thay migration. Không thực hiện các lệnh này trong Task11.

Migration `20261014010000_RepairRoleTenantCompatibility` chỉ thêm nullable `Roles.TenantId`, index composite/FK restrict và default hai cột quota cũ; giữ role ID/row và unique `IX_Roles_Name`. Lịch sử migration giữ nguyên. Snapshot bỏ Product phantom vì runtime không map; không tạo DROP Products, không động bảng Products nếu còn tồn tại. Department dùng shadow `LegacyStorageQuotaBytes/LegacyStorageUsedBytes` trỏ đúng cột vật lý cũ, giữ dữ liệu; canonical `QuotaBytes/UsedBytes` là nguồn nghiệp vụ. Down của migration sửa tương thích cố ý giữ cột/index/FK để tránh mất dữ liệu: rollback app thay vì rollback schema. DB đã được vá tay phải đối chiếu schema/history trước, không chạy mù migration rồi bỏ qua duplicate column.

## Ma trận cấu hình

Tên dưới đây là key .NET; env thay `:` bằng `__`.

| Key | Mặc định/giới hạn và ý nghĩa |
|---|---|
| `ReadAccess:PublicApiOrigin` | Origin API tin cậy, HTTPS ngoài Development, không lấy Request.Host; inline fail closed khi thiếu; phải khác origin nội dung storage |
| `ReadAccess:UrlTtlSeconds` | 120, 1–600 giây |
| `AllowedHosts`, `Cors:AllowedOrigins` | Host cụ thể, không wildcard ngoài Development; CORS rỗng mặc định, chỉ origin HTTP(S), production HTTPS |
| `ReverseProxy:KnownProxies` | Mảng IP proxy thật; không cấu hình thì bỏ qua forwarded headers; chỉ X-Forwarded-For/Proto, 1 hop, không tin forwarded Host |
| `DataProtection:ApplicationName/KeyRingPath/CertificatePath/CertificatePassword` | Namespace deployment + key ring chung bền vững + PFX/private key; password secret |
| `Database:InitializeOnStartup` | false, opt-in migrate+seed, lỗi không bị bỏ qua |
| `SecurityRedis:ConnectionString` | Bắt buộc, secret; command 1000 ms/connect 2000 ms, mỗi loại 1–30000; circuit threshold 3/open 5000 ms |
| `SearchIndex:Enabled` | true; false dừng indexing và preview processing, cảnh báo rõ, không giả ready |
| `SearchIndex:MaxScanBytes/ClamAvCoverageConfirmed` | 26214400/false, bytes >0 và <2147483648; false giữ chờ xác minh |
| `SearchIndex:ScanTimeoutSeconds/RetentionMinutes` | 3600 (1–86400)/60 (>=10), scan deadline và giữ generation cũ |
| `SearchIndex:TokenizerEndpoint/ModelVersion/PdfTextExecutable` | HTTP(S) endpoint nội bộ kết thúc `/`; model identity pin E5; `/usr/bin/pdftotext` trên Linux image |
| `Embedding:Endpoint/ModelVersion/BatchSize/TimeoutSeconds` | `http://embedding:8001/`, identity pin E5, 16 (1–16), 120 (1–120) |
| `Qdrant:Endpoint/ApiKey/TimeoutSeconds` | `http://qdrant:6333/`, key secret trùng server, 120 (1–120); không expose public |
| `Preview:Enabled/Endpoint/ConverterVersion` | false, `http://preview:8002/`, `libreoffice-sandbox-v1`; version phải trùng service, thay khi image/profile đổi |
| `ClamAV:Enabled/Host/Port/TimeoutSeconds` | Phải bật mới quét; `clamav`/3310/3600 trong mẫu; không quét hoặc lỗi không coi Clean |
| `Upload:MaxStorageBytes/MaxStagingBytesPerProvider` | 50 GiB/200 GiB, riêng logical quota và inflight physical staging |
| `RateLimiting:SearchPerMinute/AuthPerMinute/UploadPerMinute` | 30/10/10, mỗi key 1–10000; limiter theo process, không global giữa replica |

Search còn giới hạn trong mã: hybrid mặc định, RRF k60, page20/max100, query1000 ký tự, candidate1000/refill5, deadline10 giây, cursor/session5 phút; cần đổi mã nếu đổi các giá trị này. Analysis tối đa100 MiB,1000 trang/sheet,2000 chunk,120 giây. Không gán các giới hạn này cho upload storage.

## ClamAV và file lớn

Người dùng đã chọn giữ ClamAV, cho phép lưu tới50 GiB nhưng file ngoài coverage ở trạng thái chờ xác minh, không read/download/preview/clean index. Không tuyên bố ClamAV quét50 GiB; giới hạn kỹ thuật khoảng2 GiB và cấu hình hiện tại nhỏ hơn. Mẫu `deploy/clamav/clamd.conf`: StreamMaxLength25M, MaxFileSize25M, MaxScanSize100M, MaxRecursion16, MaxFiles10000, AlertExceedsMax yes. Limit archive có thể trả không xác minh dù file dưới25 MiB; không chuyển thành Clean. Service image cần đúng đường config và signatures hợp lệ; cập nhật signatures offline định kỳ hoặc qua mirror được duyệt vì mạng backend không có Internet. Không bật log tên/nội dung tài liệu.

Giữ `ClamAvCoverageConfirmed=false` cho đến khi test daemon thực tế với bộ file sát giới hạn, nested archive và AlertExceedsMax chứng minh không false-clean. Chỉ tăng coverage cùng StreamMaxLength/MaxFileSize/MaxScanSize và resource budget đã kiểm chứng, luôn<2 GiB; không tăng chỉ một option API. File vượt coverage ghi lý do ổn định, không lặp scan tốn kém; khi đổi coverage key, reconciliation xét lại. Đây là giới hạn read lớn đã được chấp nhận, không phải lỗi storage.

## Parser và converter cô lập

API runtime image cài Poppler và bubblewrap. PDF chỉ chạy Linux qua `bwrap --unshare-all`: user/PID/network/mount namespace riêng, chỉ bind read-only `/usr`, libraries và đúng input PDF, tmpfs riêng, env sạch; không mount `/run`, credentials, DB socket hay API source. Không có nhánh fallback chạy pdftotext trực tiếp. Thiếu Linux/bwrap báo `PDF_SANDBOX_UNAVAILABLE`; namespace/parser bị chặn báo `PDF_SANDBOX_OR_PARSER_FAILED`, metadata-only, không tin MIME từ phần mở rộng. Child có timeout/cancellation/watchdog memory512 MiB; hard cgroup budget là toàn API2 GiB, chưa có per-child cgroup. Output giới hạn16M ký tự; stderr drain nhưng không log. Windows dev giữ metadata-only cho PDF, chỉ đổi executable không loại bỏ yêu cầu sandbox Linux.

Preview mặc định false vì sandbox chưa kiểm chứng trên host thật; bật cả compose profile `preview` và `PREVIEW_ENABLED=true` sau test. LibreOffice child dùng bwrap namespaces, prlimit, libseccomp BPF chặn sockets/escape, UID10001, tmpfs/job256 MiB, memory512 MiB,1 CPU/64 PID, output32 MiB và deadline60 giây (transport65 giây). Transport HTTP vẫn có mạng riêng; parser child không có mạng. Host Linux x86_64/aarch64 cần unprivileged user namespaces, namespace mounts và cài seccomp; Docker/AppArmor mặc định có thể chặn. Khi không đáp ứng, giữ disabled/metadata-only hoặc failed, không thêm privileged, SYS_ADMIN, Docker socket hay seccomp unconfined để vượt qua. PDF chỉ dùng network namespace và seccomp kế thừa runtime; không tuyên bố có BPF socket-deny riêng như converter. Chứng minh isolation/limits/kill descendant là nghĩa vụ test còn mở.

## Object Storage, proxy và client

Tạo bucket private và policy không public (ACL private không vô hiệu public policy). Provider S3 phải dùng endpoint có thể truy cập từ browser tại origin HTTPS riêng; DNS/TLS/proxy cho endpoint giữ nguyên Host/path/query đã ký, không rewrite làm hỏng chữ ký. Chỉ cấp credentials hạn chế bucket/prefix cần thiết. Không đưa object key vào response search.

CORS bucket chỉ origin frontend thực tế, GET/HEAD, request Range; expose Accept-Ranges, Content-Range, Content-Length, Content-Type, Content-Disposition, ETag, Last-Modified; kiểm GET206/HEAD và URL hết hạn trên provider thật. Chặn credentialed wildcard. Proxy nội dung thêm `X-Content-Type-Options: nosniff`, không cache URL đã ký công khai; không proxy inline qua API cùng origin. Không nhúng SVG/HTML/Office chưa convert. Request byte Range đi thẳng storage; legacy download trả byte vẫn tồn tại để chuyển client nhưng chịu ACL/scan mới. Provider Local/Drive/OneDrive không có direct URL capability, không giả lập thành công.

Proxy API chỉ tin peer đã cấu hình, terminate TLS và ghi log `$uri` (không `$request`, `$request_uri`, `$args`), không ghi Authorization/Cookie/body, query q/cursor hay signed URL; áp dụng tương tự CDN/WAF/APM/trace/error collector. Không bật ASP.NET HTTP logging hay EF sensitive data logging. Filter hosting diagnostics Warning đã chặn request-start query log; exception handler/logging behavior chỉ log loại lỗi/tên request, phản hồi lỗi chung/no-store. Không gửi full text/embedding sang AI/viewer ngoài. Ví dụ nginx cần TLS/server names/certificates riêng:

```nginx
log_format dms '$remote_addr $request_method $uri $status';
access_log /var/log/nginx/dms.log dms;
# Trong server TLS cho API:
location / {
  proxy_pass http://127.0.0.1:8080;
  proxy_set_header Host api.example.invalid;
  proxy_set_header X-Forwarded-For $remote_addr;
  proxy_set_header X-Forwarded-Proto https;
}
```

Limiter search30/user/phút, auth10/IP/phút, upload10/user/phút (fallbackIP) nằm mỗi process. Nhiều replica nhân quota hiệu lực; cần distributed limiter hoặc gateway policy tương đương trước khi scale, không coi sticky session là bảo đảm global.

Client mới xin read-access và tôn trọng purpose/MIME/Range/expiry, xin lại khi hết hạn; polling preview202 và text pagination phải kiểm version/generation. Revoke/logout chặn cấp URL mới và search; URL đã cấp có thể dùng đến TTL120 giây, request đang truyền có thể tiếp tục theo provider. Không hứa thu hồi tức thì; nếu cần phải có gateway kiểm quyền từng request ngoài Release1.

## Retry, reconciliation và rollback

DocumentOutboxMessages riêng với outbox authorization; lease3 phút, tối đa8 lần, exponential backoff cap5 phút, reconciliation30 phút. Kiểm failed bằng FailureCode đã scrub, không lấy exception/provider response làm log. Khi xử lý nguyên nhân xong, operator được duyệt chỉ reset exact event failed về Pending/AttemptCount0/NextAttemptAt hiện tại, xóa lease cũ sau khi chắc worker trước đã kết thúc; giữ tenant/file/version/generation. Không reset toàn bảng, không sửa Clean/Ready hoặc published generation bằng tay. Chạy reconcile qua lịch worker, không có HTTP admin retry/reconcile endpoint được triển khai. Preview job dùng cùng worker.

Multipart completion lease30 phút, recovery5 phút; legacy intent persist trước object. Terminal intent/manifest được revisited công bằng vô thời hạn để bắt object đến muộn, KHÔNG có cutoff48 giờ nữa. Staging quota riêng logical quota; không double-decrement khi cleanup. Cần giám sát orphan/staging growth và retry lâu ngày. Retention version cũ không thay logical quota.

Rollback app giữ schema additive; dừng worker trước khi thay đổi generation/model/converter. MySQL current document/generation/model và ACL là nguồn chuẩn; Qdrant cũ không được publish ngược. Model mới hiện bị options pin từ chối: cần release đồng bộ producer, tokenizer, manifest và identity; stage generation mới rồi CAS publish, giữ generation cũ trong retention. Muốn quay về generation cũ phải tái tạo event/version hợp lệ qua quy trình được duyệt, không đổi current pointer qua SQL tùy tiện. Xóa vector chỉ exact tenant/file/generation trong namespace, không drop collection/bucket. Không xóa key ring khi rollback/restart.

## Kiểm thử hoãn bắt buộc

DB sạch/upgrade/migration SQL không DROP bảng/cột cũ, role seed/materialize/FK/name uniqueness và department quota reload; startup thiếu options/cert/key permission/Redis fail closed, scope DI và multi-replica cursor; HTTP401/404/409/202/422/429/503,no-store/nosniff/CORS/Host spoofing/proxy spoofing/scrubbed log; login/refresh/logout/upload/delete/restore/legacy download; storage private/presigned TTL/Range/cross-origin; ClamAV infected/limit/archive/truncated replies/coverage; indexing stale generation/CAS/retry/dead-letter/fair cleanup; model offline hash/token boundary/query timeout/degraded/cursor tamper; PDF/Office bombs/XXE/macro/external links/no-network/AF_UNIX/descendants/memory/output/deadline; resource/latency/Recall benchmark sau cùng. Chưa có kết quả runtime/benchmark để công bố.
