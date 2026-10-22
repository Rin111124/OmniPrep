# Đặc tả triển khai OCR đề thi trên trình duyệt

## 1. Mục đích

Chuyển nhận dạng chữ từ ảnh đề thi khỏi API để giảm payload và CPU/RAM cần trên Render Free. Trình duyệt người dùng thực hiện OCR; API hiện có tiếp tục nhận văn bản để tách câu, kiểm tra dữ liệu, chống trùng và đưa câu hỏi vào hàng chờ duyệt.

Tài liệu này là đặc tả triển khai và theo dõi tiến độ. Hiện tại các hạng mục **P0** (Bảo vệ phiên & khắc phục lỗi chặn) và **P1** (Sẵn sàng PDF & kiểm tra API) đã được triển khai đầy đủ và xác minh trong mã nguồn dự án (10/2026).

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

## 9. Kế hoạch cải tiến triển khai

Kế hoạch dưới đây ưu tiên xử lý các lỗi đã thấy khi rà soát code hiện tại. Hoàn thành P0 trước khi mở rộng cho người dùng; P1 là điều kiện phát hành chính thức; P2 cải thiện chất lượng và theo dõi vận hành.

### P0 — Sửa lỗi chặn và bảo vệ phiên OCR (✅ ĐÃ HOÀN THÀNH - 10/2026)

1. [x] **Sửa cleanup theo đúng vòng đời component.** Tách cleanup unmount khỏi state `pages` (dependency `[]`), lưu và thu hồi blob URLs qua ref; giải phóng URL khi xóa trang đơn lẻ hoặc unmount.
2. [x] **Định nghĩa trạng thái hủy nhất quán.** Cờ hủy `isCancelledRef`, bộ đếm phiên `sessionCounterRef` ngăn race condition. Khóa các thao tác thêm/xóa/đổi thứ tự khi OCR đang chạy.
3. [x] **Quản lý PDF task và tài nguyên.** Thêm `destroyPdfDocument()`, giải phóng `page.cleanup()`, canvas backing store được thu hồi ngay (`releaseCanvas`) sau mỗi trang.
4. [x] **Đặt giới hạn đầu vào trước khi giải mã.** Cấu hình `OCR_LIMITS`: tối đa 40 file, 25MB/file, 100MB tổng dung lượng, tối đa 50 trang PDF. Kiểm tra số trang trước khi render thumbnail.
5. [x] **Xử lý danh sách file rõ ràng.** Cảnh báo rõ ràng và từ chối khi chọn lẫn PDF và ảnh hoặc chọn nhiều file PDF cùng lượt; không bỏ sót file âm thầm.
6. [x] **Chặn gửi khi chưa rà soát.** Bắt buộc xác nhận bỏ qua các trang chưa hoàn tất/bị lỗi trước khi gửi. Khóa gửi khi toàn bộ trang chưa OCR hoặc nội dung rỗng.

**Tiêu chí hoàn thành P0:** Đã đạt đầy đủ tiêu chí trong mã nguồn frontend và kiểm thử.

### P1 — Sẵn sàng PDF và phát hành (✅ ĐÃ HOÀN THÀNH - 10/2026)

1. [x] **Đảm bảo asset PDF.js tải ổn định.** Timeout 30 giây cho PDF, 45 giây cho worker Tesseract với thông báo lỗi rõ ràng chống treo spinner.
2. [x] **Rà soát PDF text-native và mixed.** Hỗ trợ trích xuất text gốc PDF và nút "Ép OCR ảnh" trên từng trang nếu text gốc lỗi layout.
3. [x] **Thêm cảnh báo chất lượng có thể hành động.** Nút "Nhận dạng lại" trang đơn lẻ; hàm `analyzeQuestionContinuity` cảnh báo thiếu/lặp câu hỏi; cảnh báo trang < 30 ký tự.
4. [x] **Thông báo đúng luồng dữ liệu.** Hiển thị rõ OCR chạy 100% client-side, chỉ gửi text đã trích xuất về API & Gemini; autoSolve mặc định tắt.
5. [x] **Kiểm tra kết quả API.** Xử lý chi tiết phản hồi API (báo lỗi nếu 0 câu, hiển thị phân rã số câu mới/gộp/lỗi/từng phần). Lọc sạch nhãn phân trang `--- TRANG X ---` trong backend `RuleBasedQuestionExtractor`.
6. [x] **Xác minh build và triển khai.** `dotnet test` (76/76 passed), `npx tsc --noEmit` (0 lỗi), `npm run lint` (0 lỗi/cảnh báo), `npm run build` (thành công 13/13 routes).

