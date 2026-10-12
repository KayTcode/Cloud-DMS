# Thiết kế xác thực và phân quyền Cloud DMS

Ngày: 07/10/2026. Trạng thái: bản thiết kế chờ review; chưa triển khai.

## 1. Mục tiêu và hiện trạng

Thiết kế lại đăng nhập với Auth Service riêng, Kong OSS xác thực access token, DMS phân quyền bằng Redis. Quyền thay đổi có hiệu lực từ request tiếp theo sau khi thao tác cập nhật báo thành công. Khi Redis lỗi, không đọc quyền từ DB để thay thế.

Hiện trạng theo ARCHITECTURE.md, SECURITY.md và mã nguồn đã đọc:

- API .NET 8 hiện xác thực JWT Bearer; LoginCommandHandler kiểm tra mật khẩu BCrypt, IsActive và lấy quyền từ MySQL.
- JwtTokenService hiện phát HS256 với role/permission; PermissionPolicyProvider kiểm tra claim permission.
- Có User, Tenant, Department, Role, Permission, UserRole, RolePermission, FileEntry, Folder, FileShare và RefreshToken.
- FileShare có CanRead, CanWrite, CanDelete, ExpiresAt; FileEntry có OwnerId, TenantId nhưng chưa có DepartmentId sở hữu độc lập.
- Chưa có Auth Service riêng, Redis, luồng refresh/logout hoàn chỉnh hoặc Google login. Thư mục Gateway đang trống.
- Tenant isolation, bảo vệ mặc định endpoint và một số fallback danh tính hiện còn thiếu; thiết kế mới không giữ các fallback đó.

## 2. Quyết định đã thống nhất

| Nội dung | Quyết định |
|---|---|
| Gateway | Kong OSS. |
| Auth | Service riêng; sở hữu credential, Google identity, phiên và token. |
| DMS | Sở hữu hồ sơ người dùng, tổ chức, quyền và nghiệp vụ tài liệu. |
| Access token | JWT RS256, 5 phút, giữ trong RAM trình duyệt. |
| Refresh token | Cookie HttpOnly/Secure; rotation; phiên tối đa 7 ngày. |
| Sai mật khẩu | 5 lần trong 30 phút thì khóa mật khẩu 30 phút từ lần sai thứ 5; Google vẫn dùng được nếu tài khoản hoạt động. |
| Google | Chỉ tài khoản đã tồn tại; tự liên kết email do Google xác nhận phù hợp; email bên thứ ba cần chứng minh thêm. |
| Phân quyền | RBAC + PBAC (Policy-Based) + ReBAC; kiểm tra bằng dữ liệu Redis. |
| Quyền lâu dài | DB của DMS; Redis phục vụ kiểm tra quyền, không fallback DB. |
| Cập nhật quyền | Chặn phạm vi đang cập nhật; chỉ báo thành công khi Redis đã áp dụng. |
| Chia sẻ | Chỉ giữa người dùng cùng tenant, kể cả khác phòng ban. |
| Recycle bin | Thuộc phòng ban sở hữu file; vào bin vẫn chiếm quota, quyền chia sẻ tạm vô hiệu. |

## 3. Ranh giới kiến trúc

Client → Kong → Auth Service hoặc DMS API. Các request nghiệp vụ được Kong xác thực trước khi vào DMS. Luồng đăng nhập/refresh có cơ chế xác thực riêng tại Auth, không yêu cầu access token còn hạn.

Mỗi service .NET 8 giữ bốn tầng: API, Application, Domain, Infrastructure. Use case là command/query và handler trong Application; interface ở Application/Common/Interfaces; entity ở Domain/Entities; MySQL, Redis, Google, JWT và kho tệp tích hợp tại Infrastructure.

| Thành phần | Sở hữu |
|---|---|
| Auth DB | Credential, Google identity theo issuer + sub, refresh token dạng băm, phiên, trạng thái đồng bộ danh tính, outbox. |
| DMS DB | User profile, Tenant, Department, Role/Permission, quota, file/share/bin, nhật ký, phiên bản quyền, outbox. |
| Redis | Trạng thái phiên/tài khoản, bộ đếm sai/khóa mật khẩu, dữ liệu quyền và quan hệ, trạng thái đồng bộ/readiness. |

