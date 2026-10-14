# Kế hoạch triển khai Read File và Search File

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai đầy đủ Release 1 theo thiết kế: ACL dùng chung, direct read, keyword/semantic/hybrid search, indexing nền, preview và API nội dung trích xuất.

**Architecture:** Giữ bốn tầng .NET 8. Application điều phối use case qua interface; Infrastructure tích hợp MySQL, Redis bảo mật, Object Storage tương thích S3, Qdrant, embedding nội bộ và converter cô lập. Mỗi task có implementer và reviewer riêng; không chạy nhiều implementer đồng thời.

**Tech Stack:** .NET 8, EF Core/Pomelo MySQL, Redis, xUnit, S3-compatible storage, Qdrant HTTP API, Python embedding CPU `intfloat/multilingual-e5-small`, LibreOffice trong container cô lập.

**Spec:** [Thiết kế](../design/CloudDMS_Search_Read_Design.md), [đặc tả](../spec/CloudDMS_Search_Read_Spec.md).

## Global Constraints

- Đọc `AGENTS.md`, `ARCHITECTURE.md`, `SECURITY.md`; interface ở `Application/Common/Interfaces`, entity ở `Domain/Entities`; không thêm phụ thuộc ngược giữa các tầng.
- Không ghi bí mật, signed URL hoặc toàn văn tài liệu vào mã/log/tài liệu. Cấu hình mẫu chỉ chứa giá trị không nhạy cảm và tên biến môi trường.
- Tenant lấy từ ngữ cảnh đã xác thực; SystemAdmin không tự có quyền nội dung xuyên tenant. MySQL là nguồn chuẩn ACL/trạng thái/version.
- Security pending, thiếu session hoặc Redis bảo mật không sẵn sàng không được fallback sang claim permission cũ. Scan lỗi/tắt không được coi là sạch.
- Không dùng bảng `OutboxMessages` của authorization cho indexing. Không chạy migration/seed hoặc thay đổi dữ liệu trên DB đang sử dụng của người dùng trong quá trình phát triển.
- Không hứa thu hồi tức thì presigned URL đã cấp. TTL mặc định 2 phút; file lớn đọc trực tiếp Object Storage; provider không có capability phải báo không hỗ trợ.
- PDF có text, DOCX/XLSX/PPTX, TXT/CSV/Markdown được phân tích; không OCR/transcript/HLS. File không hỗ trợ vẫn tìm metadata nếu an toàn.
- Duy trì endpoint download trả byte trong giai đoạn chuyển đổi nhưng bắt buộc dùng ACL/scan mới; client mới dùng read-access. Không đưa direct storage key vào response search.
- Phân biệt `DocumentVersion` và `AuthorizationVersion`; object key bất biến theo version; không trả index/preview version cũ.
- Review từng task trước task tiếp theo; test có chu kỳ fail → implement → pass. Mọi giới hạn chạy thử phải ghi là cấu hình ban đầu, không tuyên bố đã đạt benchmark.

## Các quyết định triển khai từ câu hỏi mở

- Chọn S3-compatible adapter để chạy local bằng MinIO và có thể đổi provider. Local/Drive/OneDrive giữ hành vi hiện tại nhưng không giả lập presigned capability.
- ACL dùng hợp các grant còn hạn, không explicit deny. Viewer đọc; Editor thêm cập nhật; Owner thêm chia sẻ/xóa. Team là entity riêng, không mặc định mọi người cùng phòng ban đều đọc được file.
- RRF `k=60`, trọng số hai nhánh bằng nhau; chunk mục tiêu 300 token, overlap 50, luôn tính prefix/special token trong giới hạn model thực tế.
- Search mặc định hybrid, page size 20/tối đa 100; query tối đa 1.000 ký tự, tổng candidate budget 1.000, tối đa 5 lần refill, timeout 10 giây; cursor/cache session TTL 5 phút.
- Analysis ban đầu tối đa 100 MiB, 1.000 trang/sheet, 2.000 chunk, timeout 120 giây; converter tối đa 60 giây và 512 MiB. Giới hạn phân tích không phải giới hạn storage upload.
- Ngưỡng upload/storage quota và retention lấy cấu hình; không mặc định unlimited. Worker không gửi dữ liệu tới AI/viewer bên ngoài.
- Mỗi event document có trạng thái retry, lease và terminal failure; tối đa 8 lần thử, backoff có trần 5 phút, lease 3 phút. Reconciliation chạy mỗi 30 phút.
- Code theo các giá trị trên để có bản chạy thử; thay đổi sau benchmark không cần viết lại nghiệp vụ.