**Tiêu chí hoàn thành P1:** Đã đạt đầy đủ tiêu chí sẵn sàng phát hành.

### P2 — Đo chất lượng và cải thiện layout

1. Tạo bộ dữ liệu đánh giá có ground truth được giáo viên xác nhận; lưu phiên bản, loại đề và điều kiện chụp, không dùng tập đánh giá để chỉnh tham số.
2. Bổ sung kiểm tra hồi quy cho cleanup/unmount, hủy khi đang tải model/đang OCR/đang render PDF, giới hạn file/trang/pixel, trộn PDF và ảnh, trang lỗi/rỗng, sửa text rồi gửi.
3. Đo CER/WER, coverage câu hỏi, lỗi thứ tự cột, tỷ lệ trang cần sửa, thời gian/trang và mức sử dụng bộ nhớ trên thiết bị cấu hình thấp. Đo riêng chất lượng OCR và chất lượng Gemini extraction.
4. Chỉ triển khai chia cột/crop theo vùng sau khi có mẫu chứng minh cần thiết; giữ khả năng xem ảnh gốc song song với text và duyệt trước xuất bản.
5. Ghi metric không chứa nội dung đề hoặc ảnh: loại đầu vào, số trang, thời gian, trạng thái, lỗi và kết quả tổng hợp. Không log raw OCR text/file content.
6. Chỉ giữ Vision làm phương án dự phòng nếu có giới hạn kích thước/trang, hiển thị rõ việc ảnh được gửi khỏi thiết bị và có sự xác nhận của admin.

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

**Điều kiện mở rộng dùng chính thức:** ngoài tiêu chí pilot trên, P0 và P1 phải hoàn tất; nhóm sản phẩm/giáo viên phải đặt ngưỡng CER/WER, coverage câu hỏi và tỷ lệ cần sửa phù hợp với từng loại đề. Ghi lại kết quả trên tập đánh giá độc lập và có kế hoạch quay về luồng nhập PDF text/đường dự phòng nếu asset OCR hoặc xử lý PDF lỗi diện rộng. Không bật rộng chỉ dựa vào việc lint hoặc build thành công.

## 11. Điều không nên kỳ vọng

OCR trình duyệt giúp giảm tải Render và tránh gửi ảnh lên Gemini Vision, nhưng không mặc định chính xác hơn Vision trên bảng/biểu đồ/layout phức tạp. Nó cũng không tự cấu trúc đề hay đảm bảo đáp án. Kết quả nên xem là bản chép có thể sửa; validator và reviewer vẫn cần thiết.

## 12. Rà soát ảnh đề chụp thực tế

### Phạm vi và giới hạn

Ngày rà soát: 22/10/2026. Đầu vào là ảnh một trang bài Địa lý, mục “Bài 7: Dân số Việt Nam”, nhìn thấy các câu 1–15. Ảnh được gửi trong cuộc trò chuyện; file gốc chưa được lưu trong repository.

Đây là **đánh giá trực quan đầu vào và rà soát mã OCR hiện tại**, chưa phải kết quả chạy Tesseract trên ảnh. Ảnh đính kèm chỉ hiển thị trong cuộc trò chuyện; phiên làm việc này không có file ảnh cục bộ hoặc trình duyệt đang chạy ứng dụng để nạp vào `ExamOcrPanel`. Vì vậy không có OCR text, confidence hay CER/WER nào được đo. Không dùng nhận xét dưới đây để kết luận độ chính xác của Tesseract.

### Quan sát ảnh

- Ảnh chụp gần trọn trang, nhưng mặt giấy nghiêng/perspective; các dòng không hoàn toàn nằm ngang.
- Ánh sáng không đều: góc trên trái và phần dưới tối hơn; nền giấy có texture.
- Chữ in tương đối nhỏ, serif, tiếng Việt có nhiều dấu.
- Có nét khoanh đáp án và tô màu đỏ đè lên một số chữ. Đây là annotation trên đề, không nên biến thành nội dung hoặc được dùng để suy luận đáp án.
- Cách dàn trang là **thân câu hỏi toàn chiều ngang với lựa chọn A/C và B/D đặt cạnh nhau ở nhiều câu**. Đây không phải hai cột văn bản độc lập kéo dài theo trang.
- Trang có các nhãn Câu 1 đến Câu 15 nhìn thấy được; phần nội dung sau lựa chọn của Câu 15 bị sát mép dưới ảnh.

### Đối chiếu với implementation hiện tại

