# Đặc tả triển khai OCR đề thi trên trình duyệt

## 1. Mục đích

Chuyển nhận dạng chữ từ ảnh đề thi khỏi API để giảm payload và CPU/RAM cần trên Render Free. Trình duyệt người dùng thực hiện OCR; API hiện có tiếp tục nhận văn bản để tách câu, kiểm tra dữ liệu, chống trùng và đưa câu hỏi vào hàng chờ duyệt.

Tài liệu này là đặc tả triển khai, không có nghĩa chức năng đã được viết trong code.

## 2. Quyết định kiến trúc

### Luồng đề xuất

```text
Ảnh / PDF scan
      ↓
Trình duyệt: đọc trang → hiển thị preview → OCR tiếng Việt bằng Web Worker
      ↓
Người dùng xem và sửa văn bản OCR, giữ nhãn trang
      ↓
Gửi rawContent dạng multipart qua API đang có
      ↓
Gemini text extraction → QuestionValidator → chống trùng → chờ duyệt
```

- Dùng **Tesseract.js trong Web Worker** để OCR ảnh PNG/JPEG/WebP/BMP và ảnh trang PDF.
- Dùng **PDF.js** trong browser để render PDF scan theo trang thành canvas/image trước khi gửi qua Tesseract.js. Tesseract.js không đọc PDF trực tiếp; hướng dẫn của dự án nêu PDF.js là một cách render PDF thành ảnh trước khi OCR.
- PDF text-native có thể tiếp tục đi qua `PdfDocumentExtractor` hiện có. Với PDF hỗn hợp, cần phân loại theo trang hoặc cho admin chọn OCR toàn bộ PDF; không gửi cả PDF lên Gemini Vision mặc định.
- Gửi `rawContent` đã OCR vào endpoint `/api/backend/admin/ingest`. Trường này đã được hỗ trợ ở `submitExamForIngestion`; không cần gửi byte ảnh tới Render trong luồng OCR chính.
- Gemini vẫn có thể dùng để biến text thành câu hỏi có cấu trúc. Thay đổi này loại Gemini Vision khỏi đường chính, không loại bỏ mọi lời gọi Gemini.

