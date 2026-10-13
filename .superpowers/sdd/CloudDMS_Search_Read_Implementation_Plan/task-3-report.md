# Báo cáo Task 3 — ACL chung và mutation

BASE: `67e1583`; nhánh `feat/search-read`, triển khai tại workspace hiện có theo lựa chọn người dùng. Chưa đánh dấu qua review. Không sửa thay đổi riêng trong `task-2-report.md`.

## Đã triển khai

- `AccessPolicyEvaluator` hợp tất cả grant còn hạn theo từng action. Viewer đọc; Editor thêm cập nhật; Owner thêm chia sẻ/xóa. `ShareGrant.CanShare` mặc định false; `CanDelete` của share cũ không tự cho phép chia sẻ. Không gộp expiry của các quyền.
- `IFileAccessService.CheckManyAsync` trả dictionary các file có quyền đọc; đọc envelope Redis trước/sau batch MySQL, kiểm generation, session/user/scope. `CheckAsync` hỗ trợ action mutation; `AssertStillValidAsync` truy vấn lại và so document/authorization version, stamp, fingerprint ACL. Pending/thiếu snapshot/Redis lỗi chặn; revoke hoặc expiry giữa check/assert chặn.
- `IFileAclQuery` là nguồn ACL MySQL dùng chung: owner, share cũ, grant trực tiếp, team tồn tại với membership hiện tại, grant từ ancestor cùng tenant. Cây có cycle hoặc parent thiếu/khác tenant bị loại kể cả owner. Mỗi query/join tenant-bearing dùng tenant server và `IgnoreQueryFilters` để tránh model-cache scope cũ. Team hoạt động được hiểu là team và membership đang tồn tại; chưa có toggle inactive trong schema.
- Đọc chỉ được cấp khi `FileContentVersion` của đúng `DocumentVersion` có enum `FileScanStatus.Clean`; chuỗi `FileEntry.ScanStatus="Clean"` không làm chứng cứ. Scope SQL đọc áp dụng cùng graph/grant và điều kiện version/scan/lifecycle.
- Grant file/folder upsert/revoke, add/remove team member và move folder qua handler Application, global coordinator/idempotency và audit. File ACL đòi Owner relation + `file.share`; folder ACL/move đòi Owner grant kế thừa/team/direct + `file.share`, kể cả Owner không phải admin. Move đòi Owner ở nguồn và đích. Admin không có Owner không được dời/chia sẻ folder để tự có nội dung. Team membership đòi TenantAdmin + `user.manage`; quyền quản trị này không tự cấp đọc file.
- Callback trong coordinator kiểm lại envelope raw (generation, session block/version), DB user/session/scope, ACL fingerprint/version và cây thư mục hiện tại. Global pending của chính đường coordinator được kiểm theo version kế tiếp; không dùng evaluator để bỏ qua pending cho đọc nội dung.
- Sửa first-use scope: bootstrap không có DB scope dùng version 0, coordinator nay cũng tạo version 0 rồi tăng lên 1; trước đây tạo 1 rồi nhảy lên 2 khiến kiểm version callback từ chối mutation đầu tiên. Hai fixture coordinator cũ đã được đổi snapshot Redis không có DB scope về version 0.
- Delete/restore/purge cũ dùng cùng dịch vụ mutation. Tăng `AuthorizationVersion`, giữ `DocumentVersion`, bỏ active search generation. Soft delete không hoàn quota. Purge giữ tombstone `Purging`, hoàn quota logic đúng một lần bằng `QuotaReleasedAt`, không gọi storage trước commit.
- Sự kiện document cùng transaction: `DocumentBinned`, `DocumentRestored`, `DocumentDeleted` (chỉ purge). Payload chứa ID/version/lifecycle, không toàn văn/storage key/bí mật. Worker cleanup vật lý thuộc Task 7.
- Replay idempotency xác thực session bằng DB + Redis, cho phép pending chỉ để trả receipt cũ thuộc đúng actor, hash payload đúng; trả ID/version/status trước khi kiểm trạng thái file đã xóa. Session blocked hoặc Redis unavailable không được replay. Grant đã hết hạn vẫn replay receipt cũ; chỉ mutation mới kiểm expiry. Khác payload cùng key conflict.

## Hợp đồng cho task sau