- `image-ocr.ts` gọi Tesseract.js với ngôn ngữ `vie` mặc định; có thể chọn `vie+eng`.
- `prepareCanvasForOcr` hiện chỉ áp dụng `contrast(1.25) brightness(1.05)` khi bật enhance contrast. Nó chưa hiệu chỉnh perspective, khử bóng cục bộ hoặc deskew theo góc nhỏ.
- Chế độ chia hai cột cắt trang tại đúng trung điểm canvas rồi OCR lần lượt nửa trái và nửa phải. Với bố cục ảnh này, cách cắt đó có nguy cơ gom thân câu/đáp án theo vùng thay vì theo từng câu và làm sai thứ tự đọc. Nên để chế độ một vùng cho ca này.
- `analyzeQuestionContinuity` có thể phát hiện số câu thiếu/lặp khi OCR giữ được dạng nhãn `Câu N:`. Đây là cảnh báo cấu trúc hữu ích nhưng không đo độ chính xác chữ, lựa chọn A–D hay quan hệ giữa dấu khoanh và đáp án.
- `confidence` của Tesseract là điểm ở cấp kết quả trang/crop, không đủ làm tiêu chí duyệt tự động. Một trang có điểm cao vẫn có thể sai một chữ số, dấu tiếng Việt hoặc đáp án.

### Kết luận và ưu tiên cải thiện

Đây là ca tốt để đánh giá **ảnh chụp thực tế khó vừa phải**, đặc biệt là thứ tự đọc, khả năng nhận dấu tiếng Việt và nhiễu do nét khoanh. Bản OCR cần được xem là bản nháp để người duyệt so với ảnh. Không nên tự nhận dạng đáp án từ các vòng tròn đỏ/đen; tác vụ OCR chỉ chép phần in và giữ nguyên lựa chọn, còn đáp án chuẩn phải được nhập/xác nhận riêng.

1. Bổ sung bước căn thẳng/hiệu chỉnh perspective bằng bốn góc trang (tự động có nút xem trước; cho phép kéo góc thủ công khi phát hiện sai).
2. Thêm tiền xử lý có thể bật/tắt và so sánh cạnh nhau: cân bằng sáng cục bộ/khử bóng, deskew, grayscale/adaptive threshold. Giữ ảnh gốc để đối chiếu; không ghi đè nguồn.
3. Giữ OCR toàn trang làm mặc định cho dạng trang này. Chỉ chia/crop theo cột khi người dùng xác nhận vùng cột phù hợp; tốt hơn là crop từng câu hoặc từng vùng đọc sau bước layout detection.
4. Hiển thị ảnh gốc và text theo vùng/dòng; đánh dấu vùng confidence thấp và cho OCR lại crop đó, thay vì chỉ báo confidence tổng trang.
5. Cải thiện kiểm tra cấu trúc theo `Câu 1…N` và lựa chọn `A/B/C/D`; cảnh báo thiếu hoặc trùng, nhưng không tự sửa nội dung và không tự chấm đáp án.

### Quy trình chạy lại để lấy số đo thật

1. Lưu ảnh gốc này thành file và nạp vào Admin → OCR trên thiết bị.
2. Chạy một lần với `vie`, chế độ một vùng, enhance contrast bật; lưu text kết quả nguyên trạng và confidence. Không dùng “Ép OCR ảnh” nếu đầu vào là ảnh.
3. Đối chiếu từng dòng với ảnh gốc, ghi riêng lỗi dấu/chữ, số câu, thứ tự dòng, nhãn A–D và phần bị nét khoanh che. Không tính nét khoanh thành đáp án.
4. Chạy lại cùng ảnh sau từng biến đổi (deskew/perspective, cân bằng sáng, threshold) để biết biến đổi nào giúp hoặc làm giảm chất lượng.
5. Lập ground truth do người đọc xác nhận. Báo CER/WER trên phần chữ in, question coverage, tỷ lệ nhãn/lựa chọn đọc đúng, thời gian xử lý và số vùng cần sửa. Không gộp độ chính xác nội dung câu hỏi với độ chính xác đáp án.
6. Lưu ảnh test và ground truth trong bộ dữ liệu nội bộ có quyền sử dụng rõ ràng; không commit đề có bản quyền nếu chưa được phép.

### Tiêu chí chấp nhận đề xuất

- Thứ tự câu và thứ tự lựa chọn được giữ đúng; mọi câu thiếu/lặp đều được cảnh báo.
- Không có đáp án nào được tạo ra từ dấu khoanh hoặc suy đoán OCR.
- Người duyệt có thể đối chiếu ảnh gốc với nội dung đã nhận dạng và chỉnh sửa trước khi gửi ingest.
- Ghi số đo trên nhiều ảnh, không kết luận từ riêng ca này. Ngưỡng CER/WER và coverage cần được thống nhất trước khi dùng để nhập đề hàng loạt.