UserId là định danh ổn định giữa hai service. Hai service không cùng ghi bảng của nhau; giao tiếp nội bộ phải xác thực. DMS là nguồn chính cho trạng thái hoạt động của người dùng; Auth giữ bản sao để chặn đăng nhập/refresh.

Tạo người dùng: DMS tạo hồ sơ ở trạng thái chưa sẵn sàng đăng nhập, gửi yêu cầu tạo danh tính idempotent sang Auth; chỉ mở đăng nhập khi hoàn tất đồng bộ và Redis sẵn sàng. Không cung cấp đăng ký công khai tạo tài khoản ngoài quy trình quản trị/lời mời.

Vô hiệu hóa người dùng: chặn tại Redis trước, ghi DMS DB và outbox, đồng bộ Auth để chặn phát/refresh token và thu hồi phiên. Lỗi đồng bộ không được mở lại nghiệp vụ. Thay mật khẩu/reset credential thuộc Auth; quyền gọi use case quản trị do DMS kiểm tra và chuyển qua API nội bộ được xác thực.

## 4. Kong và danh tính xuống server — đề xuất kỹ thuật cần review

- Dùng JWT plugin tích hợp, RS256; private key chỉ ở Auth, public key ở Kong. Kong Consumer đại diện credential của bên phát hành, không phải từng UserId của DMS.
- Chỉ nhận token qua Authorization: Bearer; tắt nguồn token query/cookie. Route nghiệp vụ không bật anonymous.
- Cấu hình kiểm tra exp/nbf; bộ phát hành bắt buộc tạo các claim này. Pin phiên bản Kong trong kế hoạch triển khai và kiểm tra hành vi claim thực tế.
- Đề xuất claim riêng auth_key để chọn JWT credential qua key_claim_name, tách khỏi iss; đổi khóa bằng credential mới, giữ khóa cũ qua thời hạn token cũ.
- Kong chuyển nguyên Authorization đã kiểm tra xuống upstream, không thay token sau kiểm tra. Không dùng X-Consumer-ID làm UserId, không nhận X-User-ID do client gửi.
- DMS chỉ nhận request từ Kong qua mạng riêng và mTLS với chứng thư được cho phép; public traffic không thể gọi trực tiếp upstream. Xóa các header danh tính do client cung cấp tại gateway.
- Trên kênh đã xác thực Kong, adapter API đọc sub/sid từ chính JWT được chuyển tiếp, kiểm tra định dạng, iss, aud của DMS, exp/nbf, alg cố định và claim bắt buộc. Không giả định JWT plugin tự kiểm tra aud hoặc tự xuất sub thành header.
- Không có đường gọi nội bộ bỏ qua kiểm tra quyền. API quản trị nội bộ dùng danh tính service và policy riêng; Kong Admin API không public.

Đây là thiết kế dựa vào ranh giới tin cậy Kong → DMS, không cho phép chỉ giải mã JWT từ kết nối bất kỳ. Kiểm thử bypass và token nguồn khác nhau là điều kiện bắt buộc trước triển khai.

## 5. Đăng nhập mật khẩu và khóa Redis

1. Client gửi email/mật khẩu qua Kong tới Auth; áp dụng giới hạn lưu lượng theo IP bổ sung cho khóa theo tài khoản.
2. Auth kiểm tra Redis sẵn sàng và trạng thái khóa, xác minh credential và trạng thái tài khoản.
3. Sai credential: phản hồi chung, không phân biệt email tồn tại. Cập nhật bộ đếm nguyên tử, kể cả email không tồn tại; key không chứa email thô.
4. Đúng credential và không bị khóa: xóa bộ đếm sai, tạo phiên bền vững và trạng thái phiên Redis, đặt refresh cookie, trả access token. Không phát token trước khi trạng thái phiên Redis sẵn sàng.

Đề xuất làm rõ cửa sổ: dùng cửa sổ trượt 30 phút, loại lượt sai cũ rồi thêm lượt hiện tại trong một thao tác Redis nguyên tử; lượt thứ 5 tạo khóa TTL 30 phút. Request trong thời gian khóa không kéo dài TTL. Đồng bộ kết quả xác minh thành công/thất bại với trạng thái khóa để request đồng thời không vượt khóa.