## Review Focus

1. JWT hiện chưa có session identity nối tới Redis: không để endpoint mới luôn từ chối hoặc chấp nhận phiên giả; Task 2 kiểm chứng vòng đời login/refresh.
2. Grant kế thừa/team bị thu hồi trong khi trả search/URL: Task 3 kiểm stamp/generation và mutation pending.
3. Worker cũ hoàn tất sau ghi đè/xóa: Task 7 kiểm compare-and-publish và retry idempotent.
4. Tên tiếng Việt ngắn, dấu `%`/`_`, query rỗng và index lỗi: Task 6/9 kiểm SQL parameterization, metadata fallback và degraded đúng điều kiện.
5. URL hết hạn/Range và file độc hại đưa vào converter: Task 4/10 kiểm provider thật, MIME, scan và giới hạn converter.

## Chuẩn bị workspace và kiểm chứng

Hiện checkout ở `main`, design/spec còn untracked. Trước triển khai, xin lựa chọn workspace theo skill worktree; ưu tiên `.worktrees/search-read` trên nhánh `feat/search-read`. Sao chép design/spec/plan vào worktree để agent có đủ căn cứ; không xóa hay ghi đè file người dùng.

Baseline đã chạy ngày 13/10/2026: `dotnet test tests/CloudDms.UnitTests/CloudDms.UnitTests.csproj --no-restore --verbosity minimal`, 22 pass/0 fail. Repository thực tế có bộ test dù hướng dẫn/tài liệu cũ nói chưa có. Integration cần Redis/MySQL; không coi test bị skip là pass đầy đủ.

Ledger riêng của kế hoạch ở `.superpowers/sdd/`; ghi BASE, task hoàn tất, test/review và các ruling để tiếp tục an toàn sau compaction. Bash script của skill được dùng nếu môi trường hỗ trợ; nếu không có Bash, tạo brief/review package tương đương bằng PowerShell, ghi rõ ruling trong ledger.

## Hợp đồng dữ liệu dùng giữa các task

Tạo các record trong `Application/Common/Models/Files/` ở Task 1:

- `FileAccessContext(ActorIdentity Actor, Guid TenantId)`; `FileAccessGrant(Guid FileId, long DocumentVersion, long AuthorizationVersion, AuthorizationStamp Stamp)`.
- `SearchRequest(string Query, Guid? FolderId, int CandidateLimit, string ModelVersion)`; không chứa tenant do client gửi.
- `SearchCandidate(Guid FileId, long DocumentVersion, Guid? GenerationId, int? ChunkOrdinal, double Score, string Source, string? ModelVersion)`; không chứa snippet/toàn văn.
- `SearchBranchResult(IReadOnlyList<SearchCandidate> Candidates, bool HasMore)`.
- `ReadUrlRequest(Guid ProviderId, string ObjectKey, string FileName, string ContentType, string Purpose, TimeSpan Lifetime)`; `ObjectReadAccess(Uri Url, DateTimeOffset ExpiresAt, bool SupportsRange)`.
- `EmbeddingResult(float[] Values, string ModelVersion)`; `ExtractedChunk(int Ordinal, string Text, int TokenCount, string? Location)`.
- `SearchCursorState(Guid UserId, Guid TenantId, string QueryHash, Guid SearchSessionId, int Position, DateTimeOffset ExpiresAt)`.

