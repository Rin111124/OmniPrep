# Lộ trình nâng cấp module thu thập và bóc tách đề

Tài liệu này mô tả hiện trạng, mục tiêu và các bước triển khai module nhập đề của OmniPrep. Phạm vi bao gồm nhập đề thủ công, tìm nguồn đề tự động, tải và chuyển đổi tài liệu, bóc tách câu hỏi, kiểm tra chất lượng, chống trùng và kiểm duyệt.

> Đây là thiết kế và kế hoạch thực hiện, không phải xác nhận rằng chức năng tìm kiếm nguồn, tải PDF hoặc OCR đã được triển khai. Chỉ câu hỏi được người có chuyên môn duyệt mới được đưa vào ngân hàng dùng để tạo đề.

## 1. Mục tiêu

- Nhận đề từ văn bản, PDF có text và PDF/ảnh scan qua OCR.
- Tìm tài liệu mới từ các nguồn đã đăng ký và được phép sử dụng.
- Tự tải, lưu nguồn, trích xuất văn bản, bóc tách và kiểm tra câu hỏi.
- Cho phép xem lại tài liệu gốc và đối chiếu từng câu trước khi duyệt.
- Chạy nền có retry hữu hạn, chống xử lý trùng và theo dõi được trạng thái, chi phí, lỗi.
- Giữ mặc định mọi câu do AI tạo ở trạng thái chờ người duyệt.

## 2. Hiện trạng trong mã nguồn

| Thành phần | Hiện trạng |
|---|---|
| Nhập thủ công | Trang admin nhận nội dung dán vào hoặc file `.txt`; API xử lý trực tiếp trong request. |
| PDF, DOCX, ảnh | Chưa hỗ trợ. UI chỉ nhận `.txt`; OCR chưa được triển khai. |
| Tìm nguồn | `ScraperService` chỉ đọc `.txt`/`.md` trong `scratch/incoming_papers`; chưa tìm kiếm web hoặc tải tài liệu từ xa. |
| Bóc tách | `GeminiService` dùng structured output và prompt riêng cho GEO, GDKTPL. Chia phần dựa trên tiêu đề phần; không có chia đoạn tổng quát theo kích thước hoặc xác nhận đủ số câu. |
| Kiểm tra đầu ra | Có deserialize JSON, nhưng chưa có bước kiểm tra đầy đủ cấu trúc `AnswerData` theo định dạng câu hỏi trước khi lưu. |
| Chống trùng | Hash nội dung câu hỏi và tìm gần giống qua embedding/pgvector. Bảng, đáp án và ngữ cảnh chưa được tính vào hash chính xác. |
| Kiểm duyệt | Câu mới chưa xác minh; API và trang admin có duyệt, từ chối và hiệu đính. Biến thể AI cũng được đưa vào hàng chờ duyệt. |
| Worker | Có worker liên tục với lease/retry. Blueprint `render.yaml` hiện chỉ triển khai API nên không tự chạy worker. UI admin gọi endpoint bóc tách đồng bộ; không gửi job cho worker. |

Các điểm trên được xác nhận trong `frontend/src/app/admin/page.tsx`, `src/OmniPrep.Api/Endpoints/IngestionEndpoints.cs`, `src/OmniPrep.Infrastructure/Services/ScraperService.cs`, `src/OmniPrep.Infrastructure/Services/GeminiService.cs`, `src/OmniPrep.Infrastructure/Services/IngestionService.cs` và `src/OmniPrep.Worker/Program.cs`.

## 3. Luồng đích

```text
Nguồn được duyệt
      ↓
Lịch tìm kiếm → ứng viên tài liệu → kiểm tra URL/quyền sử dụng/định dạng
      ↓                                  ↓
                         hàng đợi tải và xử lý
                                      ↓
               lưu bản gốc + hash + metadata nguồn
                                      ↓
          trích văn bản (PDF text) hoặc OCR khi cần
                                      ↓
            chia đoạn có liên kết về vị trí nguồn
                                      ↓
               Gemini structured extraction
                                      ↓
      kiểm tra schema + quy tắc theo format + phát hiện thiếu
                                      ↓
            chống trùng / gắn nguồn / lưu câu chờ duyệt
                                      ↓
              giáo viên đối chiếu và duyệt
                                      ↓
                    ngân hàng câu hỏi
```

