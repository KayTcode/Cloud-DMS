# Kiểm chứng search/read — 14/10/2026

## Phạm vi đã thực hiện

Dùng .NET8 trên Windows, Docker Desktop Linux; chỉ database/container/cổng loopback và credentials test riêng. Không migration/seed DB người dùng, deploy, push hoặc merge. Build production và test cũ được thực hiện sau khi Tasks1–11 hoàn tất theo thứ tự người dùng yêu cầu.

- Schema: MySQL8.4 thật, chuỗi migration sạch và nâng cấp dữ liệu legacy; giữ IDs/rows/quota, FK/uniqueness version/generation/cache.
- ACL/session: unit và test cũ; MySQL/Redis thật với tenant/team/grant kế thừa/hết hạn/thu hồi/pending; batch520 tài liệu; login/refresh và session bị khóa, Redis unavailable.
- Keyword: SQL thực với tiếng Việt, từ ngắn, `%`/`_`, chuỗi giống SQL injection, không lộ tenant khác hoặc phiên bản pending. Chọn đúng chunk có score cao nhất rồi ordinal nhỏ nhất trong tập đồng điểm.
- Search: E5 CPU offline từ assets đúng manifest, tokenizer thật, Qdrant1.19.2 digest pin, SQL/ACL/Redis thật, cả ba mode; RRF/dedup, cursor replay/tamper/query/user/expiry, stale version, text pagination20/100 và cursor generation/user, pause E5 → hybrid degraded/semantic unavailable.
- Read/storage: MinIO thật; presigned multipart PUT/complete/abort, bucket private, URL hết hạn, HTTP Range/resume, direct-read handler qua HTTP và legacy download. Upload hoàn tất vẫn Pending, không read/index sạch khi chưa xác minh.
- Multipart/quota: API Production với retry MySQL bật; Begin→PUT→Complete202, replay reservation, inject TimeoutException SAU commit MySQL thật để chứng minh không trừ quota hai lần, Abort replay, recovery hết hạn và xóa legacy orphan. SQL writer race upload/purge ở cả hai thứ tự commit, cùng/khác tenant; rollback quota cuối chuỗi.
- Outbox: advisory lock file bận không tiêu attempt/lease hoặc làm đói file kế; exhausted lease terminal; coverage chưa xác nhận giữ Pending/không ACL đọc, owner lease sai bị chặn.
- Extraction: TXT/CSV/Markdown, DOCX text/XXE/ZIP duplicate, từ chối HTML/SVG/binary, strict UTF8/16/32 BOM và dữ liệu hỏng. PDF trên Windows không chạy parser không sandbox.
- HTTP Production: PFX/keyring/secrets riêng, startup DI, login/refresh,401/400/404/429/503, no-store/nosniff. Luồng read-access và legacy giữ response hợp đồng hiện hữu; không khẳng định mọi endpoint lỗi đã thống nhất mã trạng thái.
- Converter thật: image LibreOffice build thành công; kiểm MIME422/version409/hash mismatch/macro rejection và request hợp lệ. Host từ chối user namespace; request hợp lệ nhận503. Đây là kiểm fail closed, KHÔNG là chuyển đổi PDF/layout thành công.
- ClamAV: adapter được thử với peer INSTREAM có kiểm framing, OK/FOUND/resource limit/malformed; tắt/chưa xác nhận/vượt coverage không Clean. Peer giao thức KHÔNG thay cho daemon/engine/signatures thật.

## Kết quả lượt kiểm tra cuối

Lượt cuối sau mọi sửa code: build solution0 lỗi,2 warning GoogleCredential obsolete có sẵn; build legacy thành công. Unit38/38, legacy47/47, integration81/81: tổng166 pass,0 fail,0 skip. Integration3 phút55 giây. Không có skip tự động không có nghĩa ClamAV engine hoặc conversion/layout đã được kiểm thành công; xem phạm vi và giới hạn phía dưới.

Lệnh tái lập:

```powershell
dotnet restore tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj
dotnet restore tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj
dotnet build dotnet-clean-architecture-cqrs-starter.sln --no-restore
dotnet build tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj --no-restore
dotnet test tests/CloudDms.UnitTests/CloudDms.UnitTests.csproj --no-build
dotnet test tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj --no-build
dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj --no-build
```

Integration cần image `cloud-dms-embedding-task12`, `cloud-dms-preview-task12`, `cloud-dms-minio-tests:2025-10-15`; build theo Dockerfile trong services và deploy/minio. MySQL/Redis/Qdrant được tải nếu thiếu cache. Fixture tự dọn đúng container nó tạo. Lỗi dependency AWSSDK.S3 lần đầu là assets `--no-restore` cũ, không phải lỗi production.

## Benchmark thực, giới hạn rõ

[Corpus50 query](../tests/fixtures/search-read/corpus.json), [runner](../tools/search-read-benchmark/README.md). Nhãn relevance nhị phân tổng hợp, chưa có đánh giá độc lập. Có Việt/Anh/đa ngôn ngữ, tên ngắn, metadata-only, content replacement; kiểm permission/version riêng ngoài mẫu metric.

Máy: Intel Core i7-13650HX14 core/20 logical; Windows; Docker Desktop Linux20CPU,12.43GB RAM; .NET8.0.23, SDK9.0.309. E5 bị giới hạn2CPU/1200MiB; MinIO/Qdrant/converter512MiB mỗi container, pids128. Corpus10 tài liệu,50 query mỗi mode,concurrency1,page10; mỗi query tạo search session mới. Không warmup riêng, không giả lập tải production.

