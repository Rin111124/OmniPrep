# Đánh giá và hướng dẫn cải thiện bóc tách đề từ ảnh

Tài liệu này đánh giá luồng xử lý ảnh/PDF scan hiện có và đề xuất cách làm cho kết quả chính xác, dễ đối chiếu và vận hành ổn định hơn.

## 1. Kết luận nhanh

Module **đã có bóc tách bằng Gemini Vision**, không chỉ là kế hoạch tương lai. Admin có thể tải nhiều ảnh hoặc PDF; API phân biệt PDF có text và PDF scan; câu hỏi đầu ra đi qua `QuestionValidator` trước khi lưu. Câu mới được lưu `IsVerified = false`, vì vậy AI chưa tự đưa câu vào ngân hàng đã duyệt.

Luồng hiện tại phù hợp để thử nghiệm có giáo viên kiểm tra. Chưa nên coi kết quả là đáng tin cậy tự động: ảnh gốc không được lưu bền để đối chiếu, cả bộ ảnh gửi trong một request, xử lý chạy đồng bộ, PDF trộn trang text/scan có thể bỏ sót trang scan, và tùy chọn tự giải đáp mặc định bật.

## 2. Luồng hiện tại

1. Admin chọn subject, thêm ảnh hoặc PDF/TXT và gửi multipart/form-data.
2. Frontend có thể nén/chuyển ảnh thành JPEG, tăng contrast và giới hạn kích thước tổng trước khi gửi.
3. API nhận tối đa 25 MB cho nhánh ảnh; loại ảnh theo phần mở rộng. Các ảnh được đóng gói trong một yêu cầu Vision.
4. Với PDF, `PdfDocumentExtractor` trích text bằng PdfPig. Nếu kết quả rỗng, PDF được gửi nguyên dạng sang Gemini Vision. Nếu có một phần text, chỉ nhánh text được chạy.
5. Gemini trả JSON theo schema; `IngestionService` kiểm tra từng câu với `QuestionValidator`, rồi áp dụng chống trùng và lưu câu mới chưa xác minh.
6. UI tải lại danh sách chờ duyệt.

Các điểm triển khai chính:

- UI, nén ảnh và tải nhiều trang: `frontend/src/app/admin/page.tsx`.
- Gửi multipart: `frontend/src/lib/api.ts`.
- Nhận ảnh/PDF và điều phối: `src/OmniPrep.Api/Endpoints/IngestionEndpoints.cs`.
- Trích text PDF: `src/OmniPrep.Infrastructure/Services/PdfDocumentExtractor.cs`.
- Gọi Gemini Vision: `src/OmniPrep.Infrastructure/Services/GeminiService.cs`.
- Kiểm tra câu, chống trùng và lưu: `src/OmniPrep.Infrastructure/Services/IngestionService.cs`.
- Quy tắc chất lượng đầu ra: `src/OmniPrep.Infrastructure/Validation/QuestionValidator.cs`.

## 3. Phát hiện cần xử lý

### P0 — Sửa trước khi dùng thường xuyên

1. **PDF hỗn hợp có thể mất câu.** API chỉ gọi Vision khi PdfPig trả về chuỗi rỗng. PDF có lớp text ở một số trang và ảnh scan ở trang khác sẽ đi theo nhánh text, bỏ qua nội dung scan. Cần trích text theo từng trang, đo độ phủ, rồi chỉ OCR các trang thiếu hoặc chất lượng text thấp; giữ thứ tự trang khi ghép kết quả.

2. **Ảnh gốc không sẵn để kiểm duyệt.** Nhánh ảnh chỉ lưu một chuỗi mô tả trong `ExamPaper.RawContent`; byte ảnh không được lưu vào object storage và `FileUrl` là null. Reviewer chỉ thấy câu đã bóc tách, khó xác minh bảng, biểu đồ, chỉ số nhỏ hoặc vị trí câu. Lưu file gốc ở bucket riêng tư với thời hạn lưu và quyền truy cập rõ ràng; tạo preview/trang tham chiếu cho hàng chờ duyệt.

3. **Giới hạn tải khác nhau giữa các tầng.** UI giới hạn payload khoảng 3.5 MB; Next proxy giới hạn 4.2 MB; API cho phép body 10 MB qua Kestrel và nhánh ảnh nêu mức 25 MB. Vì request đi qua proxy, giới hạn thực tế thấp hơn giới hạn API. Chọn một ngưỡng end-to-end an toàn, tính cả multipart/base64 overhead, hiển thị chính xác cho người dùng và có thông báo 413 rõ ràng.

4. **Gemini nhận cả nhóm ảnh trong một request.** Một tài liệu nhiều trang có thể vượt giới hạn provider hoặc output token, làm hỏng toàn bộ lần bóc tách và không xác định được trang gây lỗi. Chia theo trang hoặc nhóm nhỏ, giữ số trang/ID ảnh trong kết quả, sau đó ghép theo thứ tự và kiểm tra trùng câu xuyên nhóm.

