# Task 4 — S3 và quyền đọc

## Đã triển khai

- Thêm provider `S3=3`, cấu hình JSON được mã hóa bằng cơ chế provider hiện có. Trường bắt buộc: `Endpoint`, `Bucket`, `Region`, `AccessKey`, `SecretKey`, `CapacityBytes`. Endpoint HTTP chỉ chấp nhận loopback; dung lượng là hạn mức cấu hình, bộ đếm sử dụng lấy từ DB. Không đưa khóa vào mã hoặc tài liệu.
- Adapter dùng `AWSSDK.S3 4.0.104.3` cho PUT, GET, HEAD, DELETE và URL ký GET. Object key mới có GUID bất biến. Đường đọc ký `Content-Disposition`, MIME và `Cache-Control: no-store`; tên tệp được lọc ký tự điều khiển/quote và mã hóa UTF-8. URL hỗ trợ Range do GET S3. Nguồn SDK: [AWS .NET S3](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/csharp_s3_code_examples.html), [API presigned URL v4](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/MS3GetPreSignedURLAsyncGetPreSignedUrlRequest.html).
- `GET /api/files/{id}/read-access?purpose=download|inline|stream` dùng tenant/session hiện hành, ACL và `FileContentVersion` Clean đáng tin; từ chối provider tắt/không hỗ trợ và MIME không an toàn. Inline chỉ cấp qua origin storage khác API. TTL mặc định 120 giây, cấu hình `ReadAccess:UrlTtlSeconds` trong khoảng 1–600. Kiểm lại ACL và phiên bản ngay trước kết quả; audit chỉ lưu ID, purpose và version.
- Endpoint download cũ vẫn trả stream byte nhưng dùng cùng ACL/scan/version, bỏ ngoại lệ SystemAdmin xuyên tenant.

## Kiểm tra đã chạy

- `dotnet build src/CleanArchCqrs.API/CleanArchCqrs.API.csproj --no-restore -v:q`: thành công, 0 lỗi, 1 warning NuGet reference trùng; bản build đầy đủ trước đó còn 2 warning GoogleCredential cũ.
- `git diff --check`: không phát hiện lỗi whitespace.

## Còn chờ kiểm thử theo chỉ đạo hiện hành

- Chưa viết/chạy unit hoặc integration test. Cần kiểm denied, tenant khác, scan Pending/Failed, purpose/MIME sai, provider tắt, ACL/version đổi khi ký, URL hết hạn, Content-Disposition/MIME, Range 206, download byte cũ.
- Integration phải dùng MinIO local `cloud-dms-minio-tests:2025-10-15`, container riêng bind loopback, credentials ngẫu nhiên và tự dọn container của fixture.
- Upload hiện hành chưa ghi `FileContentVersion` đã xác minh; tệp thiếu bản ghi này cố ý bị từ chối đọc. Task 5 sẽ sửa luồng upload/scan và tạo version tin cậy. Không dùng chuỗi `FileEntry.ScanStatus=Clean` làm bằng chứng.
