# Task 9 — Hybrid Search, cursor và danh sách metadata

BASE: `1643dab`. Đã triển khai production; chưa kiểm chứng runtime. Theo yêu cầu người dùng, không viết/chạy test, không khởi động API/service, không truy cập DB, không integration/benchmark. Giữ nguyên test hiện có.

## Đã triển khai

- `GET /api/files/search?q=...&mode=hybrid&folderId=...&pageSize=20&cursor=...`: mode `keyword`, `semantic`, `hybrid`; query 1–1.000 ký tự, page size 1–100; response trực tiếp `items/nextCursor/modeUsed/degraded/warnings`, không bọc `Result`. Item: fileId/name/contentType/sizeBytes/documentVersion/score/sources/snippet; không URL, storage key hoặc cấu hình provider.
- RRF k=60, hai nhánh cùng trọng số. Gộp chunk thành một vote/file/nhánh trước đánh thứ hạng; tie-break file ID, chunk ordinal. SQL tenant/folder/ACL/active/clean/current version/current generation/model/ordinal kiểm trước ranking; batch CheckMany thêm bước phân quyền hiện hành. Keyword metadata có generation/ordinal null được chấp nhận; content match phải đúng generation hiện hành.
- Nhánh semantic gọi embedding/Qdrant trong deadline riêng 4 giây, toàn handler 10 giây. Hybrid chỉ degraded khi exception phát sinh trong hai lời gọi semantic; khi đó bỏ toàn bộ evidence semantic và trả keyword + `SEMANTIC_UNAVAILABLE`. Semantic-only trả 503. MySQL/ACL/Redis lỗi không fallback; cancel từ request/timeout toàn cục không biến thành degraded.
- Snapshot ban đầu hữu hạn: initial + tối đa 5 refill, window 32 tăng gấp đôi; keyword dùng offset, vector lấy top-limit tăng nghiêm ngặt. Tổng capacity yêu cầu cả hai nhánh tối đa 1.000; vector trả lại prefix cũ vẫn bị tính vào budget. Dừng khi HasMore false, hết budget hoặc không đủ budget tăng vector window. Không tiếp tục query vô hạn để cố đủ trang và không công bố số candidate bị loại.
- Snapshot ranking bất biến trong 5 phút. Redis chỉ giữ ID/version/generation/ordinal/model/source/score và hash ràng buộc, không query/text/snippet/URL. Data Protection bảo vệ cả cursor lẫn cache với purpose khác nhau. Cursor ràng buộc user, phiên đăng nhập, tenant, query đã trim, mode, folder, page size, model, position, expiry. Redis thiếu session/tamper/expiry bị từ chối; Redis outage fail-closed.
- Mỗi trang kiểm lại SQL và CheckMany trên phần snapshot còn lại. Chỉ sau grant mới đọc metadata và snippet MySQL tối đa 320 ký tự, bỏ control char, không HTML highlighting. Kiểm generation lần cuối và AssertStillValid cả trang + lookahead; chỉ phát nextCursor nếu lookahead hiện hành đã được cấp quyền. Context strict kiểm cuối cả với trang rỗng, expiry kiểm trước trả. Cursor không cấp URL đọc.
- SQL chunk lookup chỉ lấy cặp generation/ordinal cần thiết (batch 200), không tải toàn bộ text/chunk của candidate. Giá trị động trong predicate dùng parameter; snippet lấy substring ở SQL.

## Đường danh sách cũ và hợp đồng tương thích