5. **Tự giải đáp đang bật mặc định.** Prompt Vision yêu cầu AI tự chọn đáp án, giải đúng/sai và tính đáp án ngắn. Đây là suy luận của model, không phải OCR từ đáp án chính thức. Đặt mặc định tắt hoặc yêu cầu xác nhận rõ; nếu bật, gắn nhãn `AI-generated answer` và bắt reviewer kiểm tra đáp án/lời giải riêng trước khi duyệt.

6. **Tài liệu không có câu vẫn có thể được đánh dấu hoàn tất.** Endpoint chỉ đánh dấu `Failed` khi số câu lỗi bằng số câu trích xuất và lớn hơn 0. Nếu model trả rỗng hoặc tất cả đầu ra lỗi trước khi được tính là extracted, đề có thể thành `Completed`. Chuyển trường hợp 0 câu/0 câu hợp lệ sang `Failed` hoặc `NeedsReview`, kèm lý do; không báo thành công.

7. **Kiểm tra ảnh dựa trên phần mở rộng.** Phần mở rộng được ánh xạ trực tiếp sang MIME. API cần xác thực MIME/signature thực, từ chối file rỗng, số pixel quá lớn, ảnh không giải mã được và loại nội dung không hỗ trợ. Đặt giới hạn số trang/ảnh và giới hạn từng file ở server, không chỉ tổng payload ở UI.

### P1 — Tăng độ chính xác và khả năng sửa

- Nén ảnh hiện giới hạn cạnh dài khoảng 1400 px, đổi sang JPEG chất lượng 0.75, rồi áp contrast/brightness. Với đề chữ nhỏ, công thức, dấu âm, chỉ số trên/dưới, bảng dày hoặc biểu đồ, phép thu nhỏ và nén này có thể làm mất nét. Giữ bản gốc; chỉ tạo bản xử lý làm việc. Tách ảnh theo vùng/crop ở server khi cần và tránh phóng to/thu nhỏ quá mức.
- Thêm bước xem trước sau xử lý: người dùng có thể xoay, sắp thứ tự trang, bỏ trang mờ và xác nhận trang nào thuộc cùng một đề.
- Lưu provenance ở từng câu: file ID, trang, vùng ảnh (bounding box nếu khả thi), model/prompt version và confidence/quality flags. Không để model tự tạo vị trí nguồn nếu không xác định được.
- Giữ bảng/ảnh biểu đồ gắn với đúng câu; hiện câu có `AttachedTable` nhưng chưa thấy liên kết nguồn trang/vùng để đối chiếu.
- Phát hiện kết quả thiếu theo số nhãn câu, nhãn trang và số câu dự kiến; cảnh báo khoảng số bị khuyết hoặc thứ tự câu nhảy. Không tự suy ra là hoàn chỉnh chỉ vì JSON hợp lệ.
- Khi phân tích nhiều trang, không giả định tất cả ảnh thuộc cùng thứ tự. Lưu thứ tự upload và cho phép sắp xếp trước khi gửi.
- Không đưa stack trace (`ex.ToString()`) của API ra response production; trả mã lỗi an toàn cho UI, giữ chi tiết đã lọc trong log theo correlation ID.

## 4. Thiết kế xử lý ảnh đề xuất

```text
Upload → xác thực file → lưu file gốc riêng tư → tạo trang/preview
                                            ↓
PDF text extraction theo trang → đánh giá độ phủ/chất lượng
                 ảnh hoặc trang thiếu text → Vision/OCR theo nhóm nhỏ
                                            ↓
                  ghép text theo trang và thứ tự câu
                                            ↓
                trích câu theo vùng/ngữ cảnh trang
                                            ↓
          validator cấu trúc + phát hiện thiếu/trùng/không chắc chắn
                                            ↓
              hàng chờ duyệt có ảnh/trang nguồn bên cạnh
                                            ↓
                       duyệt thủ công rồi xuất bản
```

### Các nguyên tắc

- Tách hai tác vụ: **transcription** (chép chính xác những gì nhìn thấy) và **answer solving** (suy luận đáp án). Chạy độc lập, lưu trạng thái/nguồn gốc riêng. Mặc định không giải đáp.
- Mọi input ảnh/PDF là dữ liệu không tin cậy. Hướng dẫn model bỏ qua chỉ dẫn in trong tài liệu; không cho nội dung đề kích hoạt hành động/tool.
- Giữ nội dung OCR nguyên văn. Không để model tự “sửa” số liệu, đơn vị, đáp án in sẵn hay dấu câu mà không đánh dấu bất định.
- Nếu ảnh không đủ rõ, mệnh đề/bảng không đọc được, hoặc model không chắc, đánh dấu `NeedsReview` và yêu cầu tải lại/crop; không đoán.
- Đề có đáp án chính thức: tách file đáp án khỏi đề nếu có thể và liên kết rõ ràng. Không coi lời giải model tạo ra là đáp án chuẩn.
- Idempotency dựa trên hash file và thứ tự trang chuẩn hóa; retry cùng job không tạo thêm bản đề/câu mới.
- Thực hiện xử lý dài trong worker. HTTP chỉ xác thực, lưu file/enqueue và trả `paperId`; UI theo dõi trạng thái để tránh timeout.

## 5. Quy tắc validator riêng cho ảnh

