# Chạy benchmark search/read

Yêu cầu .NET 8, Docker Desktop Linux đang chạy; model thật trong `services/embedding/model/` khớp manifest. Không commit model. Chuẩn bị image riêng:

```powershell
docker build -t cloud-dms-embedding-task12 services/embedding
docker build -t cloud-dms-preview-task12 services/preview
docker build -t cloud-dms-minio-tests:2025-10-15 -f deploy/minio/Dockerfile .
pwsh -File tools/search-read-benchmark/run.ps1
```

Nếu không có `pwsh`, dùng Windows PowerShell. Fixture tự tạo MySQL8.4/Redis7.4/MinIO/Qdrant/E5 riêng, cổng loopback ngẫu nhiên, secrets tạm và dọn container do nó tạo. Không nhận connection string DB người dùng. Qdrant dùng digest pin trong fixture. Docker cần tải image nếu chưa cache.

Corpus tổng hợp tại `tests/fixtures/search-read/corpus.json`: 10 tài liệu, 50 query Việt/Anh/đa ngôn ngữ, tên ngắn, wildcard, metadata-only, nội dung thay thế; nhãn relevance nhị phân chưa thẩm định độc lập. Kiểm tra ACL/thu hồi/cursor/version chạy thêm ngoài tập tính metric.

Đo cả ba mode qua handler thật gồm SQL/ACL/Redis, E5/Qdrant thật; mỗi query mới, page10, concurrency1. Recall@5 là số relevant trong top5 chia tổng relevant; nDCG@10 dùng gain nhị phân và discount `1/log2(rank+1)`; p95 nearest-rank. Có duration upload/download/extract/chunk/embed/stage; scan và publication generation được seed tin cậy cho fixture. Không đo worker indexing end-to-end, HTTP p95 hoặc ClamAV thật. Mẫu `docker stats` chỉ là quan sát rời rạc, không chứng minh peak/cgroup isolation hay CPU-seconds.

Đây là smoke benchmark corpus nhỏ, không thay workload production, kiểm thử tải, đánh giá chất lượng độc lập hoặc nghiệm thu mọi AC. Kết quả/giới hạn lần chạy: `docs/search-read-validation.md`.
