# Task 7 — worker lập chỉ mục

BASE: `5102f3122d8e3481c88bf2e580d2013f80502eba`. Đã viết mã production; chưa kiểm chứng runtime. Theo yêu cầu code trước, không viết/chạy test, integration hoặc benchmark; không chạy migration/seed/DB người dùng.

## Đã triển khai

- Worker claim outbox document bằng CAS; lease 3 phút, gia hạn 30 giây bằng scope riêng, tối đa 8 lần thử, backoff trần 5 phút. Advisory lock MySQL `dms-index:{fileId:N}` giữ riêng suốt job, tránh hai event cùng file xóa/stage chồng nhau. Mất lease/lock sẽ hủy job; publication giữ khóa dòng lease đến commit.
- Đọc lifecycle/version/provider/key hiện tại từ SQL, không dùng thứ tự hoặc payload event để khôi phục file. Scan mutation đi qua coordinator global, so sánh file hiện tại trước cập nhật scan/hash/MIME; publication generation so sánh current version + Active + Clean trong transaction.
- Stream toàn object qua ClamAV và SHA-256 cùng một lượt; chỉ capture tối đa 100 MiB phục vụ analysis. Không cho Clean nếu scan lỗi/tắt/giới hạn, chưa EOF, sai kích thước hoặc hash thay đổi. Hash đã biết được giữ khi scan lỗi để lần retry không chấp nhận object bị thay thế.
- `VirusScanResult.ScanSucceeded` phân biệt lỗi dịch vụ với nhiễm; upload handler cũ cũng từ chối lỗi dịch vụ. Chỉ phản hồi ClamAV `stream: OK` trong coverage được xác nhận mới là Clean; giới hạn `Heuristics.Limits.Exceeded` là scan thất bại.
- PDF có text dùng tiến trình Poppler `pdftotext` cục bộ, giới hạn trang và output, kill khi timeout/memory vượt ngưỡng. Không OCR. OOXML đọc DOCX paragraph, XLSX shared strings/rows, PPTX slides; cấm DTD/XML resolver, kiểm ZIP entry/tổng giải nén/tỉ lệ nén, không chạy macro. TXT/CSV/Markdown kiểm encoding/text; trả MIME text/plain an toàn. Unsupported/không có text/partial có reason.
- Giới hạn analysis 100 MiB, thời gian extraction + tokenizer 120 giây, 1.000 trang/sheet và tối đa 1.000 source section để chặn tăng bộ nhớ; XML/text tối đa 16M ký tự; 2.000 chunk. PDF child process theo dõi ngưỡng 512 MiB. Nguồn text chưa token hóa dùng `TextSection`, không giả token count.
- SQL stage generation/chunks trước sink. Body generation chỉ Ready/Partial sau khi sink thành công; thiếu tokenizer/sink thì retry, generation chưa publish. Metadata-only có 0 chunk, luôn Partial và reason; metadata SQL vẫn dùng scan/ACL hiện tại.
- Bin xóa index, giữ object hiện tại; restore xử lý phiên bản hiện tại. Purge chờ TTL URL tối đa 10 phút, xóa mọi version/artifact và sink, giữ tombstone/parent. Không hoàn lại logical quota đã được giải phóng. Xóa old version chỉ đúng provider/key bất biến và không phải current object; `UploadQuota.RecordPhysicalCleanupAsync` nằm trong SQL transaction để refund staging một lần.
- Retention mặc định 60 phút, tối thiểu 10 phút, tính từ replacement hiện tại trước xóa object cũ. Dọn generation bằng ID riêng, không xóa vector generation đang active. Reconciliation mỗi 30 phút: backfill version Pending từ authoritative provider/key, không tin legacy Clean; thiếu nguồn thì ghi `AUTHORITATIVE_OBJECT_UNAVAILABLE`; tạo job cho missing generation/retention/purge.

## Ruling ClamAV và cấu hình Task 11