Mỗi bước phải lưu trạng thái và lỗi riêng để có thể retry từ bước lỗi mà không tải hoặc bóc tách lại toàn bộ tài liệu khi không cần thiết.

## 4. Tìm kiếm và quản lý nguồn đề

### 4.1 Danh mục nguồn

Tạo danh mục nguồn do admin quản lý. Mỗi nguồn cần có tối thiểu:

- Tên, môn học, đơn vị phát hành, URL gốc và loại nguồn.
- Trạng thái `PendingReview`, `Active`, `Paused` hoặc `Blocked`.
- Phạm vi URL/domain được phép truy cập và phương thức tìm tài liệu (RSS, sitemap, trang danh mục hoặc API công khai).
- Điều khoản sử dụng/giấy phép hoặc ghi chú quyền sử dụng, ngày đánh giá và người xác nhận.
- Chu kỳ quét, giới hạn request, loại file chấp nhận và giới hạn dung lượng.
- Lần quét gần nhất, lần thành công gần nhất và thông tin lỗi vận hành.

Không bật nguồn chỉ bằng cách nhập URL tùy ý. Nguồn mới cần người quản trị xem xét quyền sử dụng, độ tin cậy, nội dung và cách truy cập trước khi kích hoạt.

### 4.2 Quy tắc tìm và tải tài liệu

- Giai đoạn đầu chỉ hỗ trợ adapter cho nguồn đã allowlist; không xây crawler mở có thể đi theo mọi liên kết trên Internet.
- Ưu tiên RSS, sitemap hoặc API công khai do nguồn cung cấp. Chỉ dùng HTML parsing khi cần và khi điều khoản nguồn cho phép.
- Tôn trọng điều khoản sử dụng, robots.txt khi áp dụng, giới hạn tốc độ và yêu cầu ghi nguồn. Không vượt đăng nhập, paywall, CAPTCHA hay biện pháp kiểm soát truy cập.
- Chỉ cho phép `https` và domain đã đăng ký; chặn địa chỉ loopback, mạng nội bộ, link-local và redirect sang host không được phép để tránh SSRF.
- Kiểm tra MIME type, phần mở rộng, kích thước, thời gian tải và chữ ký nội dung file. Không tin tên file do máy chủ nguồn trả về.
- Lưu URL chuẩn hóa, URL cuối sau redirect, tiêu đề, ngày phát hành (nếu có), thời điểm thu thập, hash file và ghi chú quyền sử dụng.
- Áp giới hạn số tài liệu mỗi nguồn/ngày, số request mỗi domain và ngân sách AI theo ngày.

### 4.3 Tự động hóa có kiểm soát

Khi một nguồn đang `Active`, worker có thể định kỳ tìm tài liệu mới và tự tải/bóc tách vào hàng chờ. Việc tự động này **không tự duyệt hoặc xuất bản câu hỏi**. Tài liệu mới không đủ metadata, lỗi quyền sử dụng, không xác định được môn hoặc có nội dung bất thường phải chuyển sang hàng chờ xem xét nguồn/tài liệu.

## 5. Tải tài liệu và trích xuất văn bản

Triển khai theo thứ tự để dễ vận hành:

1. Giữ nhập `.txt` và văn bản dán hiện có.
2. Thêm PDF có lớp text, giới hạn số trang/dung lượng và lưu vị trí trang cho từng đoạn.
3. Thêm DOCX nếu cần sau khi đã có kiểm tra file an toàn.
4. Thêm OCR cho PDF scan/ảnh; lưu confidence, số trang và cảnh báo OCR chất lượng thấp.

Mỗi đoạn trích xuất nên mang metadata: trang, khoảng ký tự, tiêu đề phần và hash văn bản chuẩn hóa. Khi tạo câu hỏi, lưu lại liên kết đoạn nguồn để người duyệt mở đúng trang và đối chiếu câu/đáp án.

