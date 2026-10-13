# Task 6 — Keyword Search

## Đã triển khai

- `IKeywordSearchService.SearchAsync(context, request, offset, ct)` trả `SearchBranchResult`. Candidate dùng `Source = "keyword"`; metadata có `GenerationId`, `ChunkOrdinal`, `ModelVersion` là `null`; nội dung có generation và ordinal, `ModelVersion = null`.
- Tái dùng `IFileAclQuery.ReadScopeAsync` cho tenant, ACL ứng viên, vòng đời và phiên bản scan sạch trước `Take`. Lọc `FolderId` trên cùng scope. Task 9 vẫn phải dùng `CheckManyAsync` và `AssertStillValidAsync` trước khi trả kết quả.
- Tên file tìm bằng `MATCH/AGAINST` tham số hóa và `LIKE` escape `\\`, `%`, `_`; nội dung chỉ lấy từ generation active có đúng document version và trạng thái Ready/Partial. Token ngắn dưới ba ký tự dùng `LIKE` có escape trên nội dung. Hòa hai nhánh theo FileId, điểm giảm dần rồi FileId tăng dần.
- Migration `20261013130000_AddKeywordFullText` chỉ thêm FULLTEXT cho tên và chunk text, cùng index tenant/lifecycle; không sửa bảng lịch sử. Đã đăng ký DI.

## Kiểm tra và giới hạn

- `dotnet build src/CleanArchCqrs.API/CleanArchCqrs.API.csproj --no-restore -v q`: thành công, 0 lỗi; còn cảnh báo package trùng và API GoogleCredential cũ.
- Chưa chạy migration trên DB người dùng. Chưa chạy test, integration hay EXPLAIN theo thứ tự code trước, test sau.
- Giai đoạn test cần fixture MySQL thật cho tiếng Việt ở tên/nội dung, token ngắn, ký tự `%`/`_` literal, đầu vào SQL injection, tenant/ACL/scan trước LIMIT, generation lỗi/cũ, file không hỗ trợ, cùng EXPLAIN trên fixture đủ lớn. Cần kiểm tra SQL EF sinh ra, FULLTEXT index và hiệu năng `LIKE` token ngắn.
- Candidate giới hạn tổng offset + limit tối đa 1.000. `HasMore` chỉ phản ánh cửa sổ candidate đã truy vấn trong budget này. Bộ điều phối phải xác nhận ACL lại trước response và xử lý quyền bị thu hồi giữa truy vấn.
