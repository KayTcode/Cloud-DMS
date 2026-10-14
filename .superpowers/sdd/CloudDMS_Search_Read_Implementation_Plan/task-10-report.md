# Task 10 — Preview và API nội dung trích xuất

BASE: `dab3b69`. Đã viết mã production, chưa kiểm chứng runtime. Theo yêu cầu code trước: không viết/chạy test, integration, benchmark, migration, dịch vụ hay DB; giữ nguyên các bộ test có sẵn.

## Đã triển khai

- Command `RequestFilePreview`, query `GetFilePreview`/`GetFileText`, interface `IPreviewProvider` và ba endpoint `/api/files/{id}/preview` (POST/GET), `/api/files/{id}/text` (GET). Bắt buộc session/ACL hiện hành; response no-store, lỗi được rút gọn, request đánh dấu sensitive. Không trả storage key, hash, toàn văn qua log hoặc lỗi dependency.
- PDF, PNG/JPEG/GIF/WebP và text/plain đi qua read-access inline Task 4, cùng TTL mặc định 120 giây/tối đa 600 giây và kiểm origin riêng. Bổ sung nhận dạng raster bằng magic byte giới hạn trong extractor Task 7 để worker không đổi MIME ảnh sạch thành octet-stream; không decode/OCR, không tin extension, không nhận SVG/HTML. TXT/CSV/Markdown vẫn phải qua kiểm text của extractor. Kiểm current version/hash/source trước và sau cấp URL.
- Office chỉ DOCX/XLSX/PPTX có MIME đã xác minh, Clean, source version/key/provider/hash hiện hành và tối đa 100 MiB. Converter tắt mặc định; disabled/format/provider/input limit có reason unsupported. Không dùng viewer bên ngoài.
- Artifact intent có exact key `previews/{tenant}/{file}/{version}/{artifactId}.pdf` được ghi SQL trước upload. Cache duy nhất theo file/version/converter-version cộng SHA-256/format. Transaction khóa dòng FileEntry trước tạo intent/outbox, tránh insert sau purge. POST lặp lại dùng cùng intent/job; GET không tạo job. Pending trả 202; hết retry trả failed; Ready mới cấp URL riêng tư.
- Dùng `DocumentOutboxMessages` riêng của document, không đụng authorization outbox. Worker hiện có dispatch `PreviewRequested` tới processor mới, giữ nguyên claim CAS, lease 3 phút/gia hạn 30 giây/8 attempts/backoff trần 5 phút và advisory lock `dms-index:{fileId:N}`. Event chỉ chọn intent đã tồn tại, không tái tạo artifact bị purge xóa. Retry ghi cùng key khi chưa Ready; Ready là no-op. Không cần migration/schema mới.
- Worker đọc đúng object bất biến, tính đủ SHA-256/size qua stream giới hạn và deadline; service kiểm hash lần nữa. Kiểm lifecycle/scan/current source trước và sau conversion; trước upload khóa dòng lease và FileEntry đến khi upload + Ready commit. Không publish job cũ, mất lease hay file đã đổi/binned/purging. Physical write dùng private S3 ACL và key đã lưu; intent còn lại sau crash cho phép retry/purge dọn. Retention/purge Task 7 tiếp tục dọn artifact đúng version và chờ read TTL.
- Text API chỉ đọc SQL chunks của active generation hiện hành, Ready/Partial, cùng tenant/file/document version và pinned model; không extraction đồng bộ. Limit mặc định 20/tối đa 100; cursor Data Protection riêng chứa user/session/tenant/file/version/generation/model/ordinal/expiry, TTL 5 phút không gia hạn qua trang. Kiểm lại session/ACL và generation sau đọc chunks; generation thay đổi không trả text cũ; chưa index trả pending.

## Cô lập converter

`services/preview` là HTTP service Python chạy LibreOffice child riêng. Child chạy `prlimit` → `bubblewrap --unshare-all`, root mount tối thiểu read-only, user/PID/network namespace riêng, capability rỗng. BPF libseccomp được cài vào child và descendant, cấm socket/socketpair/connect/listen/accept/send/recv/socketcall và io_uring, cùng syscall mở rộng quyền/namespace. Parent vẫn vận chuyển HTTP được. Không có unsandboxed fallback hay Docker socket. Chi tiết syscall, package và prerequisites ở `services/preview/README.md`.

Profile riêng tắt macro/external updates; preflight OOXML loại VBA/ActiveX/embedded objects/external relationship và DTD/entity. Deadline preflight + child 60 giây; luôn kill process group, PID namespace/die-with-parent bảo vệ descendant. Một conversion/tối đa 8 transport threads; child address space 512 MiB/CPU 55 giây/output 32 MiB; compose cgroup toàn service 512 MiB/no swap, tmpfs job 256 MiB, read-only, non-root UID 10001, 1 CPU/64 PID. `/tmp` child dùng cùng job mount. Dọn crash directories lúc startup. Macro/crash/timeout/sandbox lỗi không tạo Ready.

