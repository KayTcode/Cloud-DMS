# Task 2 — Session identity

## Đã triển khai

- `RefreshToken.Id` là `SessionId` chuẩn; JWT bắt buộc nhận ID và phát claim `sid`. Login, Google login, nhận lời mời và refresh đều lưu refresh token trước khi cấp JWT qua `IAuthSessionService`.
- Nhận lời mời trước đây trả refresh token chưa lưu; nay lưu hash và phiên thực tế. Login ghi `LastLoginAt`.
- Refresh chặn snapshot phiên cũ trước khi lưu rotation; `RevokedAt` là concurrency token EF, hai refresh đồng thời chỉ một transaction lưu thành công. Giữ chính sách Result của monolith; phiên hết hạn/revoked hoặc user inactive/unconfirmed bị từ chối.
- `IFileAccessContextResolver.ResolveContextAsync(CancellationToken)` lấy actor từ current user đã xác thực, kiểm phiên persisted và snapshot Redis, lấy tenant từ snapshot chuẩn. Không nhận tenant/user/session từ request, không fallback permission claim. Task 3 sẽ triển khai ACL.
- `AuthorizationProjectionBuilder` dùng chung user/file/share/session wire shapes cho mutation và rebuild. `SecurityProjectionBootstrapper` dựng toàn bộ snapshot trong transaction MySQL RepeatableRead, bảo toàn index scope.
- Rebuild on-demand khi cấp JWT: lease Redis 60 giây, state rebuilding, mutation scripts bị chặn trong lease; publish nguyên tử chỉ khi còn đúng owner. Kiểm run ID trước/sau, thiếu/lỗi không phát JWT.
- Scope pending từ MySQL giữ version/operation ID; barrier Redis chưa có trong DB hoặc mới hơn không được xóa/ghi đè. Outbox có thể publish sau khi dựng lại. Marker phiên bị chặn không được rebuild/mutation làm sống lại.
- Không migration/schema mới, không thay snapshot Product, không chạy DB người dùng.

## Kiểm chứng

- RED JWT: `AccessTokenMustNotInventSessionIdentity` thất bại “No exception was thrown” trước khi thêm yêu cầu session persisted.
- RED Redis thật: index scope trống và rebuild xóa barrier orphan; hai assertion thất bại đúng hành vi, sau đó sửa bootstrap.
- GREEN auth tập trung: 16 pass, 0 fail, 0 skip; test bổ sung xác thực JWT bằng signing key rồi đọc `CurrentUserService.SessionId`: 1 pass.
- GREEN tích hợp MySQL 8.4 + Redis 7.4: 4 pass, 0 fail, 0 skip; JWT/evaluator Allow, revoke/restart/rebuild không phục hồi phiên, barrier orphan giữ fail-closed, concurrent refresh một DB winner, scope DB pending được dựng lại và outbox chuyển Applied.
- Docker trong sandbox bị từ chối named pipe; rerun có escalation. Fixture dùng container/DB tạm, không DB đang dùng.
- Các test resolver mở rộng được bổ sung sau triển khai; không tuyên bố mọi dòng triển khai đều có RED trước. Test JWT và hai bug bootstrap có chu kỳ RED/GREEN thực tế.
- Toàn bộ `CloudDms.UnitTests`: 32 pass, 0 fail, 0 skip (`dotnet test ... --no-restore --verbosity minimal`).
- Toàn bộ `CleanArchCqrs.Tests`: 27 pass, 3 fail, 0 skip. Ba lỗi upload không sửa trong task session: `Handle_WhenUserHasNoQuota_ShouldRejectImmediately` (đi tới null storage thay vì quota guard); `Handle_WhenCallerIsSystemAdmin_ShouldRejectImmediately` (thông báo missing tenant thay vì SystemAdmin); `Handle_WhenFileSizeExceedsUserQuota_ShouldRejectWithQuotaExceededMessage` (thông báo quota khác assertion). Handler upload không thay đổi.
- Regression tích hợp filter `FullyQualifiedName~Authorization|FullyQualifiedName~Security`: 22 pass, 1 fail, 0 skip. Lỗi đã biết `MigrationBackfillsLegacyOwnershipAndKeepsDeletedFilesClosed`: quota alias Expected 100 / Actual 0, dành Task 5.
- Test tập trung cuối (`FullyQualifiedName~AuthSessionIntegrationTests`): 6 pass, 0 fail, 0 skip; gồm login handler thật lưu hash/sid, evaluator, revoke+restart, refresh đồng thời, pending reconstruction+outbox, Redis stopped không cấp JWT và owner lease bị thay không được publish readiness.
- `dotnet build src/CleanArchCqrs.API --no-restore`: thành công; `git diff --check`: không lỗi whitespace.