- GetTenantFiles dùng strict resolver và shared SQL ACL; không ưu tiên tenant/role claim cũ. Tenant khác bị từ chối; không có admin bypass content. Áp dụng folder, tên LIKE đã escape `%/_/\\`, thứ tự ổn định, offset/page size; batch CheckMany trước metadata, AssertStillValid trước trả.
- Giữ DTO `TenantStorageSummaryDto/FileEntryDto`; storage key trả chuỗi rỗng, hash và provider từng file null. Summary quota tenant vẫn giữ hợp đồng hiện hữu. **TotalCount chỉ là số item được trả trong trang, không còn tổng toàn tenant**; thêm `HasMore` từ lookahead đã kiểm quyền để client chuyển pageNumber. Không suy số trang từ TotalCount.
- Trash metadata tách khỏi content ACL: chỉ owner hoặc quyền hiện hành `TenantAdmin` + `file.metadata.read` do evaluator quyết định; không dùng JWT role, không cho SystemAdmin truy cập tenant khác. SQL bắt buộc tenant/IsDeleted/Binned, hỗ trợ department/name/page. So sánh context stamp trước/sau và SQL owner/document/authorization version trước trả; không cấp nội dung file rác.
- Trash giữ DTO hiện hữu, provider name null; **TotalCount và TotalTrashSizeBytes chỉ tính trang trả về**, thêm HasMore từ lookahead đã revalidate. Không công bố tổng candidate/thùng rác của người khác. PageSize mặc định 20/tối đa 100; pageNumber không hợp lệ/overflow bị từ chối.
- Controller ba route dùng Authorize danh tính; quyền nội dung/metadata nằm ở handler theo nguồn chuẩn, không để policy claim cũ thay ACL. Cả ba có no-store, cancellation, timeout 10 giây và mã lỗi đã làm sạch; không phản hồi exception/dependency payload.

## Rate limit, log và bàn giao Task 11

- Bật middleware limiter sau authentication/authorization. `file-search` 30 request/phút/user, auth 10/phút/IP, upload 10/phút/user, queue 0; 429 no-store. Auth/upload đã có annotation trước đó nhưng thiếu đăng ký; controller đồng ý đăng ký khi bật middleware. Đây là giá trị khởi đầu, limiter hiện theo process, chưa benchmark; Task 11 cần quyết định config/proxy/distributed policy.
- Marker `ISensitiveReadRequest` chặn LoggingBehavior ghi raw exception của search/list/trash; vẫn ghi loại request, correlation, duration. Hosting Diagnostics chỉ từ Warning để không ghi GET q/cursor ở request-start. Task 11 phải loại query string khỏi reverse proxy/APM/access log, giữ EF sensitive logging tắt.
- **Task 11 bắt buộc cấu hình Data Protection key ring dùng chung/bền vững và bảo vệ key khi lưu**, application isolation phù hợp cho các replica. Hiện dùng AddDataProtection mặc định; mất/khác key sẽ fail-closed cursor/cache chứ không fallback. Redis dùng cùng kết nối/circuit bảo mật, key namespace `clouddms:search:session:v1:*`, SET NX TTL; triển khai ACL Redis cần cho phép GET/SET namespace này. Không sliding TTL.
- Model chính xác: `intfloat/multilingual-e5-small@614241f622f53c4eeff9890bdc4f31cfecc418b3`; Application SearchPolicy pin cùng producer/adapter. Đổi model phải đổi các pin đồng bộ và invalidate search sessions.
- Controller đã xác nhận snapshot bounded/cách tính budget kể cả vector lặp; quyền metadata owner/admin nêu trên; chấp thuận page-local totals nhưng yêu cầu chỉ dẫn phân trang. Task 9 thêm HasMore cho mục đích đó.

## File thay đổi

- Application: `Common/Interfaces/{ISearchSessionStore,ISensitiveReadRequest}.cs`, `Common/Models/Files/SearchSession.cs`, `Common/Behaviors/LoggingBehavior.cs`.
- Application: `Features/Files/Queries/SearchFiles/{SearchFilesQuery,SearchFilesQueryHandler,SearchCandidateReader,ReciprocalRankFusion}.cs`; `Queries/GetTenantFiles/{GetTenantFilesQuery,GetTenantFilesQueryHandler}.cs`, `Queries/GetTrashFiles/GetTrashFilesQuery.cs`; `DTOs/{FileEntryDto,TrashFileItemDto}.cs`.
- Infrastructure: `Search/SearchSessionStore.cs`, `Security/RedisSecurityClient.cs`, `DependencyInjection/DependencyInjection.cs`, `.csproj` thêm framework reference Data Protection.
- API: `Controllers/{FilesController,FileSearchHttpResult}.cs`, `Program.cs`; báo cáo này. Không sửa/commit thay đổi controller đang giữ trong task-2-report hoặc plan.