Interface đều nhận `CancellationToken`; kiểu DTO API tách khỏi entity EF. Agent được bổ sung field cần thiết nhưng phải cập nhật brief/consumer trước task tiếp theo.

### Task 1: Schema version, ACL và document outbox

**Files:** Tạo entity `FileContentVersion`, `FileSearchGeneration`, `FileTextChunk`, `FilePreviewArtifact`, `DocumentOutboxMessage`, `FileUploadSession`, `Team`, `TeamMember`, `ResourceGrant` trong `src/CleanArchCqrs.Domain/Entities/`; enum trạng thái trong `Domain/Enums/`. Sửa `FileEntry.cs`, `Folder.cs`, `Application/Common/Interfaces/IApplicationDbContext.cs`, `Infrastructure/Persistence/AppDbContext.cs`; thêm configuration và migration `AddFileSearchRead` trong Infrastructure.

**Interfaces:** Sản xuất các record hợp đồng ở trên; `DocumentVersion` mặc định 1, `ActiveSearchGenerationId` nullable. Grant có tenant, resource kind/id, principal kind/id, role, expiry; team/folder có authorization version. Content version giữ hash, object key và provider id; generation giữ model/extractor/chunker version và trạng thái partial.

- [ ] Viết `tests/CloudDms.UnitTests/Files/SearchReadContractTests.cs` và `tests/CloudDms.IntegrationTests/Persistence/SearchReadSchemaTests.cs`: version nội dung/quyền độc lập, FK và unique `(FileId, DocumentVersion)`, chunk ordinal/generation, preview cache key; document outbox không nằm trong authorization outbox.
- [ ] Chạy test mới, xác nhận fail trước schema/contract.
- [ ] Triển khai model, cấu hình, migration; xác minh tenant FK và index truy vấn; không cascade-delete dữ liệu bất đồng bộ ngoài chính sách.
- [ ] Chạy test + script migration trên DB integration riêng; giữ nguyên migration đã tồn tại.
- [ ] Commit task và gửi report cho reviewer.

### Task 2: Session identity cho authorization dùng được với API

**Files:** Sửa `Application/Common/Interfaces/ICurrentUserService.cs`, `IJwtTokenService.cs`, `API/Services/CurrentUserService.cs`, `Infrastructure/Services/JwtTokenService.cs`, handler login/Google login/refresh và projection/session hiện có trong `Infrastructure/Security/`. Tạo `Application/Common/Interfaces/IFileAccessService.cs` và `Infrastructure/Security/FileAccessContextResolver.cs`.

**Interfaces:** Thêm `Guid? SessionId` vào current user; phát token có claim `sid` gắn session được persist/project thực tế. `IFileAccessService.ResolveContextAsync(CancellationToken)` trả `FileAccessContext`; không nhận user/tenant/session từ request body. Login/refresh không cấp token hoạt động trước khi session snapshot sẵn sàng; cập nhật mọi fake current user trong tests.

- [ ] Viết test login tạo session/claim, refresh theo chính sách phiên hiện hành, phiên hết hạn/revoked, sid thiếu/sai, Redis lỗi; không tạo session ngẫu nhiên chỉ từ claim client.
- [ ] Chạy test fail, nối vòng đời session tới store/projection hiện có; không thay toàn bộ SSO ngoài phần cần cho read/search.
- [ ] Test JWT + Redis integration xác minh token mới thực sự gọi được evaluator, revoked không gọi được.
- [ ] Chạy test auth hiện có và commit/report.

### Task 3: ACL chung, batch authorization và mutation version

**Files:** `Application/Common/Authorization/AccessPolicyEvaluator.cs`, `AuthorizationModels.cs`, `IAuthorizationService.cs`, `IFileAccessService.cs`; `Infrastructure/Security/AuthorizationProjectionWriter.cs`, `RedisAccessSnapshotStore.cs` và Lua liên quan. Thêm query ACL tenant-scoped, handler quản lý grant/team/folder tối thiểu trong `Application/Features/Files/Commands/ManageAccess/`, controller API tương ứng; sửa command delete/restore/permanent-delete hiện có.