Không chạy file Office/PDF như chương trình. Bộ giải nén phải giới hạn số entry, kích thước sau giải nén, thời gian xử lý và từ chối file lỗi hoặc vượt giới hạn.

## 6. Bóc tách và kiểm tra chất lượng

### 6.1 Chia đoạn

- Chuẩn hóa Unicode và xuống dòng nhưng giữ nguyên nội dung, công thức, bảng, nhãn câu và dấu tiếng Việt.
- Phát hiện cấu trúc đề bằng tiêu đề phần/câu; nếu không nhận dạng chắc chắn, chia đoạn theo giới hạn token có overlap thay vì gửi tài liệu dài trong một request.
- Giữ đủ ngữ cảnh cho câu phụ thuộc đoạn dẫn/bảng; không tách câu khỏi bảng hoặc tình huống chung.
- Ghi nhận số câu dự kiến theo mục lục/nhãn câu nếu nhận diện được để so sánh với số câu đã bóc tách. Chênh lệch phải gắn cờ, không coi một mảng JSON hợp lệ là bằng chứng đã bóc tách đủ.

### 6.2 Đầu ra có cấu trúc

Mỗi câu cần có `content`, `format`, `difficulty`, `topic`, `answer_data`, cùng metadata nguồn. Validator phía server phải kiểm tra tối thiểu:

- `MULTIPLE_CHOICE`: lựa chọn cần thiết có mặt, đáp án tham chiếu tới lựa chọn tồn tại.
- `TRUE_FALSE`: có đúng bốn mệnh đề với ID duy nhất và mỗi mệnh đề có giá trị đúng/sai.
- `SHORT_ANSWER`: có đáp án số hoặc tập đáp án chữ hợp lệ; dung sai không âm; đơn vị được lưu riêng.
- Tất cả format: nội dung/chủ đề không rỗng, enum hợp lệ, độ dài trường trong giới hạn, không có câu rỗng/trùng trong cùng kết quả.
- Kiểm tra format của câu có phù hợp với phần đề và subject đã chọn; cảnh báo thay vì tự đổi dữ liệu mơ hồ.

Nếu Gemini trả lỗi, JSON lỗi, bị cắt, bị chặn hoặc không đạt validator, lưu lỗi và đưa job sang retry/thất bại có thể xem lại. Không đánh dấu tài liệu hoàn tất với 0 câu trích xuất. Không ghi nội dung đề đầy đủ, đáp án hoặc API key vào log lỗi.

### 6.3 Ràng buộc AI

Nội dung nguồn là dữ liệu không tin cậy. Prompt cần nói rõ bỏ qua chỉ dẫn nằm trong tài liệu; chỉ trích xuất nội dung đề theo schema. Không cho nội dung tài liệu quyền gọi tool, truy cập URL hoặc thay đổi cấu hình. Cần giới hạn input/output token và chi phí; có thể tắt sinh biến thể AI độc lập với chức năng bóc tách gốc.

## 7. Chống trùng và lưu nguồn

- Hash file chuẩn hóa để bỏ qua tải lại cùng tài liệu; hash phải có tính ổn định khi URL hoặc metadata thay đổi.
- Hash câu nên bao gồm nội dung chuẩn hóa cùng bảng/ngữ cảnh gắn liền và dữ liệu cấu trúc thích hợp. Không hash đáp án vào hash nội dung nếu mục tiêu là phát hiện hai bản câu có stem giống nhưng đáp án khác; trường hợp xung đột cần chuyển kiểm duyệt.
- Semantic dedup phải lọc theo môn và dùng ngưỡng được đánh giá trên tập ví dụ gán nhãn. Không tự gộp các kết quả gần giống có thể khác số liệu, bảng, đáp án hoặc điều kiện.
- Lưu liên kết nhiều nguồn và số lần xuất hiện thực tế; chỉ tăng occurrence khi ghi nhận nguồn mới, không tăng do retry cùng một tài liệu.
- Khi phát hiện ứng viên giống, lưu quyết định `PossibleDuplicate` để reviewer gộp/tách; không bỏ dữ liệu ứng viên một cách âm thầm.

## 8. Hàng đợi, trạng thái và giao diện duyệt