## Rà soát và compilation

Đã đọc brief/shared context/Task 8 report/kiến trúc/bảo mật và phần search design/spec; tự rà response/cache/log không text nhạy cảm/URL, cursor binding, stale generation, folder, grants thay đổi giữa trang, scope claim, timeout/fallback, budget tiến triển, metadata rác và pagination overflow. Trong self-review bổ sung log scrubbing, lookup chunk chính xác thay vì tải mọi ordinal, HasMore cho danh sách cũ, bắt rõ InvalidDataException của adapter và expiry cuối request.

Lệnh: `dotnet build src/CleanArchCqrs.API/CleanArchCqrs.API.csproj --no-restore --verbosity quiet`.

- Hai lần đầu sau các đợt sửa: exit 0, 0 lỗi/3 warning baseline (NU1504 Hosting.Abstractions trùng, hai CS0618 GoogleCredential.FromStream).
- Lần incremental kế tiếp: exit 0, 0 lỗi/1 warning NU1504, 1,08 giây. Không diễn giải việc incremental không in CS0618 là đã sửa warning.
- Build cuối sau expiry guard: exit 0, Build succeeded, 0 lỗi/1 warning NU1504, 1,04 giây. `git diff --check` exit 0, chỉ cảnh báo chuyển LF/CRLF. Không có bằng chứng TDD/test/runtime trong task này theo override.

## Hoãn kiểm chứng sau Task 11

Unit: RRF k=60/weights/tie/chunk dedup; keyword metadata-null/body generation; cả ba mode/empty query/Unicode/limits; Qdrant malformed/model/tenant/folder/generation/docversion/scan/ordinal rejection; semantic timeout/HTTP/contract fallback chỉ hybrid; SQL/ACL/Redis/session failure tuyệt đối không fallback; cancellation và timeout tổng.

Cursor/session: tamper/user/session/tenant/query/mode/folder/pagesize/model/expiry/cache loss/key rotation; không query/text/URL trong cache; replay ổn định; revoke/team/inherited ACL/content version/generation đổi giữa trang và trong lúc đọc snippet; lookahead bị revoke không phát nextCursor; expired trong request; snapshot hết budget không lặp/không duplicate; vector top-limit HasMore không gây loop.

Integration: MySQL translation cho captured OR predicates, substring/LIKE/FTS; MySQL + Qdrant + Redis thật; Redis circuit/cache TTL và authorization pending; startup DI/Data Protection/rate policies; q/cursor không trong app/proxy/APM log; 429/400/401/409/503/504 không detail nhạy cảm; no-store; request disconnect.

Legacy: JWT tenant cũ sau transfer, unshared same-tenant, admin không implicit content, folder/name/paging/escape/overflow, phiên bị thu hồi giữa metadata và trả; trash owner vs explicit admin vs role claim giả, không cross-tenant, Binned→Active/version đổi trước trả. Client cần chuyển pagination sang HasMore và hiểu page-local totals; test cũ có constructor handler thay đổi cần cập nhật trong giai đoạn test.

Giới hạn: snapshot hữu hạn có thể không tìm tới match sâu dù kho còn file, đặc biệt vector nhiều chunk; không trả thống kê candidate để giải thích thiếu kết quả. ACL graph hiện do Task 3 tải theo tenant; timeout 10 giây giới hạn request nhưng chi phí SQL cần benchmark. Snippet hiện lấy đầu chunk, chưa highlight đoạn trùng query. Chưa có số latency/Recall, chưa chứng minh runtime/translation/distributed setup đúng.