**Interfaces:** `IFileAccessService.CheckManyAsync(FileAccessContext context, IReadOnlyCollection<Guid> fileIds, CancellationToken)` → dictionary grant; `AssertStillValidAsync(FileAccessContext, IReadOnlyCollection<FileAccessGrant>, CancellationToken)`. Batch resolve grant MySQL, chia sẻ trực tiếp còn hạn, team còn hoạt động và các ancestor cùng tenant; phát hiện folder cycle. Mọi mutation trên đường này dùng coordinator/idempotency và tăng version phù hợp.

- [ ] Test owner/direct share/team/ancestor, Viewer/Editor/Owner; tenant khác, admin không có grant, expiry, cycle; mutation pending và revoke giữa check/assert phải chặn.
- [ ] Viết test fail; nối grant effective vào evaluator hiện có thay vì viết evaluator riêng cho search.
- [ ] Kiểm tra SQL lọc tenant và snapshot/version nguyên tử; test batch không gây N+1 tăng tuyến tính theo chunk.
- [ ] Regression policy/coordinator tests; commit/report.

### Task 4: S3 adapter và Read Access API

**Files:** Tạo `Application/Common/Interfaces/IObjectReadUrlService.cs`, `Infrastructure/Services/Storage/S3StorageService.cs`, `ObjectReadUrlService.cs`; sửa enum provider/factory. Tạo `Application/Features/Files/Queries/GetFileReadAccess/`, sửa `API/Controllers/FilesController.cs`, handler `DownloadFileQuery`; thêm audit cấp URL và options TTL.

**Interfaces:** `IObjectReadUrlService.CreateAsync(ReadUrlRequest, CancellationToken)` → `ObjectReadAccess`. Thêm provider enum `S3=3`, config mã hóa theo cơ chế hiện có. Query nhận `FileId`/purpose, kiểm ACL+scan+current version, revalidate sát trả response. `read-access` chỉ trả URL GET object/version cụ thể; header `no-store`.

- [ ] Test denied/tenant khác/scan pending hoặc lỗi không gọi signer; purpose sai, inline HTML bị từ chối, inactive provider, auth/version đổi trước trả URL.
- [ ] Viết test fail; dùng SDK S3 chính thức cho signing/GET/HEAD, chọn dependency tương thích net8 sau đối chiếu docs chính thức; không tự viết thuật toán chữ ký.
- [ ] Integration MinIO: GET attachment, MIME, expiry và Range 206; legacy download vẫn trả bytes nhưng bắt buộc cùng ACL/scan, không còn role bypass.
- [ ] Chạy test, commit/report; không log URL trong exception/telemetry.

### Task 5: Upload trực tiếp và vòng đời version

**Files:** Tạo `Application/Features/Files/Commands/BeginMultipartUpload/`, `CompleteMultipartUpload/`, `AbortMultipartUpload/`, `ReplaceFileContent/`; interface `IMultipartObjectStorage.cs`; S3 adapter, endpoints file upload session, document event writer. Sửa upload hiện có và delete/restore để ghi document outbox cùng transaction nghiệp vụ.

**Interfaces:** Multipart begin giữ owner/tenant/quota reservation/expiry, server tạo immutable key; complete chỉ nhận session id và part numbers/ETag, không tin kích thước/hash/type client. HEAD/object inspection xác minh trước commit metadata. Ghi `DocumentCreated`, `DocumentContentChanged`, `DocumentMetadataChanged`, `DocumentPermissionsChanged`, `DocumentDeleted` theo thiết kế.

- [ ] Test giả complete, session tenant khác/hết hạn, part sai/trùng, quota concurrent, HEAD mismatch, retry idempotent và orphan cleanup.
- [ ] Viết test fail; triển khai begin/presign-part/complete/abort; không mở active file trước scan sạch. Chuyển ClamAV disabled/error sang trạng thái không an toàn; regression upload cũ.
- [ ] MinIO integration upload nhiều part, complete lặp không nhân quota/outbox; content replacement tạo version/key mới, event cũ không khôi phục file xóa.
- [ ] Commit/report, chưa chạy mutation trên dữ liệu người dùng.