Artifact [lần chạy 04:42UTC](../tools/search-read-benchmark/results-2026-10-14.json):

| Mode | Recall@5 | nDCG@10 | p95 handler (ms) |
|---|---:|---:|---:|
|Keyword|0,96|0,937856|92,1843|
|Semantic|0,90|0,892619|133,2940|
|Hybrid|1,00|0,944325|122,4237|

Index-stage10 tài liệu:1355,4588ms. Mẫu E5 quan sát cao nhất63,46%CPU và522,6MiB RAM; không phải bảo đảm peak. UUID fixture mới có thể đổi thứ tự tie giữa các lần chạy; không dùng metric làm test threshold. Duration index-stage gồm upload/download/extract/chunk/embed/vector stage; scan và SQL publication generation được seed tin cậy. Không đo pipeline worker/AV end-to-end. Mẫu docker stats rời rạc chỉ cho biết quan sát CPU/RAM, không chứng minh peak hoặc CPU-seconds.

## Lỗi production được sửa từ RED → GREEN

1. Migration multipart thiếu GUID collation làm FK thất bại MySQL thật → thống nhất ascii_general_ci với parent/model.
2. Keyword GroupBy/ordered First không dịch được trong Pomelo → SQL max-score/join/min ordinal trong tập best-score, không materialize toàn văn để xử lý client.
3. BOM tự đổi strict decoder thành permissive → decoder strict UTF8/16/32 theo BOM, byte/surrogate hỏng không thành text replacement.
4. SDK presign tự chọn HTTPS cho endpoint loopback HTTP → scheme PUT/GET lấy từ endpoint đã validate.
5. FluentValidation lỗi input bị500 →400 chung/no-store, không trả nội dung lỗi hoặc dữ liệu người dùng. Không blanket-map mọi ArgumentException.
6. Multipart Begin/Abort/recovery dùng transaction ngoài execution strategy của API → đặt phần SQL trong strategy. Begin kiểm session server-generated cố định khi retry sau mất ACK commit; Abort/recovery dùng CAS trạng thái để hoàn quota đúng một lần. S3 effects nằm ngoài vòng retry SQL.

## Chưa nghiệm thu / còn giới hạn

Chưa tuyên bố hoàn tất mọi AC01–12 hoặc production ready. Cần host cho user namespace hợp lệ để kiểm PDF/Office layout, namespace/seccomp socket denial, descendant cleanup, CPU/RAM/tmpfs/deadline60/120s, slowdrip/transport saturation và parser exploit/bomb sâu. Không dùng privileged/unconfined/Docker socket để vượt lỗi.

Chưa chạy ClamAV engine/signatures thật, EICAR thực, daemon skip oversized, coverage đến2GiB hoặc luồng clean upload→worker→scan→CAS publish hoàn chỉnh. Storage lớn50GiB, nhiều replica/cross-process lease heartbeats/crash ở mọi điểm/uncertain commit do network thật, cache/key-ring rotation/migration model đầy đủ, production CORS/proxy/private policy, tải và corpus lớn còn cần kiểm chứng. Có kiểm mất ACK bằng interceptor sau commit thực, không thay chaos/network test đầy đủ.

Ảnh image digest/SBOM toàn chuỗi và certificate rotation vận hành chưa hoàn tất. Coverage chưa xác nhận vẫn phải giữ mặc định fail closed. Whole-branch review do controller thực hiện sau handoff; chưa merge/publish.

## Đối chiếu AC01–12

| AC | Bằng chứng hiện có | Trạng thái nghiệm thu |
|---|---|---|
|01 Keyword|MySQL tên/body/metadata, Unicode/literal wildcard/ACL, best chunk|Đạt phạm vi fixture; chưa corpus lớn|
|02 Semantic|E5/Qdrant thật và50 query|Đạt smoke; nhãn/corpus nhỏ|
|03 Hybrid|RRF k60, chunk dedup/tie, handler thật, degraded|Đạt phạm vi test|
|04 Tenant|MySQL batch520, grant và SQL scope tenant|Đạt read/search fixture; không tuyên bố toàn hệ thống|
|05 ACL|Team/inherited/expired/revoke/pending/session và revalidation|Đạt phạm vi test; chưa mọi race điểm cắt|
|06 Index lifecycle|Outbox mutation cũ, content replacement loại vector stale|Một phần; chưa worker clean pipeline đổi/xóa/restore/crash đầy đủ|
|07 Resilience|Busy lock fairness, lease exhausted/wrong owner, retry/quota/cleanup|Một phần; chưa heartbeat/process crash/sink failures toàn chuỗi|
|08 Model independence|Contract pin/model và schema đã kiểm|Chưa chạy đổi model/reindex runtime|
|09 Self-host|E5 CPU model local đúng hash, Qdrant/MinIO riêng|Đạt smoke nội bộ; chưa toàn bộ deployment isolation|
|10 Observability|Status/failure code/HTTP header và artifact benchmark|Một phần; chưa observability vận hành dưới tải|
|11 Read|URL private/expiry, Range/resume5MiB, read-access/legacy HTTP|Một phần; chưa file50GiB/production CORS; file ngoài scan coverage phải pending|
|12 Preview|MIME/version/hash/macro transport; text paging; sandbox fail closed503|Chặn môi trường cho conversion/layout; chưa nghiệm thu PDF/Office thành công|