Google thành công không xóa khóa/bộ đếm mật khẩu. IsActive=false chặn cả hai phương thức. Google thất bại không tăng bộ đếm sai mật khẩu. Lỗi Redis trả 503; không bỏ qua khóa hoặc đọc DB thay thế bộ đếm.

## 6. Google login

Auth thực hiện OIDC Authorization Code flow, PKCE S256, state và nonce dùng một lần có thời hạn. Callback kiểm tra chữ ký token, iss, aud, exp và nonce bằng thư viện phù hợp. Không tin email hoặc UserId do frontend tự gửi.

- Chỉ liên kết hồ sơ DMS đã tồn tại, đang hoạt động và hoàn tất đồng bộ danh tính.
- Lần đầu: đối chiếu email chuẩn hóa nhất quán với DMS. Gmail, hoặc Google Workspace có email_verified và hd phù hợp, đủ điều kiện tự liên kết; không suy ra Workspace chỉ từ đuôi email.
- Email bên thứ ba: cần mật khẩu DMS hợp lệ, không bị khóa; hoặc mã xác nhận gửi tới địa chỉ hiện tại. Mã dùng một lần, hạn ngắn, có giới hạn số lần thử và ràng buộc với giao dịch liên kết.
- Sau liên kết: nhận diện bằng issuer + sub; email đổi không tự chuyển liên kết sang người dùng khác. Ràng buộc unique ngăn một Google identity liên kết nhiều người dùng.
- Không ghi đè liên kết có sẵn bằng Google sub khác chỉ vì email giống nhau; phải từ chối và yêu cầu quy trình liên kết lại có xác minh.
- Không tạo tài khoản/quyền/tenant từ Google; không dùng access token Google để gọi DMS.

Callback không đưa token DMS lên URL. Đặt refresh cookie rồi redirect về frontend; frontend gọi refresh để nhận access token vào RAM. Toàn bộ đường đăng nhập này cũng yêu cầu Redis hoạt động.

## 7. Token, cookie và phiên

JWT chứa iss, aud, sub=UserId, sid, jti, iat, nbf, exp và định danh khóa; không chứa danh sách permission quyết định truy cập. Tenant/phòng ban hiện hành lấy từ Redis, không tin tenantId trong request hoặc thông tin token đã cũ.

Refresh token ngẫu nhiên, lưu băm tại Auth DB, gắn với session/family. Cookie HttpOnly, Secure, SameSite=Lax, không Domain; triển khai frontend/API cùng origin, Path giới hạn phù hợp các route refresh/logout. Không trả refresh token trong JSON. Phiên hết hạn tuyệt đối sau 7 ngày, rotation không kéo dài mốc này.

Refresh xác minh token, trạng thái tài khoản/phiên Redis; đổi token nguyên tử tại Auth DB, cập nhật Redis trước khi trả kết quả. Token cũ dùng lại thu hồi cả family; không phát token khi Redis lỗi. Lỗi giữa DB và Redis để phiên bị chặn tới khi hòa giải trạng thái, không phục hồi bằng token cũ.

Frontend gom refresh đồng thời, có phối hợp giữa các tab; không refresh vòng lặp khi gặp 401/503. Tải lại trang gọi refresh, không dùng localStorage/sessionStorage/IndexedDB để lưu token.

Logout thu hồi phiên bền vững và Redis, xóa cookie; chỉ báo thu hồi thành công khi hoàn tất. Nếu Redis lỗi, vẫn có thể xóa cookie cục bộ nhưng trả 503, không khẳng định phiên server đã thu hồi. Request tiếp theo sau logout thành công bị từ chối dù JWT còn hạn.

Refresh/logout và các thao tác dùng cookie kiểm tra Origin/CSRF token; SameSite không thay thế kiểm tra CSRF. CORS chỉ cho origin được cấu hình; mọi kết nối public dùng HTTPS. Không ghi credential/token vào log.

## 8. Mô hình quyền