Tesseract.js dùng WebAssembly và Web Worker trong trình duyệt; dự án có ví dụ tạo một worker dùng lại cho nhiều ảnh. Bộ dữ liệu `vie` có sẵn trong Tesseract. [Tesseract.js README](https://github.com/naptha/tesseract.js/blob/master/README.md), [danh sách dữ liệu ngôn ngữ](https://github.com/tesseract-ocr/tessdoc/blob/main/Data-Files.md).

PDF.js cung cấp API đọc tài liệu, lấy trang và render trang lên canvas. [Ví dụ chính thức PDF.js](https://github.com/mozilla/pdf.js/blob/master/docs/contents/examples/index.md).

## 3. Vì sao phù hợp với Render Free

OCR chạy trên thiết bị người dùng nên không chiếm CPU/RAM của API Render và ảnh không cần đi qua Next proxy/API. Render Free web service hiện có 0,1 CPU và 512 MB RAM, tự spin down sau 15 phút không có request và filesystem là ephemeral; các giới hạn này khiến OCR server-side hoặc lưu ảnh trên ổ đĩa của service không phù hợp cho luồng này. [Render Free](https://render.com/docs/free), [compute plans](https://render.com/docs/compute-plans).

Trình duyệt vẫn cần tải worker, WASM và language model. Tesseract.js mặc định có thể lấy các file này từ CDN; tài liệu khuyến nghị cấu hình đường dẫn worker/core/ngôn ngữ nếu muốn tự lưu trữ. Để tránh phụ thuộc CDN không pin phiên bản, production nên phục vụ asset đã pin từ static hosting của frontend. [Tesseract.js local installation](https://github.com/naptha/tesseract.js/blob/master/docs/local-installation.md).

## 4. Hành vi theo loại đầu vào

| Đầu vào | Cách xử lý |
|---|---|
| Ảnh chụp trang đề | Preview/rotate → OCR `vie` (thêm `eng` nếu tập đề có tiếng Anh) → người dùng kiểm tra/sửa → gửi text. |
| PDF text-native | Giữ luồng hiện tại dùng PdfPig; cân nhắc so sánh số trang và text để phát hiện PDF chỉ có text một phần. |
| PDF scan | Dùng PDF.js render từng trang → OCR theo trang → ghép văn bản theo thứ tự. |
| PDF hỗn hợp text/scan | MVP cho admin chọn “OCR tất cả trang” nếu không chắc. Bản sau lấy text từng trang, OCR trang thiếu text hoặc text không đạt ngưỡng, rồi giữ thứ tự trang. |
| PDF lỗi, mã hóa hoặc quá dài | Báo rõ trang/lý do; không gửi sang API như thể đã OCR thành công. Cho phép xử lý lại bằng cách tải trang ảnh riêng. |

Không tự chạy OCR và Gemini song song. Trước hết phải để admin xem text nhận dạng; chỉ sau khi xác nhận mới gọi API bóc tách.

## 5. Trải nghiệm người dùng cần có

Trên trang admin:

1. Phân biệt lựa chọn `OCR trên thiết bị` và `PDF có text`.
2. Hiện số trang/ảnh, thứ tự trang, preview, xoay, xóa trang và giới hạn kích thước.
3. Hiện tiến độ theo trang: đang tải model lần đầu, đang render trang, đang nhận dạng trang N/M, hoàn tất.
4. Cho hủy OCR; khi hủy hoặc unmount phải terminate worker, hủy PDF render và thu hồi object URL/canvas.
5. Hiển thị vùng văn bản OCR có nhãn `[Trang 1]`, `[Trang 2]`; người dùng sửa văn bản trước khi tiếp tục.
6. Đánh dấu trang OCR rỗng/ít ký tự để người dùng xem lại hoặc chụp lại.
7. Nêu rõ bước OCR chỉ đọc chữ; không tự xác định đáp án chuẩn. Tùy chọn giải đáp AI là bước riêng, mặc định tắt.
8. Sau khi gửi text, dùng trạng thái xử lý ingest hiện có và hiển thị kết quả/validator errors.

OCR là thao tác client-side, nhưng việc gọi Gemini text extraction vẫn gửi nội dung đề đến API/Gemini theo cấu hình hiện tại. Nêu điều này ở UI quản trị trước khi người dùng xác nhận xử lý.

## 6. Thiết kế kỹ thuật frontend

### 6.1 Phân tách module

Không đặt toàn bộ OCR vào `frontend/src/app/admin/page.tsx`. Tách thành các module đề xuất:

- `frontend/src/lib/ocr/types.ts`: kiểu `OcrPageResult`, `OcrProgress`, lỗi theo trang và trạng thái.
- `frontend/src/lib/ocr/image-ocr.ts`: khởi tạo/dùng lại Tesseract worker, OCR một ảnh, kết thúc worker.
- `frontend/src/lib/ocr/pdf-render.ts`: tải PDF.js động ở client, đếm trang và render tuần tự từng trang.
- `frontend/src/components/admin/ExamOcrPanel.tsx`: preview, tiến độ, chỉnh sửa, hủy và gửi kết quả.
- `frontend/src/app/admin/page.tsx`: chỉ điều phối panel và metadata đề.

Tên file chỉ là gợi ý; giữ quy ước hiện có của repo nếu cấu trúc tương đương đã được tạo.

### 6.2 Next.js client-only

PDF.js, canvas, `File`, `URL.createObjectURL`, `window` và Web Worker chỉ được truy cập từ client. Import thư viện PDF/OCR động sau thao tác người dùng hoặc trong component client; không chạy chúng trong Server Component, route handler hoặc lúc build. Cấu hình `workerSrc` và đường dẫn asset theo bundler của phiên bản Next.js đang khóa trong `frontend/package.json`; xác minh production build vì Tesseract.js ghi nhận một số bundler cần cấu hình worker path thủ công.

### 6.3 Bộ nhớ và concurrency

- Tạo một Tesseract worker cho một phiên OCR, dùng lại cho nhiều trang tuần tự, rồi `terminate()` trong `finally`.
- Không tạo một worker cho mỗi ảnh; không render nhiều trang canvas cùng lúc.
- Chỉ giữ canvas trang hiện tại; sau khi OCR xong, giải phóng backing store (`width/height = 0`) và reference.
- Giới hạn số trang và tổng pixel phía client trước khi chạy OCR; dùng ngưỡng cấu hình có thể thay đổi sau khi benchmark thiết bị yếu.
- Không áp dụng giảm kích thước 1400 px / JPEG quality 0.75 như đường Gemini hiện tại một cách mặc định. OCR chữ nhỏ thường cần giữ đủ pixel; chỉ resize khi vượt ngân sách memory và cho preview để đối chiếu bản gốc.
- Đọc một trang một lần và cung cấp `logger`/progress từ worker; không ghi toàn bộ OCR text hoặc file content ra console.

### 6.4 Language model và asset

- Khởi đầu với `vie`; chỉ thêm `eng` nếu tập dữ liệu thực tế cho thấy cần thiết, vì model đa ngôn ngữ làm tăng tải ban đầu và bộ nhớ.
- Pin phiên bản `tesseract.js`, core/WASM và traineddata như một bộ tương thích; không tham chiếu `master` hoặc `latest` trong production.
- Ưu tiên lưu worker/core/traineddata trong static asset frontend hoặc storage CDN do ứng dụng kiểm soát. Nếu dùng CDN ngoài, cấu hình version cố định, timeout, lỗi tải và hướng dẫn thử lại.
- Cache asset/model của trình duyệt; thông báo tải lần đầu có thể lâu, không để spinner vô hạn.

### 6.5 Dữ liệu gửi API

Sau khi admin xác nhận, tạo chuỗi `rawContent` có cấu trúc:

```text
[Tài liệu: ten-de.pdf]
[OCR: Tesseract.js, ngôn ngữ: vie]

--- TRANG 1 ---
...nội dung nhận dạng...

--- TRANG 2 ---
...nội dung nhận dạng...
```

Gửi qua `submitExamForIngestion({ subjectCode, sourceName, examYear, rawContent, autoSolve: false })`. Không gửi file ảnh ở nhánh này. Cần xác nhận API có lưu đủ text đã xác nhận và trả trạng thái 0 câu phù hợp; không coi HTTP 200 là bằng chứng OCR/parse thành công.

## 7. Chia cột và cấu trúc đề

Tesseract trả text, không đảm bảo tự hiểu thứ tự đọc hai cột hay liên kết bảng với đúng câu. PDF.js render từng trang không tự khắc phục vấn đề layout.

- Giữ trang riêng, không nối nội dung nhiều trang rồi bỏ nhãn.
- Thử OCR toàn trang trước. Nếu đề hai cột bị đảo, cho phép crop trái/phải theo vùng người dùng chọn; chạy OCR hai vùng theo thứ tự trái rồi phải.
- Không tự dùng một quy tắc chia đôi cố định cho mọi trang: đề có tiêu đề, hình hoặc bảng rộng có thể không theo 2 cột.
- Cảnh báo khi số nhãn câu bị nhảy/lặp; người dùng xem lại trước khi gửi.
- Bảng, công thức, biểu đồ và sơ đồ cần được reviewer đối chiếu bản gốc. OCR text-only có thể mất quan hệ hàng/cột hoặc nội dung phi văn bản.
- Không tự suy đáp án khi ảnh chỉ có đề. Câu thiếu đáp án vẫn có thể nhập chờ duyệt nếu có thể xác nhận phần câu hỏi; đáp án AI nếu dùng phải ghi rõ nguồn.

## 8. Giới hạn và lỗi

- Lỗi tải WASM/traineddata: dừng, hiển thị nút thử lại và cách tiếp tục bằng PDF text/TXT hoặc đường Vision dự phòng.
- Trang không render được: báo đúng số trang, giữ file gốc phía client để người dùng thử tải lại trang đó dạng ảnh.
- OCR rỗng/quá ngắn hoặc lỗi: gắn trạng thái `NeedsReview`; không âm thầm gửi chuỗi rỗng.
- Hủy tác vụ: dừng worker/render; không gọi API ingest.
- Upload nhiều file: giữ thứ tự được người dùng xác nhận; không suy ra thứ tự alphabet nếu tên trang không chuẩn.
- Không lưu ảnh trên local filesystem Render. Nếu cần lưu bản gốc để reviewer đối chiếu sau khi browser đóng, dùng object storage riêng tư; việc này là một hạng mục độc lập, không thuộc lợi ích “ảnh không rời trình duyệt” của OCR.
- Đường Vision hiện có có thể giữ tạm như tùy chọn dự phòng, nhưng phải rõ ràng, giới hạn số trang/kích thước và không bật mặc định.

## 9. Kế hoạch giao việc

### P0 — Thử nghiệm OCR ảnh

1. Thêm dependency Tesseract.js phiên bản pin và phục vụ asset tương thích.
2. Tạo hook/service client-side dùng một worker tuần tự cho ảnh.
3. Tạo panel preview/progress/cancel/edit text; mặc định autoSolve tắt.
4. Gửi OCR text qua API text path, không gửi ảnh.
5. Đo trên ảnh đề in rõ, chữ nhỏ, có dấu tiếng Việt và ảnh xoay.

### P1 — PDF scan và mixed PDF

1. Thêm PDF.js dynamic client-only import, render tuần tự, giới hạn trang/pixel.
2. Cho admin chọn OCR tất cả trang hoặc giữ luồng PdfPig với xác nhận hỗ trợ PDF text.
3. Sau khi đo chất lượng, thêm phân loại theo trang: dùng text PDF khi đủ nội dung, OCR khi trang thiếu text.
4. Gắn nhãn trang trong text đầu vào, báo cáo trang thất bại.

### P2 — Layout và vận hành

1. Hỗ trợ crop cột/vùng chọn, sắp thứ tự trang và phát hiện nhãn câu bị thiếu.
2. Bổ sung bộ kiểm thử thủ công/gán nhãn và dashboard metric theo loại trang.
3. Tùy kết quả, giữ Vision dự phòng chỉ cho trang không đọc được hoặc tích hợp OCR service riêng nếu accuracy yêu cầu cao hơn.

## 10. Bộ đánh giá và tiêu chí nghiệm thu

Chuẩn bị tập ảnh/PDF có ground truth do giáo viên xác nhận, gồm ảnh sạch, xoay/lệch, mờ, bóng, chữ nhỏ, hai cột, bảng, biểu đồ, công thức, scan hỗn hợp và ghi chú bút. Tách tập phát triển và tập đánh giá để tránh chỉnh ngưỡng theo cùng một mẫu.

Đo:

- Character/word error rate trên text OCR và riêng cho dấu tiếng Việt, số liệu, đơn vị, dấu âm.
- Coverage: số câu có trong kết quả so với số câu trên bản chuẩn.
- Lỗi thứ tự cột, nối nhầm câu với bảng và mất nhãn trang.
- Tỷ lệ reviewer phải sửa, thời gian xử lý, tỷ lệ OCR rỗng/lỗi.
- Thời gian tải model lần đầu, thời gian/trang và peak memory trên thiết bị cấu hình thấp.
- Tỷ lệ schema pass và đáp án đúng của bước Gemini text extraction, đo tách biệt khỏi chất lượng OCR.

Đạt pilot khi:

- OCR chạy hoàn toàn client-side và gửi text lên API; không gọi Gemini Vision trong đường chính.
- Có tiến độ, hủy, thử lại và chỉnh văn bản trước khi ingest.
- Đề lỗi/trang lỗi/0 câu được báo đúng trạng thái, không hiện thành công giả.
- Tesseract/PDF.js worker và assets hoạt động trong production build/deploy, không phụ thuộc asset `latest`.
- Reviewer vẫn đối chiếu được trường hợp ảnh không đọc rõ; mọi câu cần duyệt trước xuất bản.
- Nhóm đặt ngưỡng accuracy/coverage dựa trên tập đánh giá trước khi bật rộng.

## 11. Điều không nên kỳ vọng

OCR trình duyệt giúp giảm tải Render và tránh gửi ảnh lên Gemini Vision, nhưng không mặc định chính xác hơn Vision trên bảng/biểu đồ/layout phức tạp. Nó cũng không tự cấu trúc đề hay đảm bảo đáp án. Kết quả nên xem là bản chép có thể sửa; validator và reviewer vẫn cần thiết.