### Task 6: Keyword Search và SQL ACL

**Files:** `Application/Common/Interfaces/IKeywordSearchService.cs`; `Infrastructure/Search/MySqlKeywordSearchService.cs`, `FileSearchSqlScope.cs`; migration index FULLTEXT metadata/text; test MySQL thật.

**Interfaces:** `IKeywordSearchService.SearchAsync(FileAccessContext, SearchRequest, int offset, CancellationToken)` → `SearchBranchResult`. `FileSearchSqlScope` phải dùng cùng hợp đồng grant Task 3; query tìm metadata ngay cả khi chưa có generation, chỉ tìm nội dung generation/version hiện hành. Offset chỉ dùng bên trong bounded candidate collection.

- [ ] Test fail cho tên/nội dung Việt, token ngắn, `%`/`_` literal, SQL injection input, tenant/ACL/scan trước LIMIT, generation lỗi/cũ và file không hỗ trợ.
- [ ] Triển khai MATCH/AGAINST tham số hóa, filename fallback có escaping, tie-break FileId; không lấy snippet trong nhánh candidates.
- [ ] Chạy MySQL integration và EXPLAIN trên fixture đủ lớn; bảo đảm có index tenant/lifecycle/grant và không đọc full-text tenant khác vào response.
- [ ] Commit/report.

### Task 7: Worker extraction, chunking và indexing generation

**Files:** Interface `IFileTextExtractor.cs`, `ITextChunker.cs`, `IDocumentIndexSink.cs`; adapter extractor trong `Infrastructure/Search/Extraction/`; worker `Infrastructure/Workers/DocumentIndexWorker.cs`, processor và reconciliation; configuration limits/lease/retry.

**Interfaces:** Extractor đọc stream được giới hạn byte, xuất `ExtractedChunk`; chunker dùng tokenizer của model, mục tiêu 300/overlap 50. Processor stage chunks/generation; `IDocumentIndexSink.StageAsync(Guid generationId, CancellationToken)` và `DeleteAsync(Guid tenantId, Guid fileId, CancellationToken)` cho vector adapter Task 8. Task 7 chưa publish semantic-ready nếu vector sink chưa sẵn sàng.

- [ ] Test fail cho text PDF/Office/TXT/CSV/Markdown, PDF ảnh không OCR, malformed archive, zip bomb/resource limit; unsupported và Partial phải có lý do rõ ràng.
- [ ] Viết pipeline scan → extraction → chunk → stage; lease claim có điều kiện nguyên tử; retry/max-attempt; không giả định thứ tự event.
- [ ] Test crash/replay, worker cũ sau update/delete, lease hết hạn, stage lỗi và compare-and-publish; metadata-only vẫn searchable, generation incomplete không searchable.
- [ ] Reconciliation orphan/missing generation và retention artifacts; commit/report.

### Task 8: Embedding CPU nội bộ và Qdrant

**Files:** `Application/Common/Interfaces/IEmbeddingService.cs`, `IVectorSearchService.cs`; `Infrastructure/Search/InternalEmbeddingService.cs`, `QdrantVectorSearchService.cs`, `QdrantDocumentIndexSink.cs`; service Python trong `services/embedding/`, Dockerfile, locked requirements/model preload; compose service nội bộ.

**Interfaces:** `IEmbeddingService.EmbedQueryAsync(string, CancellationToken)` và `EmbedPassagesAsync(IReadOnlyList<string>, CancellationToken)` → embedding/model version; `IVectorSearchService.SearchAsync(FileAccessContext, SearchRequest, float[] vector, int limit, CancellationToken)` → candidates. Qdrant point id xác định theo generation/chunk; bắt buộc filter tenant, collection/model đúng dimension.