Nên tách trạng thái tài liệu khỏi trạng thái câu hỏi. Trạng thái tài liệu gợi ý:

`Discovered → AwaitingSourceReview → Queued → Downloading → ExtractingText → Parsing → Validating → AwaitingQuestionReview → Completed`

Trạng thái ngoại lệ: `SkippedDuplicate`, `RetryScheduled`, `Failed`, `Blocked`. Mỗi chuyển trạng thái lưu thời điểm, số lần thử, lỗi đã làm sạch và phiên bản pipeline/model.

Giao diện reviewer cần hiển thị cạnh nhau:

- Tài liệu/đoạn nguồn, trang và URL nguồn.
- Câu đã bóc tách, bảng/hình liên quan, đáp án và lời giải.
- Cảnh báo validator, độ tin cậy OCR và chênh lệch số câu dự kiến/thực tế.
- Tùy chọn sửa, duyệt, từ chối, báo lỗi nguồn và đánh dấu nghi trùng.

Tài liệu được coi là `Completed` khi toàn bộ câu đã có quyết định hoặc được ghi rõ là không tạo câu; không đồng nghĩa tất cả câu đã xuất bản.

## 9. Thiết kế thành phần đề xuất

- **Source registry/API:** quản lý nguồn và trạng thái allowlist.
- **Discovery adapters:** từng adapter RSS/sitemap/API/HTML cho một nguồn, có giới hạn request.
- **Document fetcher:** kiểm soát domain, redirect, MIME, kích thước và hash.
- **Document extraction:** bộ trích PDF text, DOCX nếu cần, OCR như adapter riêng.
- **Ingestion orchestrator:** điều phối job idempotent theo trạng thái, retry và lease.
- **Gemini extraction:** prompt/schema theo môn, giới hạn token, timeout và xử lý lỗi provider.
- **Question validator:** kiểm tra schema và quy tắc nghiệp vụ độc lập với Gemini.
- **Deduplication:** hash + so khớp semantic có kết quả reviewable.
- **Moderation UI:** đối chiếu câu với nguồn trước khi xuất bản.
- **Telemetry:** số tài liệu, số trang, câu trích xuất/hợp lệ/bị từ chối, retry, thời gian, token/chi phí và lỗi theo nguồn/model.

Tái sử dụng worker hiện có cho xử lý nền, nhưng đưa thao tác nhập admin vào cùng cơ chế enqueue thay vì chạy Gemini trong HTTP request. API trả về `paperId`/`jobId`; frontend thăm dò hoặc nhận cập nhật trạng thái. Cần triển khai worker chạy liên tục trên hosting; Render blueprint hiện tại chỉ có API.

## 10. Lộ trình thực hiện

### P0 — Làm đầu ra an toàn và đo được

1. Thêm validator theo format và chặn kết quả lỗi trước khi lưu.
2. Phát hiện JSON bị cắt, phản hồi rỗng, 0 câu và chênh lệch số câu; ghi trạng thái rõ ràng.
3. Đưa admin ingestion vào hàng đợi worker; lưu tổng số câu và trạng thái đầy đủ cho cả luồng admin.
4. Bổ sung nguồn gốc theo đoạn/câu và giao diện đối chiếu nguồn.
5. Tách bật/tắt sinh biến thể; đặt mặc định tắt khi chạy pilot để tập trung đánh giá chất lượng câu gốc.
6. Thu thập tập đề kiểm thử có đáp án chuẩn đã được giáo viên xác nhận; đo độ bao phủ câu và độ chính xác đáp án.

### P1 — Tự tìm nguồn có allowlist

1. Thêm bảng/API quản lý nguồn và quy trình phê duyệt nguồn.
2. Chọn 1–2 nguồn công khai được phép sử dụng; triển khai adapter discovery riêng cho từng nguồn.
3. Tải tài liệu qua fetcher có bảo vệ SSRF, giới hạn dung lượng/tốc độ và lưu metadata/hash.
4. Tạo hàng chờ ứng viên để admin xem nguồn/tài liệu; bật tự enqueue chỉ cho nguồn `Active`.
5. Đặt giới hạn quota theo nguồn/ngày và nút pause toàn bộ discovery.