## Giới hạn / việc sau

- Rebuild đầy đủ mỗi lần cấp JWT là cấu hình an toàn ban đầu, chưa benchmark; O(toàn bộ dữ liệu và số scope), Lua dùng KEYS trên namespace bảo mật của Redis standalone. Login đồng thời có thể fail-closed vì lease busy. Cần tối ưu publish session/worker bootstrap riêng sau benchmark; chưa tuyên bố production scale.
- Session hết hạn sau 7 ngày kể từ refresh mới; chưa có family absolute lifetime/replay revoke-all theo thiết kế SSO riêng. Reuse hiện bị từ chối, không tự thu hồi mọi session.
- Schema baseline Roles thiếu `TenantId`; test role được insert tham số hóa chỉ cột đang có. Chưa xác minh full startup/seed/admin Role entity trên DB mới; Task 11 cần kiểm tra/repair compatibility riêng.
- Baseline quota alias integration failure dành Task 5. Không sửa trong task này.
- Warning baseline NU1504 duplicate Hosting.Abstractions và CS0618 GoogleCredential.FromStream vẫn còn.
- Khi Redis restart, singleton client có thể fail-fast lúc reconnect; không cấp JWT trong khoảng này. Test reconstruction tạo kết nối mới sau restart để kiểm DB-to-Redis snapshot.

## Sửa theo review — vòng 1

- BASE `2f4c35c`: review phát hiện logout chỉ thu hồi DB và `cancel-change` bị bootstrap lock chặn, có thể giữ barrier không còn operation/outbox vĩnh viễn.
- RED thực tế trên MySQL/Redis: test login hai lần → `LogoutCommandHandler` → evaluator thất bại vì vẫn Allow; test gate begin mutation → bootstrap lease → callback rollback thất bại vì marker vẫn updating thay vì snapshot trước mutation. Kết quả 0 pass / 2 fail, đúng hai finding.
- Logout nay gọi `IAuthSessionService.BlockAsync` cho toàn bộ refresh session active của current user trước Revoke/SaveChanges, giữ chính sách logout hiện có. Marker blocked ngăn projection cũ làm sống lại phiên; test kiểm cả hai phiên bị từ chối trước/sau restart+rebuild.
- Cancellation được phép khi bootstrap giữ lease nhưng bắt buộc current marker updating, operation ID và version đúng. Trong cùng Lua, cleanup đánh dấu owner bootstrap hiện tại có snapshot đã thay đổi rồi khôi phục backup của operation; không xóa barrier lạ.
- `finish-bootstrap` từ chối owner bị invalidated trước khi ghi projection/readiness. Bootstrap giải phóng lease và recapture DB bằng transaction/lease mới, tối đa một retry; thay đổi lặp lại vẫn fail-closed. Marker invalidation TTL 60 giây đủ bao phủ thời gian còn lại của lease 60 giây và được cleanup khi finish/abort.
- GREEN hai test review: 2 pass / 0 fail / 0 skip. Test bổ sung dùng payload pending đã dựng trước cancel để kiểm finish trả snapshot-changed, owner/version sai không được cancel; test orphan thật và DB pending/outbox vẫn giữ.
- Xóa field/constructor `IJwtTokenService` không dùng trong login, invitation, refresh, Google; DI và tests chỉ dùng session issuer. Bộ auth/resolver sau refactor: 17 pass / 0 fail / 0 skip.
- Regression Lua/session (`AuthSessionIntegrationTests|RedisAuthorizationTests|AuthorizationMutationCoordinatorTests|RedisAvailabilityTests`): 22 pass / 0 fail / 0 skip. `git diff --check` không lỗi whitespace. Không chạy lại các suite ngoài phạm vi đã ghi kết quả baseline; không sửa SSO family hay lỗi Task 5/11.
