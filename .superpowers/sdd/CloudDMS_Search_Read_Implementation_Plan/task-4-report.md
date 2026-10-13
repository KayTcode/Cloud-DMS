# Task 4 — S3 và quyền đọc

## Đã triển khai

- Thêm provider `S3=3`, cấu hình JSON được mã hóa bằng cơ chế provider hiện có. Trường bắt buộc: `Endpoint`, `Bucket`, `Region`, `AccessKey`, `SecretKey`, `CapacityBytes`. Endpoint HTTP chỉ chấp nhận loopback; dung lượng là hạn mức cấu hình, bộ đếm sử dụng lấy từ DB. Không đưa khóa vào mã hoặc tài liệu.
- Adapter dùng `AWSSDK.S3 4.0.104.3` cho PUT, GET, HEAD, DELETE và URL ký GET. Object key mới có GUID bất biến. Đường đọc ký `Content-Disposition`, MIME và `Cache-Control: no-store`; tên tệp được lọc ký tự điều khiển/quote và mã hóa UTF-8. GET S3 có capability Range; phản hồi 206 chưa được kiểm thử. S3 client được giải phóng khi thao tác thất bại, sau upload, hoặc khi stream download được đóng. `ExpiresAt` dùng đúng thời điểm đưa vào SDK. Nguồn SDK: [AWS .NET S3](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/csharp_s3_code_examples.html), [API presigned URL v4](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/MS3GetPreSignedURLAsyncGetPreSignedUrlRequest.html).
- `GET /api/files/{id}/read-access?purpose=download|inline|stream` dùng tenant/session hiện hành, ACL và `FileContentVersion` Clean đáng tin; từ chối provider tắt/không hỗ trợ và MIME không an toàn. Inline so origin storage với `ReadAccess:PublicApiOrigin` do máy chủ cấu hình; thiếu cấu hình thì từ chối inline. Task 11 cần đặt public API origin theo triển khai thực tế. TTL mặc định 120 giây, cấu hình `ReadAccess:UrlTtlSeconds` trong khoảng 1–600. Kiểm lại ACL và phiên bản ngay trước kết quả; audit chỉ lưu ID, purpose và version.
- Endpoint download cũ vẫn trả stream byte nhưng dùng cùng ACL/scan/version, bỏ ngoại lệ SystemAdmin xuyên tenant.

## Kiểm tra đã chạy

- `dotnet build src/CleanArchCqrs.API/CleanArchCqrs.API.csproj --no-restore -v:q`: thành công sau vòng sửa review, 0 lỗi, 3 warning đã có (NuGet reference trùng và 2 GoogleCredential cũ).
- `git diff --check`: không phát hiện lỗi whitespace.

## Còn chờ kiểm thử theo chỉ đạo hiện hành

- Chưa viết/chạy unit hoặc integration test. Cần kiểm denied, tenant khác, scan Pending/Failed, purpose/MIME sai, provider tắt, ACL/version đổi khi ký, origin cấu hình thiếu/sai/cùng origin, giải phóng SDK client khi GET lỗi, URL hết hạn, Content-Disposition/MIME, Range 206, download byte cũ.
- Integration phải dùng MinIO local `cloud-dms-minio-tests:2025-10-15`, container riêng bind loopback, credentials ngẫu nhiên và tự dọn container của fixture.
- Upload hiện hành chưa ghi `FileContentVersion` đã xác minh; tệp thiếu bản ghi này cố ý bị từ chối đọc. Task 5 sẽ sửa luồng upload/scan và tạo version tin cậy. Không dùng chuỗi `FileEntry.ScanStatus=Clean` làm bằng chứng.
