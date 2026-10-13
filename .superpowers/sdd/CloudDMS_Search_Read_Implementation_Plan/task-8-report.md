# Task 8 — embedding CPU và Qdrant

BASE: `6dfb2ea6408c40e5b2f80988f058516857971044`. Đã triển khai production, chưa kiểm chứng runtime. Theo user override, không viết/chạy test, service, DB, integration hoặc benchmark; giữ nguyên test hiện có.

## Đã triển khai

- `IEmbeddingService`: `EmbedQueryAsync(string,ct)` → `EmbeddingResult`; `EmbedPassagesAsync(IReadOnlyList<string>,ct)` → danh sách kết quả theo thứ tự. Adapter batch tối đa 16, kiểm identity/dimension 384/finite/L2 norm, giới hạn query 1.000 ký tự, passage 32.768, tổng 2.000 passage; timeout và response cap 1 MiB.
- Python CPU dùng model safetensors và tokenizer fast thật; kiểm SHA256 sáu asset cục bộ trước nạp. `local_files_only=true`, `trust_remote_code=false`, offline env; không tải model trong request, không pickle. Attention-mask mean pooling + L2 normalize, prefix query/passage ngay trong dịch vụ, không truncate. Manifest weight SHA256 `1a55775f53449dac10a2bcbc312469fac40b96d53198c407081a831f81c98477`.
- `/chunk` tương thích Task 7, target 300 token đã tính prefix/special, overlap 50 theo token offset; tokenize theo cửa sổ 8.192 ký tự, giữ text nguồn/location section, kiểm lại số token đầu vào hoàn chỉnh ≤512. Tối đa 2.000 chunk; partial/CHUNK_LIMIT khi hết budget. Không dựa offset UTF-16 liên thông .NET/Python.
- Body cap 112 MiB, tổng source 16M ký tự/1.000 section; một request CPU tại một thời điểm, socket reads bounded, deadline 110 giây và watchdog kết thúc process ở 115 giây để compose restart. Batch ≤16, CPU threads mặc định 2. Không log text/exception details/secret.
- Qdrant HTTP cosine/384, collection theo hash model. Search bắt buộc filter tenant + model, kiểm tenant/model response, trả candidate metadata, không toàn văn/quyền. Search limit 1..1.000, response cap 4 MiB; HTTP timeout, cấm redirect/proxy tự động.
- Sink đọc generation/chunks SQL theo tenant, batch có ordinal liên tục, kiểm model/token/vector, upsert ID deterministic tenant/file/generation/chunk. `wait=true` và chỉ chấp nhận status completed cho mọi batch; không publish SQL. Retry ghi lại cùng point; SQL processor giữ compare-and-publish.
- Delete file cho bin/purge; DeleteGeneration exact tenant/file/generation. Cleanup duyệt mọi collection có namespace `clouddms_e5_` + 64 hex, bao gồm model cũ; không drop collection, không xóa rộng khi dọn generation.
- DI dùng adapter singleton sở hữu HttpClient/dispose và sink scoped; thay placeholder. Producer SearchIndex cũng dùng identity pin. Compose thêm embedding/Qdrant pinned 1.19.2 digest, internal network không egress/public port cho hai dịch vụ, volume Qdrant, resource caps. Model preload setup pin revision + SHA256; đã copy assets cache vào thư mục build bị ignore, không commit weights. API Docker context loại ledger/model.

## Handoff Task 9/11

Identity chính xác cho `SearchIndex:ModelVersion`, `Embedding:ModelVersion`, HTTP metadata và request search:

`intfloat/multilingual-e5-small@614241f622f53c4eeff9890bdc4f31cfecc418b3`

`EmbeddingOptions.PinnedModel` là constant hiện tại. Hai options từ chối identity khác cho release này; đổi model phải cập nhật adapter/model manifest và producer cùng lúc. Collection = `clouddms_e5_` + SHA256 UTF8 identity dạng hex thường; không alias trộn model.

`IVectorSearchService.SearchAsync(FileAccessContext,SearchRequest,float[],int limit,ct)` → `SearchBranchResult`. Candidates: FileId, DocumentVersion, GenerationId, ChunkOrdinal, Score, Source=`semantic`, ModelVersion; HasMore khi số point bằng limit. Caller Task 9 phải kiểm SQL current generation/document/model + Active/Clean + ACL, áp dụng FolderId, gộp chunk theo file, dedupe/refill theo budget. Vector payload không phải authority; adapter không áp dụng FolderId. Query embedding quá 512 token trả lỗi contract để Task 9 xử lý degraded, không cắt query ngầm.

