# Embedding CPU nội bộ

Đã triển khai E5 `intfloat/multilingual-e5-small@614241f622f53c4eeff9890bdc4f31cfecc418b3`, 384 chiều, mean pooling theo attention mask và L2 normalize. Model/tokenizer nạp từ asset cục bộ, kiểm SHA256 theo manifest; chỉ safetensors, không remote code/download lúc chạy.

Chuẩn bị trước build (Python 3.11):

```powershell
python services/embedding/preload.py --cache .superpowers/sdd/CloudDMS_Search_Read_Implementation_Plan/model-cache/e5-small
```

Bỏ `--cache` để tải đúng revision trong giai đoạn setup có mạng. Thư mục `model/` bị gitignore; API Docker context cũng loại model/ledger. Docker build cần mạng để cài requirements đã khóa phiên bản; runtime chỉ ở network `search-internal` không có egress/public port. Dependencies và Docker image chưa build/chạy theo yêu cầu hoãn runtime đến sau Task 11.

- `POST /embed`: `{modelVersion, kind:"query"|"passage", texts:[...]}` → `{modelVersion,dimensions:384,vectors:[[...]]}`. Service tự thêm `query: ` hoặc `passage: `; không thêm prefix từ caller. Batch tối đa 16, query tối đa 1.000 ký tự, passage tối đa 32.768, mỗi đầu vào sau prefix/special token tối đa 512. Quá giới hạn trả 422, không cắt ngầm.
- `POST /chunk`: hợp đồng Task 7, target 300 token gồm prefix/special, overlap 50 token, tối đa 2.000 chunk. Dùng offset tokenizer thật trong từng cửa sổ tối đa 8.192 ký tự; trả text nguồn và location section. Giới hạn chunk trả partial/`CHUNK_LIMIT`.
- `GET /health`: identity model và dimension sau khi model đã nạp.

Body tối đa 112 MiB, tổng source 16M ký tự/1.000 section. Server xử lý một request tại một thời điểm; deadline 110 giây, watchdog kết thúc process ở 115 giây để compose khởi động lại nếu inference mắc kẹt. Container CPU 2/memory 2 GiB là cấu hình ban đầu, chưa benchmark. Không log body, exception details hoặc secret.

.NET: `Embedding:Endpoint` mặc định `http://127.0.0.1:8001/`, `BatchSize=16`, `TimeoutSeconds=120`; `SearchIndex:ModelVersion` và `Embedding:ModelVersion` phải bằng identity trên. `Qdrant:Endpoint=http://127.0.0.1:6333/`, `TimeoutSeconds=120`, tùy chọn secret `ApiKey`. Compose thay endpoint bằng DNS dịch vụ; secret triển khai do môi trường cấp.

Collection `clouddms_e5_` + SHA256 UTF8 model identity (hex thường), cosine/384. Payload chỉ tenant/file/documentVersion/generation/chunkOrdinal/modelVersion. Upsert/delete chờ completed; cleanup chỉ xóa point có exact tenant/file và generation khi được yêu cầu, qua mọi collection thuộc namespace, không drop collection. Candidate phải được Task 9 kiểm lại ACL, trạng thái, current document/generation/model trong SQL trước trả người dùng. `FolderId` được Task 9/SQL áp dụng, không có trong Qdrant payload.

Nguồn chính thức: [E5 model card](https://huggingface.co/intfloat/multilingual-e5-small), [Qdrant points](https://qdrant.tech/documentation/manage-data/points/), [Qdrant filtering](https://qdrant.tech/documentation/search/filtering/). Model card hướng dẫn prefix, masked mean pooling, normalization; các giới hạn dịch vụ là lựa chọn triển khai ban đầu.
