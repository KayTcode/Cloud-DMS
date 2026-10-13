# Spec: Search File và Read File cho Cloud Storage

**Phiên bản:** 1.0\
**Ngày:** 13/10/2026\
**Phạm vi:** Release 1 --- Keyword Search, Semantic Search, Download,
Preview và Streaming

## 1. Mục tiêu

Release 1 cho phép người dùng tìm file bằng từ khóa và tìm theo ngữ
nghĩa trên nội dung văn bản được hỗ trợ, sau đó đọc file thông qua
download, preview hoặc streaming.

Các quyết định chưa được chốt được giữ tại mục [Câu hỏi
mở](#12-câu-hỏi-mở).

## 2. Phạm vi và nguyên tắc

Cloud Storage cho phép người dùng tải lên nhiều loại file, tương tự
Google Drive. Mỗi file có thể có dung lượng hàng chục GB trở lên và được
lưu trên Object Storage.

### 2.1. Phạm vi theo giai đoạn

-   **GD1 --- Keyword/Full-text Search:** tìm theo tên file, metadata và
    nội dung văn bản.
-   **GD2 --- Semantic Search:** tìm theo ngữ nghĩa bằng model embedding
    có sẵn.
-   **GD3 --- Ngoài Release 1:** transcript audio/video, OCR và mô tả
    ảnh.
-   **GD4 --- Định hướng dài hạn:** fine-tune hoặc phát triển model
    riêng khi có dữ liệu và cơ sở để chứng minh hiệu quả. Không bắt buộc
    triển khai huấn luyện trong Release 1.

### 2.2. Nguyên tắc hệ thống

1.  MySQL là nguồn dữ liệu chuẩn cho trạng thái file, metadata và quyền
    truy cập. Search Index chỉ là bản sao phục vụ tìm kiếm.
2.  Phân tích AI chạy bất đồng bộ ở nền, không chặn việc upload.
3.  File AI không phân tích được vẫn được lưu nếu đáp ứng chính sách bảo
    mật và giới hạn hệ thống.
4.  Thay đổi quyền phải có hiệu lực tại tầng truy vấn ngay khi thay đổi
    nghiệp vụ được chấp nhận; không phụ thuộc vào tốc độ đồng bộ Search
    Index.
5.  Nội dung tài liệu không được gửi đến API AI bên thứ ba trong cấu
    hình mặc định của Release 1.

## 3. Các quyết định đã chốt

  -----------------------------------------------------------------------
  Chủ đề                              Quyết định
  ----------------------------------- -----------------------------------
  Loại file được upload               Nhiều loại file: tài liệu, bảng
                                      tính, trình chiếu, ảnh, audio,
                                      video, ZIP và các định dạng khác
                                      được chính sách hệ thống cho phép

  Dung lượng file                     Hàng chục GB trở lên; giới hạn
                                      chính xác cần chốt

  Lưu trữ file                        Cloud Object Storage

  Keyword Search                      MySQL Full-Text Search

  Semantic Search                     Qdrant +
                                      `intfloat/multilingual-e5-small`,
                                      self-host trên CPU

  Xử lý nền                           .NET Background Worker +
                                      Transactional Outbox

  Ngôn ngữ nội dung                   Hướng tới hỗ trợ đa ngôn ngữ; khả
                                      năng nhận diện và chất lượng cần
                                      kiểm thử

  Phân tích sau upload                Bất đồng bộ ở nền

  Phạm vi kết quả                     File của người dùng, file được chia
                                      sẻ và file thuộc team/tổ chức mà
                                      người dùng có quyền truy cập

  Kết quả Search                      Danh sách file, điểm liên quan và
                                      đoạn trích nổi bật nếu có

  MP3, MP4 và ảnh trong Release 1     Chỉ tìm theo tên và metadata; không
                                      transcript, OCR hoặc image
                                      captioning

  Streaming audio/video               HTTP Range Request trực tiếp từ
                                      Object Storage qua URL có chữ ký;
                                      chưa chuyển mã HLS

  Download và streaming file lớn      Presigned URL từ Object Storage;
                                      backend xác thực và cấp URL sau khi
                                      kiểm quyền

  Preview DOCX/XLSX/PPTX riêng tư     Chuyển đổi PDF phía server, phục vụ
                                      bản preview qua cơ chế URL có kiểm
                                      soát

  Preview DOCX/XLSX/PPTX chia sẻ công Có thể dùng viewer bên thứ ba thông
  khai                                qua `IPreviewProvider`, chỉ khi
                                      chính sách chia sẻ và bảo mật cho
                                      phép

  Mô hình quyền                       Viewer / Editor / Owner; có thể kế
                                      thừa từ thư mục cha và chia sẻ theo
                                      nhóm/team
  -----------------------------------------------------------------------

**Lưu ý:** Presigned URL là thông tin cấp quyền. URL cần thời hạn phù
hợp, phạm vi object cụ thể và không được ghi nguyên vẹn vào log. Quyền
truy cập của URL đã cấp có thể còn hiệu lực đến khi hết hạn, trừ khi có
cơ chế thu hồi phù hợp của Object Storage.

## 4. Kiến trúc và luồng Upload/Indexing

### 4.1. Luồng xử lý

1.  Client upload file trực tiếp lên Object Storage theo cơ chế upload
    được cấp phép.
2.  Backend xác nhận trạng thái upload, ghi metadata và sự kiện Outbox
    trong cùng transaction MySQL.
3.  Background Worker nhận sự kiện và kiểm tra bảo mật: loại file thực
    tế, giới hạn dung lượng và trạng thái quét mã độc theo chính sách.
4.  Nếu định dạng được hỗ trợ, Worker trích xuất văn bản.
5.  Văn bản được chia thành các chunk.
6.  Embedding service nội bộ tạo vector bằng model
    `multilingual-e5-small`.
7.  Hệ thống cập nhật MySQL Full-Text Index và Qdrant.
8.  Chỉ công bố generation/index mới là hiện hành khi các thành phần bắt
    buộc đã sẵn sàng.

Nếu định dạng không được hỗ trợ, hệ thống lưu metadata và đánh dấu trạng
thái như `UnsupportedFormat` hoặc `Skipped`, kèm lý do.

### 4.2. Công nghệ dự kiến

-   **Backend:** .NET 8 Web API, Clean Architecture và CQRS.
-   **Metadata/ACL:** MySQL + EF Core.
-   **Keyword Search:** MySQL Full-Text Search.
-   **Vector Search:** Qdrant self-hosted.
-   **Embedding:** `intfloat/multilingual-e5-small`, chạy nội bộ trên
    CPU.
-   **Xử lý nền:** .NET Background Worker + Transactional Outbox.
-   **Embedding runtime:** có thể dùng Python + ONNX Runtime CPU nếu
    model và pipeline tương thích; cần benchmark trước khi chốt cách
    đóng gói.
-   **Storage:** Cloud Object Storage qua abstraction để hỗ trợ nhiều
    provider.

### 4.3. File không được phân tích

Các file như ZIP, RAR, EXE hoặc file nhị phân vẫn có thể được upload và
lưu trữ nếu đạt chính sách bảo mật và giới hạn dung lượng.

-   Không đưa định dạng chưa hỗ trợ vào pipeline trích xuất/embedding.
-   Vẫn lưu tên, phần mở rộng, dung lượng, người tải lên, thời gian và
    metadata quản trị.
-   Cho phép tìm theo tên và metadata, không tìm nội dung bên trong.
-   Không đồng nhất trạng thái `UnsupportedFormat` của AI với trạng thái
    upload thất bại.
-   Kiểm tra loại file thực tế và quét mã độc theo chính sách trước khi
    cho phép tải xuống hoặc chia sẻ.
-   Có thể xử lý lại khi hệ thống bổ sung bộ xử lý phù hợp.

## 5. Luồng Search File

Một truy vấn chạy qua Keyword Search và Semantic Search, sau đó chỉ trả
về file mà người dùng có quyền xem.

1.  Người dùng nhập từ khóa hoặc câu mô tả.
2.  API xác định người dùng, tenant và phạm vi quyền: file của chính
    người dùng, file được chia sẻ và file thuộc team/tổ chức, bao gồm
    quyền kế thừa từ thư mục cha.
3.  **GD1:** MySQL Full-Text Search trên tên file, metadata và nội dung
    văn bản đã trích xuất.
4.  **GD2:** tạo embedding cho truy vấn bằng `multilingual-e5-small`;
    Qdrant trả về các chunk gần nhất. Tuân thủ định dạng đầu vào mà
    model yêu cầu, bao gồm tiền tố `query:` và `passage:` nếu áp dụng
    theo model.
5.  Hợp nhất kết quả theo file, loại trùng và xếp hạng.
6.  Kiểm tra quyền lần cuối bằng dữ liệu MySQL/ACL hiện hành. Không dựa
    duy nhất vào ACL trong payload Qdrant.
7.  Trả danh sách file với điểm liên quan và đoạn trích nổi bật nếu có.

Với MP3, MP4, ảnh và file nhị phân trong Release 1, Search chỉ khớp theo
tên và metadata.

## 6. Luồng Read File

Mọi yêu cầu đọc file đều kiểm tra quyền ở backend trước. Với file lớn,
byte của file không đi qua backend trong luồng tải thông thường; client
tải trực tiếp từ Object Storage qua presigned URL.

  -----------------------------------------------------------------------
  Cách đọc                Áp dụng                 Cách hoạt động
  ----------------------- ----------------------- -----------------------
  Download nguyên file    Các loại file được phép Backend kiểm tra quyền
                          tải                     và trạng thái bảo mật,
                                                  sau đó cấp presigned
                                                  URL; client tải trực
                                                  tiếp từ Object Storage

  Preview file riêng tư   DOCX, XLSX, PPTX        Worker/service phía
                                                  server chuyển đổi sang
                                                  PDF; bản preview được
                                                  phục vụ qua URL có kiểm
                                                  soát

  Preview file chia sẻ    DOCX, XLSX, PPTX        Có thể nhúng viewer bên
  công khai                                       thứ ba qua
                                                  `IPreviewProvider`, nếu
                                                  chính sách chia sẻ cho
                                                  phép và không làm lộ dữ
                                                  liệu riêng tư

  Streaming audio/video   MP3, MP4                HTTP Range Request trực
                                                  tiếp qua presigned URL
                                                  nếu provider hỗ trợ;
                                                  chưa chuyển mã HLS

  Trích xuất nội dung     File văn bản đã được    Chỉ cung cấp nội dung
                          index                   trích xuất theo một API
                                                  có quyền và giới hạn rõ
                                                  ràng; schema API còn
                                                  cần chốt
  -----------------------------------------------------------------------

### 6.1. Preview Office

-   Dùng server-side conversion cho file riêng tư; LibreOffice headless
    là ứng viên triển khai cần kiểm thử.
-   Cache bản PDF preview theo `FileId` và version/`ContentHash`.
-   Khi nội dung thay đổi, không được trả preview của phiên bản cũ.
-   Khi file bị xóa hoặc quyền bị thu hồi, phải chặn truy cập bản
    preview và xử lý cache theo chính sách.
-   Converter cần được cô lập, giới hạn tài nguyên và thời gian xử lý.
-   Viewer bên thứ ba chỉ dùng khi loại chia sẻ và chính sách dữ liệu
    cho phép. Không gửi JWT của người dùng vào URL viewer.
-   Bố cục sau chuyển đổi có thể khác Microsoft Office; cần kiểm thử với
    DOCX, XLSX và PPTX thực tế.

### 6.2. File lớn và streaming

-   Backend không proxy byte file trong luồng thông thường.
-   Presigned URL chỉ cấp sau khi kiểm tra quyền và trạng thái bảo mật.
-   URL giới hạn vào object cụ thể, chỉ có quyền cần thiết và thời hạn
    ngắn phù hợp.
-   Không ghi URL đầy đủ vào log.
-   Tải tiếp bằng HTTP Range phụ thuộc khả năng của provider.
-   Upload file lớn nên dùng multipart/resumable upload trực tiếp tới
    Object Storage.
-   CDN + signed URL có thể được bổ sung khi có nhu cầu về hiệu năng
    hoặc phân phối địa lý; không phải yêu cầu Release 1.

## 7. Phân quyền và tenant isolation

Quyết định cuối cùng về việc ai được xem file lấy từ MySQL và các quy
tắc ACL hiện hành, không lấy từ Search Index.

-   Vai trò: Viewer, Editor, Owner, gán ở file hoặc thư mục.
-   Quyền từ thư mục cha có thể được kế thừa xuống file và thư mục con.
-   Hỗ trợ chia sẻ theo nhóm/team.
-   Thu hồi quyền phải có hiệu lực ngay tại tầng truy vấn; file không
    còn xuất hiện trong kết quả search đối với người không có quyền.
-   Cấp quyền mới có thể cần đồng bộ metadata để truy vấn tìm thấy file
    theo quyền mới.
-   Chỉ thay đổi quyền mà nội dung không đổi thì không tạo lại
    embedding.
-   Mọi truy vấn Qdrant phải giới hạn tenant phù hợp; sau khi lấy
    candidate, phải xác minh ACL hiện hành trước khi trả nội dung hoặc
    metadata nhạy cảm.
-   Không tin `TenantId` tùy ý do client gửi; xác định tenant từ danh
    tính và ngữ cảnh đã xác thực.

## 8. Đồng bộ Search Index khi file thay đổi

Dùng Transactional Outbox để ghi sự kiện cùng transaction nghiệp vụ
trong MySQL. Worker cập nhật MySQL Full-Text Index và Qdrant bất đồng
bộ. Search Index có thể trễ đồng bộ, nhưng kết quả phải tuân thủ trạng
thái file và quyền hiện hành.

  -----------------------------------------------------------------------
  Sự kiện                             Xử lý
  ----------------------------------- -----------------------------------
  `DocumentCreated`                   Lập chỉ mục metadata và nội dung
                                      nếu định dạng được hỗ trợ

  `DocumentContentChanged`            Tăng `DocumentVersion`/generation,
                                      trích xuất lại, chia chunk, tạo
                                      embedding và cập nhật index

  `DocumentMetadataChanged`           Cập nhật tên, đường dẫn và
                                      metadata; giữ embedding nếu nội
                                      dung không đổi

  `DocumentPermissionsChanged`        Áp dụng quyền hiện hành ngay ở tầng
                                      truy vấn; đồng bộ metadata quyền
                                      nếu cần

  `DocumentDeleted`                   Loại khỏi Search ngay; xóa chunk,
                                      vector và nội dung index; xóa dữ
                                      liệu vật lý theo chính sách
                                      retention
  -----------------------------------------------------------------------

### 8.1. Tính bền vững và nhất quán

-   Xử lý sự kiện phải idempotent để có thể chạy lại an toàn.
-   Retry có backoff cho lỗi tạm thời; ghi nhận lỗi kéo dài để quản trị
    viên xử lý.
-   Không giả định sự kiện luôn đến đúng một lần hoặc theo đúng thứ tự.
-   Worker phải kiểm tra version/generation hiện hành trước khi công bố
    index. Kết quả của worker cũ không được ghi đè generation mới.
-   Có quy trình đối soát để phát hiện vector mồ côi, file thiếu index
    hoặc index sai phiên bản.
-   Nếu có yêu cầu xóa dữ liệu pháp lý, xử lý cả bản sao, cache và
    backup theo chính sách tương ứng.

## 9. Lộ trình AI

  -------------------------------------------------------------------------
  Giai đoạn               Nội dung                  Trạng thái
  ----------------------- ------------------------- -----------------------
  GD1                     Tìm theo từ khóa trên     Release 1
                          tên, metadata và nội dung 
                          văn bản bằng MySQL        
                          Full-Text Search          

  GD2                     Semantic Search trên nội  Release 1
                          dung văn bản, dùng        
                          `multilingual-e5-small`   
                          self-host trên CPU và     
                          Qdrant                    

  GD3                     Transcript audio/video,   Sau Release 1
                          OCR và mô tả ảnh          

  GD4                     Fine-tuning hoặc phát     Định hướng dài hạn;
                          triển model riêng         chưa cam kết chi phí
                                                    hoặc hạ tầng
  -------------------------------------------------------------------------

GD4 không yêu cầu training from scratch. Chỉ cân nhắc fine-tuning khi có
dữ liệu truy vấn--tài liệu đáng tin cậy, bộ đánh giá độc lập và bằng
chứng cho thấy chất lượng được cải thiện.

## 10. Tiêu chí nghiệm thu đề xuất

Các ngưỡng định lượng cần được xác nhận sau khi có benchmark trên dữ
liệu và máy mục tiêu.

  -----------------------------------------------------------------------
  ID                      Tiêu chí                Điều kiện nghiệm thu
  ----------------------- ----------------------- -----------------------
  AC-01                   Keyword Search          Tìm được theo tên,
                                                  metadata và nội dung
                                                  văn bản đã index

  AC-02                   Semantic Search         Tìm được tài liệu liên
                                                  quan theo ý nghĩa,
                                                  không chỉ trùng từ khóa

  AC-03                   Hybrid Search           Hợp nhất kết quả hai
                                                  nhánh, loại trùng và
                                                  trả kết quả theo thứ tự
                                                  liên quan

  AC-04                   Tenant isolation        Không thể truy xuất tài
                                                  liệu tenant khác qua
                                                  Search API

  AC-05                   ACL                     Người không có quyền
                                                  không nhận được nội
                                                  dung hoặc metadata nhạy
                                                  cảm

  AC-06                   Vòng đời index          Xóa, ghi đè, đổi tên và
                                                  đổi quyền được xử lý
                                                  đúng

  AC-07                   Resilience              Worker retry, xử lý
                                                  trùng an toàn và phục
                                                  hồi sau khởi động lại

  AC-08                   Model independence      Có thể thay model qua
                                                  adapter/cấu hình mà
                                                  không viết lại nghiệp
                                                  vụ Search

  AC-09                   Self-host               Embedding hoạt động nội
                                                  bộ mà không cần gọi API
                                                  AI bên ngoài

  AC-10                   Observability           Có trạng thái xử lý,
                                                  lỗi, thời gian indexing
                                                  và khả năng đối soát

  AC-11                   Download/streaming      File lớn được tải trực
                                                  tiếp từ Object Storage;
                                                  hỗ trợ Range nếu
                                                  provider đáp ứng

  AC-12                   Preview                 Preview tuân thủ ACL và
                                                  không trả bản cache cũ
                                                  sau khi file thay đổi
  -----------------------------------------------------------------------

Mục tiêu benchmark ban đầu có thể dùng tối thiểu 50 truy vấn đại diện,
đo Recall@5 và nDCG@10, cùng p95 latency cho Keyword, Semantic và Hybrid
Search. Ví dụ, mục tiêu Recall@5 ≥ 80% và p95 Hybrid Search \< 2 giây ở
tải thử nghiệm thấp chỉ là ngưỡng đề xuất, phải xác nhận lại bằng
benchmark.

## 11. Câu hỏi mở

Các nội dung sau chưa được chốt và cần quyết định trước khi xem spec là
hoàn chỉnh.

-   [ ] Cách hợp nhất kết quả MySQL Full-Text và Qdrant: điểm chung,
    trọng số, hay cho người dùng chọn chế độ tìm?
-   [ ] Danh sách định dạng văn bản được hỗ trợ trong Release 1: PDF,
    DOCX, XLSX, PPTX, CSV, TXT, Markdown và các định dạng khác?
-   [ ] Với file hàng chục GB, có index toàn bộ nội dung hay đặt giới
    hạn dung lượng, số trang hoặc số chunk cho phân tích AI?
-   [ ] Kích thước chunk, overlap và cách xử lý tài liệu đa ngôn ngữ?
-   [ ] Ma trận quyền cụ thể: Viewer, Editor, Owner được xem, tải, ghi
    đè, xóa và đổi quyền như thế nào?
-   [ ] File chỉ có public link có xuất hiện trong Search của người
    ngoài không? Quy tắc hiện tại là không, trừ khi được quyết định
    khác.
-   [ ] Preview ảnh, PDF và TXT sẽ hiển thị trực tiếp hay qua một dịch
    vụ preview chung?
-   [ ] Cơ chế multipart/resumable upload, giới hạn kích thước file và
    thời hạn presigned URL?
-   [ ] Schema, quyền gọi và giới hạn của API "Trích xuất nội dung"?
-   [ ] Hiển thị gì khi file chưa được index hoặc index lỗi?
-   [ ] Mục tiêu hiệu năng: số file mỗi user/team, độ trễ Search chấp
    nhận được và thời gian từ upload đến khi tìm thấy?
-   [ ] Quota dung lượng mỗi user/team, thời gian giữ file trong thùng
    rác và version lịch sử?
-   [ ] Quy tắc dùng viewer bên thứ ba: điều kiện nào được xem là "chia
    sẻ công khai", và có cho phép gửi URL/tài liệu đến dịch vụ ngoài
    không?

## 12. Ghi chú triển khai

-   Release 1 chỉ bao gồm GD1 + GD2.
-   GD3 và GD4 không phải điều kiện hoàn thành Release 1.
-   Ưu tiên CPU, self-host và các thành phần có thể chạy bằng Docker.
-   Chất lượng tìm kiếm tiếng Việt, hiệu năng CPU, định dạng Office sau
    chuyển đổi và giới hạn file lớn phải được kiểm thử thực tế.
-   Các câu hỏi mở cần được chốt trước khi coi đây là bản đặc tả hoàn
    chỉnh để giao triển khai.