- `FileAccessGrant` thêm optional `string AclFingerprint = ""`. Consumer giữ cả grant để assert, không chỉ giữ stamp.
- `IFileAclQuery.ResolveManyAsync(context, ids, now, ct)` trả `CurrentFileAcl(Resource, DocumentVersion, VerifiedClean, Grants, Fingerprint)`; `ResolveFolderAsync` dùng cùng semantics để quản lý folder.
- `ReadScopeAsync(context, now, ct)` trả `IQueryable<FileEntry>` đã lọc tenant/ACL/scan/version. Đây chỉ là scope ứng viên: Task 6 bắt buộc `CheckManyAsync` rồi `AssertStillValidAsync` trước response; không viết bộ luật ACL SQL khác. Dùng thời gian server của snapshot ở luồng search, không nhận tenant từ client.
- `IFileAccessContextResolver.ResolveContextForMutationReceiptAsync` chỉ dùng trước lookup receipt có chủ sở hữu; không dùng cấp nội dung hay mutation mới. `ResolveContextAsync` vẫn strict pending/readiness.
- Action `file.restore`/`file.purge` nội bộ đòi quyền RBAC `file.delete` và Owner relation trên file Binned. `bin.purge` là ngoại lệ quản trị metadata/lifecycle TenantAdmin có permission, cùng tenant; không cấp đọc nội dung.
- `POST /api/file-access/mutations`: body `Kind, ResourceId, PrincipalId, PrincipalKind, Role, ExpiresAt, ParentFolderId`; không có tenant. Delete/restore/permanent endpoints và endpoint mới đòi `Idempotency-Key`, trả operation DTO 202 khi pending/200 khi applied; location `/api/operations/{id}`. Handler kiểm permission hiện tại thay vì permission claim cũ. Lỗi: 400 invalid, 401 session, 403 ACL/version, 409 conflict/busy, 503 security unavailable. GET operation hiện hữu giữ giới hạn initiator của coordinator.
- Không tạo thêm projection ACL cạnh tranh. Builder/bootstrap/writer session hiện có giữ nguyên; đường mới dùng envelope user/session/scope Redis và ACL MySQL hiện tại. Consumer nội dung mới phải dùng `IFileAccessService`, không dùng share-only Redis file snapshot cũ làm ACL đầy đủ.
- Không thêm endpoint tạo team/folder hoặc suy diễn Owner từ DepartmentId. Folder cũ chưa có Owner grant cần provisioning hợp lệ; nếu Task sau thêm create, phải cấp Owner creator cùng transaction.

## RED → GREEN thực tế

Các lệnh dưới chạy tại workspace, `--no-restore --verbosity minimal`. Compile-error ban đầu do type mới chưa có hoặc typo không được tính là behavioral RED.

| Project và filter cho `dotnet test` | RED trước sửa | GREEN đã chạy |
|---|---|---|
| `tests/CloudDms.UnitTests/CloudDms.UnitTests.csproj --filter FullyQualifiedName~EffectiveGrantTests` | 0 pass/2 fail: expiry Editor che Viewer; granted Owner không share | 2 pass/0 fail |
| Unit project trên, `--filter FullyQualifiedName~FileAccessServiceTests` | 0 pass/2 fail với stub: trả 0 thay 200, pending không throw | 2 pass/0 fail |
| Unit project trên, `--filter FullyQualifiedName~BinnedOwnerCanPerform` | 0 pass/2 fail: restore/purge owner Binned bị Deny | 2 pass/0 fail |
| `tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj --filter FullyQualifiedName~FileAclQueryTests` | 0→4 grant cases fail; sau mapping 4 pass/1 cycle fail | 5 pass/0 fail |
| Legacy project trên, `--filter FullyQualifiedName~DeleteFile_ShouldRefund` | 0 pass/1 fail: stub không chuyển bin | Sau triển khai, filter kết hợp dưới đạt |
| Legacy project, `--filter FullyQualifiedName~FileMutationTests` | 0 pass/4 fail: replay/lifecycle, admin content, stale scope, folder callback | Filter `FullyQualifiedName~FileMutationTests\|FullyQualifiedName~DeleteFile_ShouldRefund\|FullyQualifiedName~ReceiptContextAllowsPending`: 7 pass/0 fail ở thời điểm đó |
| Legacy project, `--filter FullyQualifiedName~FileOwnerGrantAndTeamMembership` | 0 pass/1 fail: chưa có mutation | Đạt trong filter `FullyQualifiedName~FileMutationTests\|FullyQualifiedName~FileAclQueryTests`: 11 pass/0 fail ở thời điểm đó |
| Legacy project, `--filter FullyQualifiedName~ReceiptContextAllowsPending` | 0 pass/1 fail: resolver strict chặn receipt pending | Đạt trong filter kết hợp 7 test |
| Legacy project, `--filter FullyQualifiedName~StructureAdminCannotMove` | 0 pass/1 fail: admin move folder không Owner vẫn được | Đạt trong filter kết hợp 11 test |
| Legacy project, `--filter FullyQualifiedName~NonAdminOwnerCanManage` | 0 pass/1 fail: bắt buộc TenantAdmin dù có Owner | 1 pass/0 fail |
| Legacy project, `--filter FullyQualifiedName~SerializedCallbackRejects` | 0 pass/2 fail: generation thay/session blocked vẫn mutate | 2 pass/0 fail |
| Legacy project, `--filter FullyQualifiedName~AppliedGrantReplay` | 0 pass/1 fail: replay sau grant expiry bị ArgumentException; một lần patch newline chưa áp dụng vẫn fail | 1 pass/0 fail |