- [ ] HTTP contract test prefix query/passage, tokenizer/truncation, normalize vector, model/dimension mismatch và timeout; test point payload không cần toàn văn/ACL chuẩn.
- [ ] Test fail rồi triển khai Python CPU theo model card chính thức, không embedding giả/hash. Preload model khi build/setup, runtime không có Internet; không tải model trong request.
- [ ] Qdrant integration stage/delete idempotent, tenant filter, stale generation, model migration collection; chạy pipeline Task 7 end-to-end với văn bản mẫu.
- [ ] Commit/report, ghi đo latency CPU; chưa tự tuyên bố Recall đạt ngưỡng.

### Task 9: Hybrid Search API và cursor

**Files:** `Application/Features/Files/Queries/SearchFiles/`, `Application/Common/Interfaces/ISearchSessionStore.cs`, DTO, RRF service; `Infrastructure/Search/SearchSessionStore.cs`, cursor protector dùng ASP.NET Data Protection; `API/Controllers/FilesController.cs`.

**Interfaces:** Handler dùng contracts Task 1/3/6/8; session cache chỉ giữ candidate ID/version/rank, không signed URL; cursor user/tenant/query/mode/filters/TTL. Kết quả gồm items/nextCursor/modeUsed/degraded/warnings; snippet plain text lấy MySQL sau ACL và current generation.

- [ ] Test fail cho RRF k=60, gộp chunk trước file ranking, tie-break, modes, metadata-only, refills giới hạn, không tiết lộ count/candidate không được phép.
- [ ] Test cursor bị sửa/dùng user khác/query khác/hết hạn; revoke giữa trang, version thay đổi trước trả; page size và query limits.
- [ ] Triển khai handler/API; keyword fallback chỉ khi semantic dependency lỗi, authorization/MySQL lỗi phải fail-closed. Rate limit, cancellation và thời gian tổng có giới hạn.
- [ ] Integration request qua API tới MySQL/Qdrant/Redis; test permission claim cũ không override ACL; commit/report.

### Task 10: Preview và extracted text API

**Files:** Interface `IPreviewProvider.cs`; `Application/Features/Files/Commands/RequestFilePreview/`, `Queries/GetFilePreview/`, `Queries/GetFileText/`; converter client/job processor trong `Infrastructure/Preview/`; service `services/preview/` chạy LibreOffice cô lập; endpoints và options.

**Interfaces:** Converter nhận object đã quét/version/hash xác định, tạo PDF artifact theo cache key thiết kế; job leasing dùng pattern Task 7. API text dùng signed cursor file/version/chunk ordinal, default 20/tối đa 100 chunk, revalidate ACL; không tự extraction đồng bộ.

- [ ] Test fail cho cache hit, duplicate job, PDF/ảnh/TXT inline, unsupported, scan không sạch, version/hash đổi, revoke trong polling và text pagination.
- [ ] Converter no-network, non-root, filesystem giới hạn/read-only, timeout 60 giây và limit memory 512 MiB; không chạy Office process trực tiếp trong API. Store PDF private, URL cùng TTL/ACL Task 4.
- [ ] Fixture DOCX/XLSX/PPTX thực tế; crash/timeouts/macro không tạo artifact ready; job cũ không publish; preview pending trả 202.
- [ ] Commit/report; viewer bên thứ ba không bật trong Release 1 mặc định.

### Task 11: Tích hợp DI, deploy, API regression và tài liệu

**Files:** `API/Program.cs`, `Application/DependencyInjection/ApplicationServiceExtensions.cs`, cấu hình DI Infrastructure thực tế trong repository; file cấu hình mẫu mới `deploy/search-read.env.example`, `deploy/compose.search-read.yml`; `docs/search-read-runbook.md`, cập nhật phần hiện trạng trong `ARCHITECTURE.md`/`SECURITY.md` chỉ liên quan thay đổi này.

**Interfaces:** Validate options khi startup; cấu hình nội bộ embedding/Qdrant/converter và secrets bằng environment/secret store. Không sửa hoặc in bí mật trong `appsettings.Development.json`. Default-disabled worker/service phải có trạng thái rõ, không giả ready.