Đây là cơ chế đã viết trong mã, chưa phải bằng chứng runtime. Kernel phải hỗ trợ unprivileged user/network/PID/mount namespaces và seccomp trên Linux x86_64/aarch64. Docker/AppArmor/seccomp host có thể chặn bwrap; khi đó conversion trả 503 và retry/terminal failure. Không bật privileged/bỏ sandbox để chạy được. LibreOffice có thể cần điều chỉnh giới hạn address-space sau đo thực tế; không tăng khỏi yêu cầu 512 MiB khi chưa có quyết định. Image/package chưa pin digest, cần chốt deployment trước production.

## Kiểm chứng mã

Đã dùng nguyên tắc `verification-before-completion`: chỉ khẳng định những gì có output. Lệnh cuối sau sửa C#:

`dotnet build src/CleanArchCqrs.API/CleanArchCqrs.API.csproj --no-restore --verbosity quiet`

Kết quả exit 0, build succeeded, 0 error/3 warning baseline (Hosting.Abstractions trùng và hai GoogleCredential obsolete). `git diff --check` không báo lỗi whitespace; chỉ warning đổi LF/CRLF theo môi trường Windows. Tự đọc đường enqueue/current source/hash/lease/publication, cursor/model/session, active MIME allowlist và sandbox fail-closed. Không tuyên bố test pass. Không chạy Python, image build, converter hoặc service.

## Files

- Application: `Common/Interfaces/IPreviewProvider.cs`; `Features/Files/Commands/RequestFilePreview/RequestFilePreviewCommand.cs`; `Features/Files/Queries/GetFilePreview/GetFilePreviewQuery.cs`; `Features/Files/Queries/GetFileText/GetFileTextQuery.cs`.
- Infrastructure: `Preview/{PreviewOptions,PreviewSource,PreviewProvider,PreviewConverterClient,PreviewJobProcessor,FileTextCursorProtector}.cs`; `DependencyInjection/DependencyInjection.cs`; `Services/Storage/S3StorageService.cs`; `Workers/DocumentIndexWorker.cs`; `Search/Extraction/FileTextExtractor.cs` (magic raster).
- API: `Controllers/FilesController.cs`, `Controllers/FilePreviewHttpResult.cs`.
- Deployment: `services/preview/{Dockerfile,server.py,README.md}`, `docker-compose.yml`; báo cáo này. Không đưa sửa đổi riêng của controller trong task-2-report/plan vào commit.

## Handoff Task 11 và test hoãn

- Options: `Preview:Enabled=false`, `Endpoint=http://127.0.0.1:8002/` (compose `http://preview:8002/`), `ConverterVersion=libreoffice-sandbox-v1` (1–48 ký tự ASCII letter/digit/dot/hyphen), phải trùng env `CONVERTER_VERSION`; đổi version khi image/profile đổi. `PREVIEW_ENABLED` mặc định false. Worker preview dùng `SearchIndex:Enabled`; không bật index worker thì job giữ pending. Không có viewer bên ngoài.
- Task 9 đã dùng Data Protection; cursor text dùng cùng key ring nhưng purpose riêng `CloudDMS.FileText.Cursor.v1`. Task 11 phải persist key ring bền vững và thống nhất app name/key ring giữa replica, bảo vệ key; không để restart/replica làm cursor mất hiệu lực ngoài dự kiến.
- Task 11 xác minh private bucket policy (ACL private không thắng public bucket policy), CORS/nosniff/origin riêng/HTTPS, TTL và khả năng provider; triển khai namespace/seccomp không đặc quyền theo README, không expose cổng converter ra ngoài. Chốt image/package digest và fonts/converter version. SQL migration không cần bổ sung cho Task 10.
- Sau Task 11: fixtures Office thật/PDF/raster/TXT; unsupported/SVG/HTML/macro/XXE/external/ZIP bomb/encrypted; hash/size mismatch; output/memory/tmpfs/process/60-second caps; chứng minh child không socket tới DB/Redis/Internet/loopback/AF_UNIX và không tồn tại descendant sau timeout/crash; thiếu namespace/seccomp phải fail closed.
- Sau Task 11: MySQL duplicate POST/job/CAS/lease expire/renew/crash sau upload trước Ready, advisory lock mất, purge/overwrite/bin/restore trong conversion/upload, không artifact xuất hiện sau purge cleanup; cleanup intent/old version/URL TTL. Thử retry không publish hai lần, Ready cache hit không convert lại, terminal retry status.
- Sau Task 11: text pagination 20/100, cursor tamper/expired/cross-user/session/tenant/file/version/generation/model/ordinal; revoke/session logout/security unavailable giữa truy vấn/polling/signing; model/generation đổi; no synchronous extraction; no text/object key/signed URL trong lỗi/log; output 202/404/409/422/503 và no-store. Cần đo tài nguyên/thời gian thực tế, không có tuyên bố benchmark.