Người dùng chọn giữ ClamAV và để file vượt coverage chờ xác minh. `SearchIndex:MaxScanBytes` mặc định 25 MiB, phải >0 và <2 GiB; độc lập với storage limit 50 GiB và analysis limit 100 MiB. `SearchIndex:ClamAvCoverageConfirmed` mặc định false. Chỉ bật sau khi đối chiếu cấu hình engine thật (`StreamMaxLength`, `MaxFileSize`, `MaxScanSize`, `AlertExceedsMax` và các giới hạn recursion/container). Không được xem xác nhận cấu hình là khả năng quét 50 GiB của ClamAV.

File biết trước vượt coverage/chưa xác nhận coverage được giữ Pending, outbox terminal `SCAN_AWAITING_VERIFICATION`; lưu coverage key không nhạy cảm trong Payload. Reconciliation không tải/quét lại cùng coverage; thay đổi coverage cấu hình mới cho phép job mới. File này chưa được đọc hoặc tìm nội dung. Upload multipart vẫn lưu được file lớn; upload legacy trực tiếp vẫn từ chối scan không thành công.

Các options khác: `Enabled=true`, `ScanTimeoutSeconds=3600`, `RetentionMinutes=60`, `TokenizerEndpoint=http://127.0.0.1:8001/`, `ModelVersion=intfloat/multilingual-e5-small`, `PdfTextExecutable=pdftotext`. Cần cài Poppler và cấu hình worker/container ít quyền, không mạng cho parser ở Task 11. PDF executable thiếu là lỗi retry rõ ràng, không giả extraction thành công. Không có dịch vụ AI ngoài hệ thống.

## Hợp đồng Task 8

- `ITextChunker.ChunkAsync(sections, ct)` trả `ChunkingResult(ModelVersion, ChunkerVersion, Chunks, IsPartial, Reason)`.
- HTTP `POST {TokenizerEndpoint}/chunk`: JSON `{modelVersion, sections:[{text,location}], targetTokens:300, overlapTokens:50, maxInputTokens:512, maxChunks:2000, prefix:"passage: "}`.
- Response `{modelVersion,chunkerVersion,chunks:[{ordinal,text,tokenCount,location}],isPartial,reason}`. `ordinal` từ 0 liên tục; `tokenCount` là số token thực sau passage prefix + special tokens, tối đa 512; chunk mục tiêu 300, overlap 50. Model/version phải trùng cấu hình; chunker version tối đa 128 ký tự; location tối đa 500. Giữ section location, không dựa offset UTF-16/.NET tương đương codepoint Python. Service phải dùng tokenizer thật được pin; adapter không có fallback đếm heuristic.
- `IDocumentIndexSink.StageAsync(generationId,ct)` đọc generation/chunks SQL, upsert idempotent, chỉ trả khi mọi vector durable. Payload bắt buộc tenant/file/documentVersion/generation; ACL/trạng thái cuối cùng vẫn từ SQL.
- `DeleteAsync(tenantId,fileId,ct)` cho bin/purge. `DeleteGenerationAsync(tenantId,fileId,generationId,ct)` cho generation cũ/orphan, tuyệt đối không xóa cả file khi dọn một generation. Task 8 thay `UnavailableDocumentIndexSink` trong DI bằng adapter thật.
- Stage không được tự publish SQL. Nếu sink không sẵn sàng, generation có text vẫn Processing và không active. Metadata-only không cần vector sink.

## Handoff Task 10 và giới hạn

Writer preview phải dùng cùng file advisory lock hoặc đảm bảo CAS/tombstone/lease tương đương trước và sau tạo artifact; không để artifact xuất hiện sau purge. Artifact phiên bản cũ được xóa theo retention; artifact phiên bản hiện tại giữ đến purge. Parser không chạy macro, OCR hoặc conversion Office. DOCX không có pagination layout thực nên 1.000 section là giới hạn bảo thủ và reason `SECTION_LIMIT`.

Coverage confirmation là vận hành, không tự introspect cấu hình clamd từ protocol. Hard isolation/cgroup memory của parser phụ thuộc Task 11; hiện child process được theo dõi và kill, không phải sandbox hoàn chỉnh. Các file lớn hơn coverage được lưu nhưng chưa mở/read được theo ruling. Chưa chứng minh hiệu năng/resource bound hoặc race bằng runtime.