Ngoài các quy tắc của `QuestionValidator`, kết quả ảnh nên có trạng thái kiểm tra nguồn:

- Trang nguồn tồn tại và thuộc đúng tài liệu.
- Câu có nội dung, nhãn số câu, format và lựa chọn/mệnh đề đọc được.
- Đáp án được xác định là `PrintedAnswerKey`, `AIInferred` hoặc `Unknown`; không nhập chung ba nguồn này.
- Bảng, hình, biểu đồ và phần dẫn liên quan được gắn với câu; thiếu phần dẫn thì đánh dấu lỗi.
- Số câu theo trang tăng hợp lý, có cảnh báo trùng hoặc nhảy nhãn.
- Số liệu/đơn vị/đáp án có ký tự dễ nhầm (`0/O`, `1/l`, dấu trừ, dấu phẩy thập phân) được đưa vào danh sách cần đối chiếu.
- Chất lượng trang được gắn nhãn `Good`, `LowResolution`, `Blurred`, `Skewed`, `LowContrast` hoặc `Unreadable`.

Validator không thể chứng minh đáp án đúng. Độ tin cậy OCR cũng không đồng nghĩa độ tin cậy đáp án; reviewer phải có thể sửa riêng nội dung và đáp án.

## 6. Kế hoạch triển khai

### Giai đoạn 1 — Độ tin cậy và lỗi xử lý

1. Sửa trạng thái 0 câu/0 câu hợp lệ; thêm kết quả `NeedsReview`.
2. Đồng bộ giới hạn body UI → proxy → API và giới hạn file/số ảnh ở API.
3. Tách `autoSolve`, mặc định tắt; thể hiện nhãn đáp án AI trên màn hình duyệt.
4. Thêm kiểm tra MIME signature, file rỗng/không giải mã được và số pixel.
5. Không trả stack trace cho client.

### Giai đoạn 2 — Giữ nguồn và xử lý theo trang

1. Lưu file gốc ở object storage riêng tư, URL có thời hạn và quyền đọc cho reviewer.
2. Với PDF, lưu text/kết quả theo từng trang; OCR riêng các trang không có lớp text hoặc text bất thường.
3. Với ảnh nhiều trang, chia nhóm nhỏ, có retry theo trang và tổng hợp kết quả idempotent.
4. Gắn câu với trang và vùng nguồn; hiển thị ảnh cạnh câu/đáp án trong trang kiểm duyệt.

### Giai đoạn 3 — Chất lượng thực tế

1. Tạo bộ ảnh đề đại diện: ảnh chụp thẳng/lệch, mờ, bóng, chữ nhỏ, hai cột, bảng, biểu đồ, công thức, đề có ghi chú bút và PDF hỗn hợp.
2. Giáo viên gán nhãn chuẩn từng câu, đáp án, bảng, trang và trường hợp không đọc được.
3. Đo riêng OCR fidelity, coverage, answer accuracy, validation pass rate, tỷ lệ reviewer sửa/từ chối, lỗi theo loại ảnh và chi phí/đề.
4. Thử các kích thước/quality preprocessing trên cùng tập ảnh. Chọn cấu hình theo độ chính xác ký tự nhỏ và công thức, không chỉ theo kích thước payload.
5. Chỉ bật tự động cho nguồn/loại đề có metric đạt ngưỡng do nhóm giáo viên thống nhất; vẫn giữ duyệt thủ công trước xuất bản.

## 7. Tiêu chí nghiệm thu

- UI, Next proxy và API thống nhất giới hạn upload; file vượt ngưỡng bị từ chối với thông báo hữu ích.
- PDF có text, PDF scan và PDF trộn text/scan đều trả trạng thái/coverage theo trang chính xác; không âm thầm bỏ trang.
- Mọi câu chờ duyệt có thể mở được trang/ảnh gốc và sửa nội dung/đáp án.
- Kết quả rỗng, model timeout, JSON lỗi, trang mờ hoặc câu invalid không được báo `Completed` thành công.
- Tắt tự giải đáp vẫn trích đầy đủ nội dung; bật tự giải đáp đánh dấu rõ nguồn đáp án là AI.
- Retry không nhân đôi tài liệu/câu hỏi; lỗi theo trang có thể thử lại riêng.
- Trên bộ chuẩn đã gán nhãn, báo cáo độ phủ, OCR fidelity, answer accuracy, tỷ lệ sửa/từ chối và chi phí theo môn/loại ảnh.
- Không câu nào được xuất bản nếu chưa qua validator và reviewer chuyên môn.

## 8. Ưu tiên đề xuất

Làm trước **trang nguồn + PDF hỗn hợp + trạng thái 0 câu + đồng bộ giới hạn upload + tắt mặc định AI auto-solve**. Đây là các điểm ảnh hưởng trực tiếp tới việc thiếu câu, khó phát hiện lỗi và không thể xác minh đầu ra. Sau đó mới tối ưu preprocessing hoặc tăng số ảnh mỗi lần gửi.

Thiết kế OCR ở trình duyệt thay Vision được mô tả trong [đặc tả triển khai OCR trên trình duyệt](browser-ocr-implementation.md).