| Vai trò | Quyền quản trị | Nội dung file |
|---|---|---|
| SystemAdmin | Hệ thống và người dùng theo phạm vi quản trị hệ thống. | Vai trò không cấp quyền đọc/tải/upload/sửa/xóa file người dùng. |
| TenantAdmin | Nhân sự, phòng ban, quota phòng ban; metadata/dung lượng/log trong tenant; purge bin của phòng ban. | Không cấp quyền đọc/tải/upload/sửa hoặc đưa file đang hoạt động vào bin. |
| DepartmentAdmin | Nhân sự và quota từng người trong phòng ban mình. | Quyền người dùng với file của mình; file người khác chỉ theo chia sẻ cụ thể. |
| User | Không có quyền quản trị từ vai trò này. | Sở hữu file hoặc quyền chia sẻ cụ thể. |

Quyền quản trị không được biến thành quyền nội dung. Đề xuất không cho gán đồng thời SystemAdmin/TenantAdmin với vai trò thao tác nội dung trong bản đầu để tránh cộng quyền vượt yêu cầu; đây là hạn chế cần review, không phải quyết định đã được người dùng chốt.

- RBAC: tập khả năng thực hiện hành động; tên quyền tách quản trị metadata/bin khỏi nội dung file.
- PBAC: điều kiện bắt buộc về trạng thái người dùng/phiên, tenant, phạm vi quản trị, trạng thái file, thời hạn chia sẻ và quota.
- ReBAC: owner, người được chia sẻ, quan hệ quản lý tenant/phòng ban. Cùng phòng ban không tự cấp quyền nội dung.

Ví dụ: đọc file = phiên hợp lệ AND có khả năng file.read AND cùng tenant AND file đang hoạt động AND (owner OR share.CanRead chưa hết hạn). Không được áp quy tắc này một cách máy móc cho purge: purge cần khả năng bin.purge, quan hệ TenantAdmin quản lý đúng tenant và file đã vào bin.

CanRead/CanWrite/CanDelete độc lập; được chia sẻ đọc không suy ra sửa/xóa/chia sẻ tiếp. Chỉ owner được chia sẻ lại trong bản đầu (đề xuất). Không bổ sung quyền kế thừa thư mục hay truy cập chéo tenant trong phạm vi này.

DMS bảo vệ mặc định endpoint; chỉ mở public bằng danh sách rõ ràng. Kiểm tra quyền tại use case Application trước tác dụng phụ và áp dụng cho cả danh sách, metadata, tải nội dung, share, background job và thao tác quản trị. Không chỉ kiểm tra ở controller.

## 9. Redis phục vụ phân quyền

Đề xuất các nhóm dữ liệu: readiness/generation toàn hệ thống; trạng thái user và scope; quyền hiệu lực theo UserId; scope tenant/phòng ban; trạng thái session; resource owner/tenant/department/lifecycle; share theo file và người nhận; version và marker đang cập nhật.

Mỗi request dùng sub/sid đã nhận qua ranh giới Kong, đọc dữ liệu Redis và đánh giá policy. Missing permission trên snapshot đầy đủ nghĩa là không có quyền; missing snapshot/readiness là dữ liệu chưa sẵn sàng, trả 503. Không tự nạp dữ liệu DB khi cache miss. API vẫn dùng DB cho metadata/nghiệp vụ sau kiểm tra, không đọc bảng quyền để quyết định truy cập.

Kiểm tra readiness, marker, trạng thái phiên và các version phải nhất quán. Đề xuất bản đầu Redis primary duy nhất có persistence, đọc primary; thao tác Lua/MULTI cần nguyên tử chạy trong cùng instance. Không đọc replica có độ trễ để quyết định quyền. Redis Cluster/HA mở rộng cần thiết kế lại ranh giới thao tác nhiều key và kiểm chứng mất ghi khi failover trước khi bật.

Đề xuất tách instance auth/security khỏi cache thông thường, maxmemory-policy=noeviction, timeout ngắn và circuit breaker; cấu hình persistence/backup. Không dùng TTL quyền rồi tự nạp DB. Key session hết hạn theo phiên; bộ đếm và khóa có TTL đúng nghiệp vụ. Hết hạn share được kiểm tra lúc đánh giá bằng thời gian server, không phụ thuộc việc key được dọn.

## 10. Đồng bộ quyền và khôi phục

