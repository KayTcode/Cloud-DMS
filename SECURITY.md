# Bảo mật Cloud DMS

## Đã triển khai trong mã

- BCrypt, JWT có session identity; Redis bảo mật readiness fail closed, snapshot quyền/version/stamp. API fallback yêu cầu authenticated user; các mutation đã chuyển dùng coordinator/outbox/pending. Không khẳng định mọi endpoint quản trị đã chuyển sang cơ chế mới.
- ACL đọc dùng chung cho search/read/download/preview/text; tenant từ phiên xác thực. SQL current version/generation/scan là nguồn chuẩn; Qdrant candidate không có quyền tự cấp đọc.
- ClamAV tắt/lỗi/coverage chưa xác nhận không coi Clean. Upload tới50 GiB được lưu chờ xác minh; scan mặc định25 MiB, coverage<2 GiB và opt-in xác nhận deployment. File ngoài coverage không read/clean index. Không có cấu hình giả quét50 GiB.
- Presigned read TTL120 giây (1–600), private bucket, inline MIME allowlist và origin nội dung khác API. PublicApiOrigin cấu hình tin cậy, không từ Request.Host. URL đã cấp không thu hồi tức thì; request đang tải có thể tiếp tục theo provider.
- CORS danh sách rõ, mặc định rỗng; production bắt buộc AllowedHosts cụ thể/HTTPS public origin. ForwardedFor/Proto chỉ nhận từ IP proxy tin cậy một hop, không tin forwarded Host. API thêm nosniff; storage proxy phải cấu hình tương tự.
- Data Protection ngoài Development bắt buộc namespace deployment/key ring chung bền vững/PFX private key mã hóa at rest. Thiếu cấu hình/permission/cert hợp lệ phải dừng. Cấp secret DB/JWT/AES/provider/certificate từ secret store; không ghi secrets vào source/log.
- Logging request chỉ tên/loại lỗi, không payload; hosting request-start query log bị chặn. Global error response thông báo chung/no-store, không exception detail. Proxy/CDN/WAF/APM cần scrub q/cursor/token/signed URL và body riêng, không chỉ dựa API.
- PDF native parser chỉ qua bwrap user/network/PID namespace riêng, không fallback trực tiếp; namespace thiếu/lỗi giữ metadata-only. Converter Office tắt mặc định; child riêng namespaces/seccomp/prlimit/non-root/no network, không mount credentials/Docker socket. Không dùng privileged để vượt lỗi sandbox.
- Startup không tự migration/seed mặc định, không nuốt lỗi hoặc ad-hoc DDL. Opt-in initializer có seeder mẫu nên phải rà soát trước production. Rate limiter search30/auth10/upload10 mỗi phút theo process, không bảo đảm global giữa replica.

## Giới hạn và công việc dự kiến

Mã Task11 mới kiểm biên dịch/tĩnh, chưa chứng minh runtime, DB nâng cấp, sandbox/isolation, provider private policy/Range/CORS, regression API hay benchmark. Các luồng quản trị chưa chuyển hết; không khẳng định tenant isolation hoàn chỉnh ngoài phạm vi read/search đã viết. AES cấu hình provider còn giữ format/tương thích cũ; thay khóa cần quy trình tái mã hóa, không đổi mù trên DB hiện hữu. Development key ring tạm thời không dùng cho production. Certificate rotation/secret backup phải được vận hành kiểm soát; không xóa key ring khi rollback.

Qdrant pin digest; các base image/package khác chưa chốt digest toàn chuỗi. Network backend private không thay thế auth/service segmentation và sandbox child. Host Docker/AppArmor/seccomp có thể chặn user namespaces: phải fail closed, chưa được coi ready. PDF dùng network namespace và filter runtime kế thừa, không có BPF socket-deny riêng như converter; cgroup memory512 MiB riêng PDF chưa triển khai (watchdog child và cgroup toàn API2 GiB).

Quy trình cấu hình, giới hạn coverage, migration thủ công, proxy/log redaction, retry/reconcile, rollback và test còn hoãn: [runbook search/read](docs/search-read-runbook.md). Không dùng compose root cũ cho production; dùng mẫu production riêng và hoàn thành các kiểm chứng trước khi mở lưu lượng.