## Kiểm tra và nghĩa vụ test hoãn

`dotnet build src/CleanArchCqrs.API/CleanArchCqrs.API.csproj --no-restore --verbosity quiet`: thành công, 0 lỗi, 3 warning có sẵn (Hosting.Abstractions trùng và GoogleCredential obsolete). Đã sửa lỗi compile FileShare ambiguity ở lần build đầu. Chỉ compilation và tự đọc mã; không tuyên bố test pass.

Sau Task 11 cần test PDF/OOXML/text fixtures, PDF ảnh, malformed ZIP/XML/zip bomb/XXE/macro/giới hạn/encoding; model tokenizer thực multilingual/prefix-special/overlap/chunk truncation và timeout; ClamAV OK/infection/disabled/unavailable/server skip/coverage/oversize/full hash/stream size mismatch; real MySQL claim nhiều process/lease expire/renew/crash/replay/advisory lock loss; scan coordinator Redis pending; old worker sau overwrite/bin/restore/purge; sink failure/stage replay/generation publish cross-tenant FK và active-generation FK (carry Task 1); metadata-only và quyền thu hồi; purge crash giữa physical delete và staging CAS, không refund logical lần hai, retained key không current, read URL TTL; reconciliation legacy thiếu version/source, terminal coverage thay đổi, orphan/preview cleanup. Không sửa/xóa test có sẵn.

Nguồn ruling coverage: [ClamAV Scanning](https://docs.clamav.net/manual/Usage/Scanning.html), [clamd.conf man](https://github.com/Cisco-Talos/clamav/blob/main/docs/man/clamd.conf.5.in). Hai tài liệu chỉ hỗ trợ giới hạn/cấu hình engine, không thay cho test deployment.

## Sửa review vòng 1

BASE sửa: `3be7323`. Đã đối chiếu và sửa ba finding Important của `task-7-review.md`:

- XLSX kiểm budget từng cell/separator trước append; row tối đa 1 Mi ký tự và không vượt phần còn lại của tổng 16M ký tự. Shared-string table kiểm tổng ký tự và tối đa 100.000 mục ngay khi tạo, có cancellation từng mục/đoạn. Không còn `string.Join` dựng nguyên row mở rộng trước kiểm budget. Dừng đọc thêm row/sheet khi đạt 1.000 section; DOCX/PPTX cũng dừng section tại giới hạn.
- Cùng coverage chỉ chặn enqueue scan. Nếu còn version/generation/artifact cũ, reconciliation enqueue `DocumentRetention`, nhánh processor này chạy trước tải/quét object hiện tại. File Pending quá lớn vẫn thu hồi staging/version cũ sau retention mà không quét lại current object.
- Khi advisory lock file bận, worker CAS lùi `NextAttemptAt` 5 giây cho các event đủ điều kiện của chính file đó rồi chọn tiếp ngay. Không tăng attempt, đổi status/owner hoặc gia hạn lease; không chạm lease còn sống. Selection và claim đều áp dụng NextAttemptAt cho cả Pending và Processing hết lease, tránh một expired candidate bận giữ đầu hàng.

Tự review tĩnh đường budget, cancellation, pending-retention và CAS/busy-lock; build API `--no-restore --verbosity quiet` thành công, 0 lỗi/3 warning baseline. Không chạy/viết test hay dùng DB.

Test hoãn bổ sung sau Task 11: shared string lớn lặp hàng nghìn lần cùng row và inline text dài phải dừng bounded, tiến trình còn hoạt động; shared-string table/section cap/cancellation; thay version nhỏ bằng object vượt coverage, giữ nguyên coverage và qua retention phải xóa đúng old version/artifact/generation, hoàn staging đúng một lần và không tải current object; giữ lock X với Pending X đứng trước Y/Z phải nhận Y/Z, attempt X không tăng và lease X không thay đổi; lặp lại với Processing X đã hết lease và nhiều worker tranh claim.