1. Serialize thay đổi theo phạm vi bị ảnh hưởng; tạo operationId/version bền vững, đặt marker chặn ở Redis trước khi ghi thay đổi.
2. Transaction DMS DB ghi quyền mới, tăng version và ghi outbox. Lỗi trước commit chỉ gỡ marker sau khi chắc chắn không có thay đổi đã commit; kết quả không rõ tiếp tục chặn.
3. Worker idempotent publish phiên bản tương ứng vào Redis, bỏ qua bản cũ; thay snapshot và gỡ đúng marker trong thao tác nguyên tử. Không gỡ marker của thao tác mới hơn.
4. API chỉ trả thành công khi nhận xác nhận Redis; đang chờ trả trạng thái pending kèm operationId, không báo thành công sớm. Client retry cùng idempotency key không tạo thay đổi lặp.

Role thay đổi ảnh hưởng nhiều user: chặn scope tenant hoặc toàn hệ thống nếu chưa liệt kê đủ ảnh hưởng; chỉ mở sau khi toàn bộ snapshot liên quan nhất quán. File share/lifecycle đổi cũng dùng protocol này. Di chuyển người dùng giữa phòng ban làm đổi quyền và scope, không được chỉ sửa hồ sơ DB.

Redis mất kết nối: request cần quyền/phiên trả 503, không tiếp tục nghiệp vụ. Redis khởi động lại, khôi phục backup hoặc failover: readiness mặc định đóng; không mở dựa chỉ vào key tồn tại. Worker chủ động kiểm tra generation/version, hòa giải operation/outbox và rebuild từ DB có giới hạn tốc độ. Chỉ mở khi dữ liệu đầy đủ; request người dùng không kích hoạt rebuild. Lỗi DB trong rebuild tiếp tục đóng.

Auth DB có outbox tương tự cho trạng thái phiên. Phiên đã thu hồi không được sống lại khi Redis khôi phục dữ liệu cũ. Operation chặn chưa hoàn tất được hòa giải chủ động, không hết hạn marker rồi tự mở bằng quyền cũ.

Sau cập nhật báo thành công, request mới phải thấy quyền mới hoặc bị chặn. Request đã được cho phép trước đó có thể đang chạy; với thao tác ghi nhạy cảm, kiểm tra lại version trước tác dụng phụ. Không tuyên bố thu hồi được nội dung đã tải.

## 11. Quota và recycle bin

TenantAdmin cấp quota phòng ban; DepartmentAdmin cấp quota từng người trong phòng ban mình. Tổng quota người dùng <= quota phòng ban. Không giảm quota dưới dung lượng đang dùng, bao gồm bin, hoặc dưới phần đã cấp. Đề xuất tổng quota phòng ban <= quota tenant.

Quota số học và giữ chỗ dung lượng là ràng buộc nghiệp vụ transactional tại DMS DB, khác với lookup quyền trong Redis. Serialize cấp quota/upload để request đồng thời không vượt giới hạn; quyền ai được cấp quota vẫn chỉ kiểm tra Redis.

File cần DepartmentId sở hữu và chủ thể chịu quota độc lập với phòng ban hiện tại của owner. Khi người dùng chuyển phòng ban, file không tự đổi bin/quota theo user; chuyển sở hữu/dung lượng cần use case riêng, ngoài phạm vi bản đầu.

Xóa mềm bởi owner hoặc share.CanDelete: chuyển file vào bin phòng ban sở hữu, giữ dung lượng đã dùng, vô hiệu quyền nội dung/share bằng lifecycle Redis. Người xóa khác phòng ban không thay đổi nơi tính quota.

TenantAdmin purge chỉ trên bin cùng tenant: đánh dấu trạng thái purging, chặn truy cập, xóa vật lý idempotent qua storage provider. Chỉ giải phóng quota một lần sau khi xác nhận xóa thành công; lỗi giữ dung lượng và cho phép retry có kiểm soát. Audit ghi người thao tác, owner, file, phòng ban và kết quả; không ghi nội dung/token.

Đề xuất khôi phục: chỉ owner khôi phục file còn trong bin; share cũ không tự có hiệu lực lại, owner phải chia sẻ lại. TenantAdmin không khôi phục bằng quyền purge. Quy tắc này cần review vì chưa được thảo luận riêng.

## 12. Hợp đồng API dự kiến và lỗi