Options: `Embedding:Endpoint=http://127.0.0.1:8001/`, `BatchSize=16` (1..16), `TimeoutSeconds=120` (1..120); `Qdrant:Endpoint=http://127.0.0.1:6333/`, `TimeoutSeconds=120`, `ApiKey` tùy chọn qua secret environment; `SearchIndex:TokenizerEndpoint` giữ default. Compose dùng DNS embedding/qdrant, container embedding 2 CPU/2 GiB/read-only/user65532, Qdrant 2 CPU/2 GiB. Đây là cấu hình ban đầu chưa đo. Deadline toàn search 10 giây do Task 9 truyền cancellation; indexing batch có thể lâu hơn. Task 11 cần hoàn thiện deployment/secrets hiện hữu ở compose ngoài phạm vi task này.

## File thay đổi

Application: `Common/Interfaces/IEmbeddingService.cs`, `IVectorSearchService.cs`.
Infrastructure: `Search/EmbeddingOptions.cs`, `InternalEmbeddingService.cs`, `QdrantVectorSearchService.cs`, `QdrantDocumentIndexSink.cs`, `HttpTextChunker.cs`, `DocumentIndexOptions.cs`, `DependencyInjection/DependencyInjection.cs`.
Dịch vụ: `services/embedding/{server.py,preload.py,model-manifest.json,requirements.lock,Dockerfile,.dockerignore,README.md}`.
Repo: `.dockerignore`, `.gitignore`, `docker-compose.yml`; báo cáo này. Không sửa test/controller ledger Task 2/plan.

## Tự review và compilation

Đã đọc kiến trúc/bảo mật, hợp đồng Task 7 và đoạn spec/design liên quan; tự rà soát model/prefix/special count, overlap/progress, tenant filter, exact-generation cleanup, bounded JSON/batch, idempotency và không publish trước durable writes. Sửa DI do Infrastructure không tham chiếu Microsoft.Extensions.Http: dùng HttpClient singleton/disposable theo cấu trúc hiện có, không thêm dependency. Sửa body reader để kiểm deadline giữa các lần đọc, thêm watchdog cho native inference, thêm timeout/input cap tại HttpTextChunker. Chuẩn hóa file mới UTF8 không BOM.

Lệnh `dotnet build src/CleanArchCqrs.API/CleanArchCqrs.API.csproj --no-restore --verbosity quiet`:
- Lần đầu: 2 lỗi CS1061 AddHttpClient + 3 warning baseline.
- Sau sửa: exit 0, Build succeeded, **0 lỗi/3 warning**, thời gian 2,16 giây. Warning baseline NU1504 Hosting.Abstractions trùng và hai CS0618 GoogleCredential.FromStream.
- `git diff --check`: exit 0 (chỉ cảnh báo Git LF/CRLF). Không tuyên bố test pass.
- Host không có Python runnable (`python ...preload.py` báo Microsoft Store alias), chưa compile Python. Chuyển sang Copy-Item assets đã hash từ cache; không chạy inference. Docker image/dependency lock chưa build/resolution kiểm chứng.

## Nghĩa vụ test hoãn và giới hạn

Sau Task 11: build Python image/resolve pinned dependencies và kiểm syntax/startup offline; hash model/tokenizer mismatch, không runtime download/remote code/pickle; prefix query/passage đa ngôn ngữ, 512 boundary, normalization/dimension/model mismatch; chunk nguồn dài/Unicode/offset lặp/whitespace/overlap/partial/no silent loss; max body/chunked transfer/timeout/watchdog/concurrent request/memory cap; HttpClient malformed/oversized response/cancellation; Qdrant thật collection create race/dimension mismatch, mandatory tenant filter, stale generation/SQL revalidation, retry sau batch fail, stage/delete idempotent, exact-generation migration cleanup/cross-tenant isolation; end-to-end Task 7 tokenizer→embedding→stage→publish; latency CPU/Recall và search degradation dưới tải. Không có số latency/Recall hoặc chứng minh resource caps đủ từ runtime.

Dịch vụ single-request có thể làm query chờ sau indexing; Task 9 phải giữ deadline/degraded và giai đoạn benchmark quyết định tách replica query/index nếu cần. Cleanup namespace phải được dành riêng cho ứng dụng; không áp dụng cho collection ngoài namespace. Resource caps, package lock và Docker build hiện mới review tĩnh, cần kiểm chứng sau Task 11.

Nguồn chính thức đã đối chiếu: [E5 model card](https://huggingface.co/intfloat/multilingual-e5-small/raw/main/README.md) cho prefix/masked pooling/normalize/512; [Qdrant points](https://qdrant.tech/documentation/manage-data/points/) và [filtering](https://qdrant.tech/documentation/search/filtering/). Revision và image digest theo brief controller đã xác minh.