- [ ] Test HTTP 401/404/409/202/422/429/503, no-store, scrubbed errors; regression legacy download, login/refresh/upload/delete/restore.
- [ ] Kiểm tra boot từ DB integration sạch và DB nâng cấp, seed/project resource/session đầy đủ; giải quyết dependency registration thiếu và scope lifetime.
- [ ] Runbook tiếng Việt: setup model/storage CORS/Range, migration trên DB thử, retry/dead-letter/reconcile, chuyển client, TTL/revoke limitation và rollback generation.
- [ ] Commit/report; không push/deploy/merge nếu chưa được yêu cầu.

### Task 12: Nghiệm thu đầy đủ và review toàn nhánh

**Files:** `tests/CloudDms.IntegrationTests/Files/SearchReadEndToEndTests.cs`, `tests/fixtures/search-read/`, script benchmark trong `tools/search-read-benchmark/`, `docs/search-read-validation.md`.

- [ ] Tạo 50 query đại diện có nhãn ground truth, fixture không nhạy cảm; bao gồm Việt/đa ngôn ngữ, tên ngắn, metadata-only, permissions và content replacement.
- [ ] Chạy `dotnet build dotnet-clean-architecture-cqrs-starter.sln`, unit tests, test cũ, integration với MySQL/Redis/MinIO/Qdrant/embedding/converter thật; không coi adapter stub là hoàn thành tích hợp.
- [ ] Đo Recall@5/nDCG@10 và p95 từng mode, indexing duration/CPU/RAM, Range/resume và layout PDF thực tế; ghi số đo/máy/tải, không biến mục tiêu đề xuất thành kết quả.
- [ ] Reviewer toàn nhánh đánh giá AC-01–12, security/concurrency và migration; sửa finding quan trọng, chạy lại test liên quan rồi review lại phạm vi fix.
- [ ] Báo cáo phần đã triển khai/đã test/giới hạn môi trường. Giữ branch/worktree để người dùng review, không tự merge hay publish.

## Self-review kế hoạch

| Phần thiết kế | Task |
|---|---|
| ACL, tenant, stamp, scan, vòng đời session thực tế | 1–3, 5 |
| Direct download/stream, capability, tương thích endpoint cũ | 4 |
| Multipart/complete validation, quota, immutable object version | 5 |
| Keyword/metadata, SQL ACL, FULLTEXT | 6 |
| Outbox riêng, retry/lease, generation, đổi/xóa/restore, đối soát | 1, 5, 7–8 |
| Embedding nội bộ, Qdrant và model independence | 8 |
| RRF, snippets, cursor, refill và degraded | 9 |
| Office/inline preview, cache, isolated converter, extracted text | 10 |
| API lỗi/rate limit, cấu hình, vận hành và acceptance/benchmark | 11–12 |

Dependency: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12. Schema/context là contracts dùng chung; mỗi reviewer kiểm consumer ở task sau khớp producer. Các task đều kiểm fail/pass/commit/report; không gắn completion Release 1 cho keyword-only hoặc adapter không chạy thật.

## Điều chỉnh thứ tự theo yêu cầu người dùng

Ngày 13/10/2026: hoàn thiện code Task 4–11 trước; chưa viết hoặc chạy thêm test trong giai đoạn này. Giữ các test đã có. Review tĩnh và build vẫn thực hiện khi cần. Sau khi code hoàn tất, thực hiện toàn bộ kiểm thử còn thiếu của từng task cùng Task 12 và benchmark. Phần chưa chạy test được ghi rõ là chưa kiểm chứng.

Người dùng chọn giữ ClamAV: file vượt giới hạn quét đáng tin cậy vẫn được lưu, nhưng chờ xác minh và không được đọc hoặc công bố index sạch. Không tăng cấu hình ClamAV để giả định quét được hàng chục GiB. Giới hạn upload, phân tích và quét là ba giới hạn riêng.