| API | Chủ sở hữu | Xác thực |
|---|---|---|
| POST /api/auth/login | Auth | Credential + Redis; route không yêu cầu JWT. |
| GET /api/auth/google/start | Auth | Khởi tạo OIDC, giới hạn lưu lượng. |
| GET /api/auth/google/callback | Auth | state/code/nonce; không yêu cầu JWT DMS. |
| POST /api/auth/refresh | Auth | Refresh cookie + CSRF + Redis. |
| POST /api/auth/logout | Auth | Refresh/session + CSRF; thu hồi idempotent. |
| Các API nhân sự/quota/file/share/bin/log | DMS | JWT qua Kong + quyền Redis. |
| API provision/đồng bộ nội bộ | Auth/DMS | Danh tính service riêng, không public. |

Migrate lời mời hiện có sang Auth cho credential và DMS cho hồ sơ, giữ quyền sở hữu rõ ràng. Loại bỏ endpoint login cũ khi chuyển đổi, không giữ đường phát JWT HS256 cũ để bypass.

401: token/phiên không hợp lệ; 403: snapshot đầy đủ nhưng policy từ chối; 429: khóa mật khẩu/giới hạn request với Retry-After phù hợp; 503: Redis, snapshot hoặc đồng bộ chưa sẵn sàng. Resource không có quyền có thể trả 404 nhất quán để tránh lộ sự tồn tại. Không trả ngoại lệ nội bộ hay thông tin phân biệt email tồn tại.

## 13. Kiểm chứng cần thực hiện khi triển khai

Thư mục tests hiện chưa có bộ kiểm thử; các mục dưới là tiêu chí dự kiến, không phải kiểm thử đã chạy.

- Kong: token giả/hết hạn/sai thuật toán/audience/issuer; thiếu claim; nhiều nguồn token; header giả; bypass upstream; đổi khóa.
- Mật khẩu: lần sai thứ 5, cửa sổ trượt, 30 phút khóa, request đồng thời, thành công xóa đếm, Google không xóa khóa; Redis lỗi không phát token.
- Google: state/nonce replay, PKCE, token sai, email không tồn tại/inactive, Gmail/Workspace/email bên thứ ba, tranh chấp unique liên kết.
- Phiên: refresh rotation và replay, nhiều tab, logout trong lúc refresh, hạn tuyệt đối 7 ngày, lỗi giữa Auth DB/Redis, khôi phục không hồi sinh phiên bị thu hồi.
- Quyền: toàn bộ ma trận vai trò/action; DepartmentAdmin chưa được share; quyền share độc lập/hết hạn; chia sẻ khác phòng ban cùng tenant; chéo tenant bị chặn.
- Đồng bộ: thu hồi sau báo thành công, worker trễ/crash, event đảo thứ tự, nhiều thao tác đồng thời, Redis thiếu key/restart/mất dữ liệu; không có query DB quyền trong request fallback.
- Quota/bin: quota cấp đồng thời, bin vẫn chiếm dung lượng, người xóa khác phòng ban, purge lỗi/retry không trừ hai lần, đổi phòng ban không tự chuyển quota file.

## 14. Điểm đề xuất cần review cùng bản thiết kế

Các phần đã thống nhất ở mục 2; các chi tiết sau là đề xuất mới để hoàn chỉnh thiết kế:

- Kênh Kong → DMS bằng mTLS và đọc chính JWT được Kong kiểm tra; không thêm plugin custom xuất UserId.
- Cửa sổ đếm sai dùng sliding window; Redis primary duy nhất ở bản đầu, chưa triển khai Cluster.
- Quy tắc role kết hợp, chỉ owner được chia sẻ lại, tổng quota phòng ban không vượt tenant, owner khôi phục nhưng phải chia sẻ lại.
- API pending/idempotency và quy trình khôi phục đóng readiness.

Review đặc tả trước khi lập kế hoạch triển khai; chưa thay mã nguồn hoặc cấu hình chạy.

## 15. Nguồn tham khảo

- [Kong JWT plugin](https://developer.konghq.com/plugins/jwt/): khả năng xác minh JWT, credential và upstream headers; cấu hình cụ thể cần đối chiếu phiên bản chọn triển khai.
- [Google: xác minh ID token](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token): sub, email_verified và trường hợp Google không xác nhận quyền sở hữu email bên thứ ba hiện tại.
- [OWASP Authorization](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html): kiểm tra quyền trên server, mặc định từ chối và quan hệ tài nguyên.