Integration thực tế được controller chạy do shell agent không truy cập Docker pipe. Fixture tạo container/DB MySQL và Redis tạm; không DB người dùng. Lần chạy agent bị fixture-init Access denied không tính behavioral RED/pass.

```powershell
dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~FileAclIntegrationTests --verbosity minimal
```

Controller lần 1: 1 pass/1 fail ở fixture `Duplicate entry 'hash'` của token hash; đổi hai session fixture thành hash độc lập. Lần 2: 1 pass/1 fail hành vi ở `AssertContextCurrentAsync`, first global scope Redis 0 → coordinator marker 2. Đây là RED cho sửa first-use version, không nới session checks.

```powershell
dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~FileAclIntegrationTests|FullyQualifiedName~AuthorizationMutationCoordinatorTests' --verbosity minimal
```

Controller sau sửa first-scope: exit 0, 4 pass/0 fail/0 skip, 34 s. Test SQL thực chạy 520 file tenant A + 1 file tenant B qua hai context liên tiếp; batch đúng 11 SELECT (2 graph + 3/chunk 256), scope SQL và batch cùng grant Viewer còn hạn sau Editor hết hạn. Test mutation dùng coordinator/Redis thật kiểm pending replay/conflict, revoke, delete/purge replay và session block trước DB revoke.

Test cuối mở rộng thêm upsert thay Viewer → Editor qua unique index rồi revoke. Regression cuối do controller chạy:

```powershell
dotnet test tests/CloudDms.IntegrationTests/CloudDms.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~FileAclIntegrationTests|FullyQualifiedName~AuthSessionIntegrationTests|FullyQualifiedName~RedisAuthorizationTests|FullyQualifiedName~AuthorizationMutationCoordinatorTests|FullyQualifiedName~RedisAvailabilityTests' --verbosity minimal
```

Output: exit 0; `Passed: 24, Failed: 0, Skipped: 0, Total: 24`, duration 1 m 13 s. Có upsert/revoke, unique index thật, pending replay/conflict, blocked session trước DB revoke, first scope version, Lua/evaluator stamps, session/logout/bootstrap/cancellation regressions. Warning NU1504 baseline vẫn còn. Không chạy lại suite sau kết quả này.

## Kiểm tra cuối cục bộ

```powershell
dotnet test tests/CloudDms.UnitTests/CloudDms.UnitTests.csproj --no-restore --verbosity minimal
dotnet test tests/CleanArchCqrs.Tests/CleanArchCqrs.Tests.csproj --no-restore --verbosity minimal
dotnet build src/CleanArchCqrs.API --no-restore --verbosity minimal
git diff --check
```

- Unit: exit 0, 38 pass/0 fail/0 skip.
- Legacy: exit 1, 44 pass/3 fail/0 skip. Ba lỗi baseline Task 5: `Handle_WhenUserHasNoQuota_ShouldRejectImmediately` đi tới null storage; `Handle_WhenCallerIsSystemAdmin_ShouldRejectImmediately` thông báo thiếu tenant; `Handle_WhenFileSizeExceedsUserQuota_ShouldRejectWithQuotaExceededMessage` lệch thông báo quota. Không sửa upload trong Task 3.
- API build: exit 0, 0 error; NU1504 baseline duplicate Hosting.Abstractions. CS0618 GoogleCredential.FromStream hiện khi rebuild Infrastructure; baseline dành Task 11.
- Whitespace: `git diff --check` exit 0 chỉ kiểm file tracked, không kiểm các file mới untracked. Sau staging, controller chạy `git diff --cached --check` và phát hiện 8 file mới còn blank lines EOF; đã trim các file đó, cần re-add rồi kiểm staged lần cuối. Formatter agent bị quyền filesystem, controller formatter scoped exit 0; service ACL/mutation đã tách statement/block để review. Literal tiếng Việt mới lưu UTF-8.

## Giới hạn / dự kiến

- Global barrier chặn ngắn hạn toàn hệ thống khi dựng projection. ACL đọc load graph folder và grant liên quan trong tenant mỗi batch; số round-trip theo chunk, chưa benchmark RAM/latency của tenant lớn. Không tuyên bố production scale.
- Task 4/6 phải nối read/download/search qua dịch vụ ACL mới. Các endpoint nội dung cũ chưa đổi ở Task 3; Redis projection file/share cũ chưa biểu diễn đầy đủ team/ancestor, nên không phải nguồn cấp quyền cho API nội dung mới.
- Cleanup storage/index/preview vật lý chưa có trong Task 3. Task 7 xử lý tombstone/event và retry; Task 5/7 giữ `QuotaReleasedAt`, không hoàn quota hai lần. Logical provider capacity có thể được hoàn trước physical cleanup, cần reconciliation và chính sách admission sau này.
- Không sửa quota alias migration baseline hoặc `Roles.TenantId` compatibility. Không chạy seed/migration trên DB người dùng.
