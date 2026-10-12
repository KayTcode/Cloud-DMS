# KE HOACH SUA LOI — Cloud-DMS

> Nguồn: `review/00_REVIEW_TOAN_BO.md` (40 bug, điểm 3.7/10) + `review/01_TEST_EVIDENCE.md` (bằng chứng test thật).
> Mục tiêu: đưa hệ thống từ **không thể triển khai** → **an toàn cho production nội bộ**.

---

## 1. NGUYEN TAC

1. **Sửa nguyên nhân, không sửa triệu chứng.** Không thêm `try/catch {}` để che lỗi, không ghi comment `// ignore` mới.
2. **Không chặn test để cho qua.** Mọi bước sửa đều phải kèm cách kiểm chứng lại (xem `06_CHECKLIST_KIEM_NGHIEM.md`).
3. **Mỗi task có commit riêng** — dễ revert.
4. **Không thêm chức năng mới** trong Phase 1–2 (chỉ sửa). Feature mới chờ Phase 3+.
5. **Không hard-code fallback secret** — thiếu config thì phải `throw`, không dùng giá trị mặc định.

---

## 2. 4 PHA + TIMELINE

| Phase | Tên | Phạm vi | Bug ID | Ước lượng |
|---|---|---|---|---|
| **1** | 🔴 Chốt cửa | Auth toàn cục, IDOR, impersonation, secret, path traversal, exception handler | BUG-01…07, 13, 16 | **~2–3 ngày** |
| **2** | 🟠 Chạy đúng | Migration/DB, logic quota, transaction/race, role, exception nhất quán | BUG-08…12, 15, 20…23, 26, 34, 35, 39 | **~4–6 ngày** |
| **3** | 🟡 Vận hành | Rate limit, refresh token, pagination/index, docs, ClamAV, email, CORS/HTTPS, thiếu chức năng | BUG-14, 17…19, 24, 25, 27…31, 38 | **~5–7 ngày** |
| **4** | 🟢 Chất lượng | Test, CI, Docker, dọn dead code, README, benchmark | BUG-32, 33, 36, 37, 40 | **~4–5 ngày** |

**Tổng ước lượng: ~15–21 ngày làm việc** (1 người, không tính thời gian review/QA).

**Chỉ số nghiệm thu chung (Definition of Done):**

- [ ] `dotnet build -c Release` = **0 error**
- [ ] Mọi endpoint không anonymous phải trả **401 khi không token**, **403 khi sai quyền**
- [ ] Không còn endpoint nào trả `ex.Message`/stack trace ra client
- [ ] Không còn secret nào trong git (đã xoá + đã đổi khoá thật)
- [ ] `git grep` không còn `catch {}` nuốt lỗi migration
- [ ] Checklist `06_CHECKLIST_KIEM_NGHIEM.md` qua hết ở Phase tương ứng

---

## 3. THU TU PHU THUOC

```
Phase 1 (bắt buộc làm trước — mọi thứ khác vô nghĩa nếu API vẫn anonymous)
  P1-01 FallbackPolicy ──┐
  P1-02 Policy per-endpoint ──┼──> Phase 2 (logic chạy trên nền đã được bảo vệ)
  P1-03 Bỏ impersonation ──┤
  P1-04 Tenant scope ──────┘
  P1-06 Secret rotate  ← có thể làm song song, làm NGAY (độc lập)
Phase 2
  P2-01 Migration  ← LÀM TRƯỚC nếu môi trường dev/DB đang 500 (đã chứng minh)
  P2-02 EF config trùng ← phụ thuộc P2-01
  P2-03…P2-09 quota/transaction ← phụ thuộc P1-01 (cần auth để test đúng)
Phase 3 ← phụ thuộc Phase 2
Phase 4 ← làm song song từ đầu được (test viết sớm hơn càng tốt)
```

**Lưu ý vận hành:**
- Việc **đổi app password Gmail + JWT key** thuộc Phase 1 nhưng cần liên hệ người quản trị email → làm trước, trong ngày.
- **DB dev đang có dữ liệu thật** (có user thật `huynthe180817@fpt.edu.vn`) → trước khi sửa migration phải **backup** `mysqldump CloudDmsDb > backup_$(date).sql`.

---

## 4. RUI RO & CACH GIAM

| Rủi ro | Cách giảm |
|---|---|
| Thêm `[Authorize]` break frontend đang gọi API anonymous | Liệt kê toàn bộ endpoint + code 401 trước, sửa trong 1 commit, báo frontend cập nhật headers; giữ Swagger Production bật để test nhanh |
| Migration trên DB có dữ liệu thật | Backup trước; chạy trên DB test trước; dùng `dotnet ef migrations` có kiểm soát |
| Sửa quota logic làm sai số liệu cũ | Thêm script đối soát (đọc 4 bảng, in chênh lệch) chạy trước/sau |
| Secret đã lộ trong git history | Đổi khoá thật (không chỉ xoá file); history purge chỉ làm khi có đồng thuận |

---

## 5. CAC FILE KE HOACH

| File | Nội dung |
|---|---|
| `01_PHASE1_CHOT_CUA.md` | 🔴 8 task Critical |
| `02_PHASE2_DUNG_DUNG.md` | 🟠 10 task logic/DB |
| `03_PHASE3_VAN_HANH.md` | 🟡 8 task vận hành |
| `04_PHASE4_CHAT_LUONG.md` | 🟢 6 task chất lượng |
| `05_BANG_THEO_DOI.md` | Bảng map **40 bug → task → trạng thái** |
| `06_CHECKLIST_KIEM_NGHIEM.md` | Lệnh test cụ thể cho từng nhóm fix |
