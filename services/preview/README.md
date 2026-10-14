# Converter preview nội bộ

Đã triển khai transport HTTP Python và child LibreOffice Linux riêng; chưa kiểm chứng runtime. API không chạy LibreOffice. Chỉ nhận DOCX/XLSX/PPTX đã Clean, SHA-256 và converter version phải trùng; không nhận URL hay thông tin kho lưu trữ. `POST /convert` nhận body nhị phân, Content-Type OOXML, Content-Length, `X-Content-SHA256`, `X-Converter-Version`; trả PDF tối đa 32 MiB và version. Lỗi trả mã chung, không ghi nội dung/hash/tên vào log.

## Cô lập đã viết trong mã

- Child qua `prlimit` rồi `bubblewrap --unshare-all` (user/PID/network/IPC/UTS/cgroup), drop toàn bộ capability, root chỉ có `/usr`, thư viện và fonts read-only; `/work` là thư mục job riêng trên tmpfs 256 MiB. `/tmp` trỏ vào cùng mount; `/dev` read-only. Không mount socket Docker, credential, DB, source code API hay home của transport.
- BPF từ libseccomp được bwrap cài vào child; từ chối socket/socketpair/connect/bind/listen/accept/send/recv, socketcall và io_uring, cùng ptrace/mount/unshare/setns/bpf/keyctl/perf/userfaultfd. Network namespace riêng và socket denial áp dụng cho mọi descendant; mạng HTTP của parent không phải bằng chứng cô lập child.
- Không có nhánh chạy không sandbox. Thiếu namespace/seccomp/binary, child lỗi, macro, embedded object, external relationship, timeout hoặc PDF thiếu/sai đều không publish ready. Profile riêng tắt macro/update link; preflight loại external relationship và VBA/ActiveX/embedded object trước converter.
- Một conversion, tối đa 8 transport threads; input 100 MiB, archive tổng 256 MiB/10.000 entries, relationship XML 1 MiB; output 32 MiB. Deadline preflight+child 60 giây, CPU child 55 giây, address-space child 512 MiB; compose cgroup toàn service 512 MiB, không swap, 1 CPU/64 PID, read-only/non-root/no-new-privileges. Luôn kill process group; PID namespace và die-with-parent chặn descendant tồn tại sau launcher. Crash leftovers trên mount job được dọn lúc khởi động.

## Điều kiện triển khai Task 11

Image Debian bookworm cài `python3`, `util-linux` (`prlimit`), `bubblewrap`, `libseccomp2`, `libreoffice-writer`, `libreoffice-calc`, `libreoffice-impress`, fonts DejaVu/Liberation. Chạy Linux x86_64/aarch64, UID/GID 10001. Kernel/container runtime phải cho phép user namespace không đặc quyền, network/PID/mount namespace và cài seccomp BPF. Chính sách Docker/AppArmor/seccomp mặc định của một số host chặn namespace: service sẽ trả 503, không được đổi sang privileged, mount Docker socket hoặc bỏ sandbox. Cần cấu hình host/runtime giới hạn đúng syscall namespace cho bwrap và kiểm chứng sandbox thực tế ở giai đoạn test.

`Preview:Enabled=false` mặc định; chỉ bật sau kiểm chứng deployment. `Preview:Endpoint=http://preview:8002/`, `Preview:ConverterVersion=libreoffice-sandbox-v1`; giá trị version phải trùng `CONVERTER_VERSION` của service và phải đổi khi image/profile thay đổi. Compose không publish cổng converter. Network internal chỉ giới hạn transport; sandbox child là lớp bắt buộc riêng. Build image cần Internet để cài package; xử lý tài liệu không cần Internet. Pin image/package digest theo quy trình deployment trước production.

Dự kiến sau Task 11: fixture DOCX/XLSX/PPTX, macro/XXE/ZIP bomb/external link, socket tới DB/Redis/Internet/loopback/AF_UNIX bị chặn, namespace không khả dụng, memory/tmpfs/output cap, timeout/process descendants/crash cleanup, tải đồng thời và retry. Chưa chạy build image, converter hay bài test nào theo yêu cầu code trước.