### P2 — PDF và OCR

1. Hỗ trợ PDF có text, giữ số trang và kiểm tra độ phủ.
2. Bổ sung OCR cho file scan sau khi đánh giá chất lượng trên bộ đề thực tế.
3. Thêm cảnh báo OCR confidence thấp và yêu cầu người duyệt đối chiếu ảnh gốc.

### P3 — Mở rộng an toàn

Mở thêm nguồn/môn sau khi đánh giá chất lượng, quyền sử dụng, quota và vận hành của từng nguồn. DOCX, hình trong đề, biểu đồ và sơ đồ cần có adapter/đánh giá riêng; không giả định OCR văn bản xử lý đúng các dạng này.

## 11. Chỉ số và tiêu chí nghiệm thu

Đặt ngưỡng cụ thể sau khi có baseline trên tập đề chuẩn. Mỗi phiên bản pipeline cần báo cáo:

- **Coverage:** số câu được bóc tách / số câu xác định trong đề.
- **Schema pass rate:** tỷ lệ câu qua validator.
- **Answer accuracy:** tỷ lệ đáp án được giáo viên xác nhận đúng.
- **Content fidelity:** tỷ lệ câu/bảng/đơn vị được chép đúng theo nguồn.
- **Duplicate precision:** tỷ lệ cảnh báo trùng là đúng; theo dõi riêng tỷ lệ gộp nhầm.
- **Review rejection/edit rate:** câu bị từ chối hoặc phải sửa đáp án/nội dung.
- **Operations:** lỗi và retry theo nguồn, thời gian xử lý, chi phí trung bình/đề, job tồn đọng.

Điều kiện pilot:

- Không câu nào được xuất bản nếu chưa qua validator và reviewer có chuyên môn.
- Reviewer truy cập được nguồn gốc cho mọi câu.
- Job retry không tạo câu/nguồn trùng; job lỗi có thể khởi chạy lại có kiểm soát.
- Discovery chỉ truy cập nguồn/domain được duyệt và có giới hạn chi phí/tần suất.
- Kiểm thử trên đề đa dạng: format chuẩn, thiếu tiêu đề phần, bảng, lỗi OCR, đề dài, đề thiếu đáp án, nội dung trùng và phản hồi Gemini bị lỗi/cắt.
- Báo cáo metric trên tập chuẩn trước khi mở rộng tự động hóa.

## 12. Rủi ro và biện pháp

| Rủi ro | Biện pháp |
|---|---|
| Nguồn không cho phép thu thập/tái sử dụng | Allowlist, lưu căn cứ quyền sử dụng, rà soát định kỳ và nút pause nguồn. |
| URL độc hại/SSRF/redirect | Chỉ HTTPS/domain cho phép, kiểm tra từng redirect, chặn mạng nội bộ và giới hạn tải. |
| AI bỏ câu hoặc đoán sai đáp án | Coverage check, validator, nguồn đối chiếu, reviewer bắt buộc và metric trên tập chuẩn. |
| OCR sai số liệu/đơn vị | Lưu trang ảnh, confidence, cảnh báo và đối chiếu trước duyệt. |
| Semantic dedup gộp câu khác đáp án | Không auto-merge trường hợp gần giống; phân loại trùng cần reviewer. |
| Gemini vượt quota/chi phí | Token cap, giới hạn theo ngày/nguồn, theo dõi chi phí, pause/circuit breaker. |
| Worker không chạy ở môi trường demo | Triển khai background worker liên tục hoặc tắt discovery enqueue; hiển thị rõ job đang chờ. |

## 13. Việc tiếp theo

1. Chốt nguồn đề đầu tiên và xác nhận quyền sử dụng/cách truy cập.
2. Chốt tập đề mẫu và nhờ giáo viên gán nhãn câu, đáp án, bảng và số câu kỳ vọng.
3. Thực hiện P0 trước để mọi đầu ra có validator, provenance và luồng nền.
4. Xây một discovery adapter P1 cho nguồn đã chọn; chưa bật crawler tổng quát.
5. Đánh giá metric và chi phí trên staging trước khi lập lịch quét tự động.
