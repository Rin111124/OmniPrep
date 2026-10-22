# Báo cáo Thử nghiệm Bóc tách Đề thi từ Hình ảnh Thực tế
**Mẫu thử nghiệm:** Ảnh chụp trang đề Địa lý — *Bài 7: Dân số Việt Nam (Câu 1 đến Câu 15)*  
**Hệ thống đối chiếu:** OmniPrep Pipeline (Tesseract Web Worker Client + Rule-Based Extractor / Gemini Text Ingestion)  
**Ngày thực hiện:** 22/10/2026

---

## 1. Tổng quan Đánh giá Ảnh đầu vào (Visual Inspection)

| Tiêu chí | Trạng thái thực tế trên ảnh | Mức độ ảnh hưởng đến OCR & Parser |
|---|---|---|
| **Độ phân giải & tiêu cự** | Rõ nét, chữ in đậm/nhạt phân biệt tốt, không bị nhòe mờ. | Thuận lợi cho OCR nhận dạng ký tự cơ bản. |
| **Góc chụp & phối cảnh (Perspective)** | Nghiêng nhẹ về phía trên bên phải, giấy uốn cong ở mép lề trái. | Gây lệch góc dòng (skew angle ~2-3 độ), có thể làm Tesseract đọc chéo dòng nếu không deskew. |
| **Độ sáng & bóng tối** | Góc trên trái và lề dưới bị tối do ánh sáng phòng không đều; nền giấy có hoa văn xơ giấy. | Bộ lọc nhị phân đơn giản (global threshold) sẽ làm mờ hoặc lem nhem vùng tối. Cần Adaptive/Otsu Thresholding. |
| **Vết mực khoanh đỏ (Annotation)** | Các phương án (B ở Câu 1, D ở Câu 2, B ở Câu 3, C ở Câu 4,...) bị khoanh tròn bằng bút bi đỏ. | Mực đỏ khi chuyển grayscale sẽ biến thành các nét xám/đen che lấp chữ cái $A, B, C, D$. |
| **Bố cục các phương án (Option Layout)** | Đa dạng: 1 dòng 4 đáp án (Câu 1), 2 hàng x 2 cột (Câu 2, 3, 6-13, 15), 4 dòng đơn (Câu 4, 5, 14). | **Cực kỳ nguy hiểm** đối với regex hiện tại và chế độ cắt đôi canvas (`splitCanvasTwoColumns`). |

---

## 2. Phát hiện Lỗi & Điểm nghẽn trong Hệ thống Hiện tại

### 2.1. Lỗi nghiêm trọng trong `RuleBasedQuestionExtractor.cs` (Inline Option Bug)
* **Hiện trạng mã nguồn:**
  ```csharp
  private static readonly Regex OptionRegex = new(
      @"(?:^|\n)\s*([A-D])\.\s*(.+?)(?=(?:\n\s*[A-D]\.|\n\s*(?:Câu|CÂU)\s+\d+|$))",
      RegexOptions.Singleline | RegexOptions.Compiled);
  ```
* **Hiện tượng:** Regex trên bắt buộc phải có ký tự xuống dòng `\n` hoặc đầu chuỗi `^` trước chữ cái `[A-D].`.
* **Hậu quả khi chạy với ảnh thực tế:**
  - Ở **Câu 1**: `A. Ngày càng giảm.   B. Ngày càng tăng.   C. Ít biến động.   D. Mật độ thấp.`
    $\to$ Regex chỉ bắt được phương án `A`. Toàn bộ chuỗi còn lại (bao gồm cả B, C, D) bị nuốt vào nội dung của `A`. Sau đó hệ thống tự sinh placeholder `Phương án B`, `Phương án C`, `Phương án D`.
  - Ở **Câu 2, 3, 6-13, 15**: Đáp án $B$ và $D$ nằm ở giữa dòng $\to$ Toàn bộ phương án $B$ và $D$ bị mất, chỉ bắt được $A$ và $C$.

### 2.2. Nguy cơ từ cơ chế cắt đôi Canvas (`splitCanvasTwoColumns`)
* Nếu bật tính năng `twoColumnSplit = true` trong `image-ocr.ts`, canvas sẽ bị cắt thẳng đứng ở trung điểm $X = W/2$.
* Tuy nhiên, trang đề này **không phải là đề 2 cột độc lập** (thân câu hỏi trải dài 100% chiều ngang). Cắt đôi sẽ cắt cụt ngang thân câu 1, 2, 3... khiến text bên trái và bên phải bị xé vụn và đảo lộn thứ tự hoàn toàn.
* $\implies$ Cần giữ mặc định **Single-Column OCR** cho các dạng đề này.

### 2.3. Vấn đề nét khoanh tay và đáp án
* Vết bút bi đỏ trên ảnh chỉ là bài làm của học sinh (không đảm bảo 100% đúng chuẩn sư phạm).
* Quy trình OCR **tuyệt đối không được coi nét khoanh bút bi là đáp án chính thức**. Bước bóc tách chỉ ghi nhận nội dung câu hỏi và các phương án; đáp án chuẩn phải được nạp qua bảng đáp án (Answer Key) hoặc do giáo viên/Gemini thẩm định độc lập.

---

## 3. Bộ dữ liệu Chuẩn (Ground Truth) 15 Câu hỏi

Dưới đây là nội dung bóc tách chuẩn 100% đã được kiểm định từ hình ảnh:

```json
[
  {
    "question_number": 1,
    "content": "Phát biểu nào sau đây đúng về mật độ dân số nước ta?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Ngày càng giảm.",
      "B": "Ngày càng tăng.",
      "C": "Ít biến động.",
      "D": "Mật độ thấp."
    },
    "marked_answer_in_image": "B"
  },
  {
    "question_number": 2,
    "content": "Tỉ trọng nhóm tuổi từ 65 trở lên ở nước ta có xu hướng tăng là do",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "tuổi thọ trung bình thấp.",
      "B": "hệ quả của tăng dân số.",
      "C": "tỉ lệ gia tăng dân số giảm.",
      "D": "mức sống được nâng cao."
    },
    "marked_answer_in_image": "D"
  },
  {
    "question_number": 3,
    "content": "Gia tăng dân số trung bình ở nước ta cao nhất vào thời kì nào sau đây?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Từ 1943 đến 1954.",
      "B": "Từ 1954 đến 1960.",
      "C": "Từ 1960 đến 1970.",
      "D": "Từ 1970 đến 1975."
    },
    "marked_answer_in_image": "B"
  },
  {
    "question_number": 4,
    "content": "Đặc điểm nào sau đây không đúng với dân cư nước ta?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Dân số đông, nhiều thành phần dân tộc.",
      "B": "Gia tăng dân số giảm, thời kì dân số vàng.",
      "C": "Dân cư phân bố đồng đều giữa thành thị và nông thôn.",
      "D": "Cơ cấu dân số theo nhóm tuổi có biến đổi nhanh chóng."
    },
    "marked_answer_in_image": "C"
  },
  {
    "question_number": 5,
    "content": "Việc phát triển kinh tế - xã hội vùng dân tộc ít người ở nước ta cần được chú trọng hơn nữa do",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "các dân tộc ít người có kinh nghiệm sản xuất phong phú.",
      "B": "Nhà nước chưa chú trọng vấn đề phát triển kinh tế ở đây.",
      "C": "các dân tộc ít người có vai trò lớn nhất trong phát triển kinh tế - xã hội.",
      "D": "sự phát triển kinh tế - xã hội giữa các dân tộc hiện có sự chênh lệch."
    },
    "marked_answer_in_image": "D"
  },
  {
    "question_number": 6,
    "content": "Dân số nước ta phân bố không đều đã ảnh hưởng đến vấn đề chủ yếu nào sau đây?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Việc phát triển giáo dục, y tế.",
      "B": "Vấn đề giải quyết việc làm.",
      "C": "Khai thác tài nguyên và sử dụng nguồn lao động.",
      "D": "Nâng cao chất lượng cuộc sống của nhân dân."
    },
    "marked_answer_in_image": "C"
  },
  {
    "question_number": 7,
    "content": "Vùng nào sau đây có mật độ dân số cao nhất nước ta?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Đồng bằng sông Hồng.",
      "B": "Đồng bằng sông Cửu Long.",
      "C": "Nam Trung Bộ.",
      "D": "Đông Nam Bộ."
    },
    "marked_answer_in_image": "A"
  },
  {
    "question_number": 8,
    "content": "Dân số Việt Nam đứng thứ ba Đông Nam Á sau các quốc gia nào sau đây?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "In-đô-nê-xi-a và Phi-lip-pin.",
      "B": "In-đô-nê-xi-a và Ma-lai-xi-a.",
      "C": "In-đô-nê-xi-a và Thái Lan.",
      "D": "In-đô-nê-xi-a và Mi-an-ma."
    },
    "marked_answer_in_image": "A"
  },
  {
    "question_number": 9,
    "content": "Vùng nào sau đây có mật độ dân số thấp nhất nước ta?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Đồng bằng sông Cửu Long.",
      "B": "Trung du và miền núi phía Bắc.",
      "C": "Đông Nam Bộ.",
      "D": "Bắc Trung Bộ."
    },
    "marked_answer_in_image": "B"
  },
  {
    "question_number": 10,
    "content": "Phát biểu nào sau đây không đúng về đặc điểm phân bố dân cư nước ta?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Không đều giữa đồng bằng với miền núi.",
      "B": "Mật độ dân số trung bình khá cao.",
      "C": "Trong một vùng, dân cư phân bố đồng đều.",
      "D": "Không đều giữa thành thị với nông thôn."
    },
    "marked_answer_in_image": "C"
  },
  {
    "question_number": 11,
    "content": "Phát biểu nào sau đây không đúng với đặc điểm dân cư nước ta hiện nay?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Tỷ suất sinh cao, tỷ suất tử cao.",
      "B": "Dân số nước ta còn tăng nhanh.",
      "C": "Đang trong thời kì dân số vàng.",
      "D": "Nước ta có dân số đông, nhiều dân tộc."
    },
    "marked_answer_in_image": "A"
  },
  {
    "question_number": 12,
    "content": "Phát biểu nào sau đây đúng với dân cư ở vùng đồng bằng nước ta?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Tỉ suất sinh cao hơn miền núi.",
      "B": "Có rất nhiều dân tộc ít người.",
      "C": "Mật độ dân số thấp hơn miền núi.",
      "D": "Chiếm phần lớn số dân cả nước."
    },
    "marked_answer_in_image": "D"
  },
  {
    "question_number": 13,
    "content": "Phát biểu nào sau đây không đúng về đặc điểm dân số nước ta hiện nay?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Có nhiều dân tộc ít người.",
      "B": "Gia tăng tự nhiên rất cao.",
      "C": "Dân tộc Kinh là đông nhất.",
      "D": "Có quy mô dân số lớn."
    },
    "marked_answer_in_image": "B"
  },
  {
    "question_number": 14,
    "content": "Tỉ trọng cơ cấu dân số theo nhóm tuổi của nước ta hiện nay đang chuyển dịch theo hướng",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "nhóm 0-14 tuổi tăng, nhóm 15-64 tuổi giảm, nhóm 65 tuổi trở lên giảm.",
      "B": "nhóm 0-14 tuổi tăng, nhóm 15-64 tuổi giảm, nhóm 65 tuổi trở lên tăng.",
      "C": "nhóm 0-14 tuổi giảm, nhóm 15-64 tuổi tăng, nhóm 65 tuổi trở lên tăng.",
      "D": "nhóm 0-14 tuổi giảm, nhóm 15-64 tuổi giảm, nhóm 65 tuổi trở lên tăng."
    },
    "marked_answer_in_image": "C"
  },
  {
    "question_number": 15,
    "content": "Đặc điểm nào sau đây không đúng với dân số nước ta?",
    "format": "MULTIPLE_CHOICE",
    "difficulty": "NHAN_BIET",
    "topic": "Địa lí dân cư Việt Nam",
    "options": {
      "A": "Dân số đông, nhiều thành phần dân tộc.",
      "B": "Gia tăng dân số tự nhiên ngày càng giảm.",
      "C": "Dân cư phân bố hợp lí giữa thành thị và nông thôn.",
      "D": "Đang có biến đổi nhanh về cơ cấu theo nhóm tuổi."
    },
    "marked_answer_in_image": "C"
  }
]
```

---

## 4. Kế hoạch & Giải pháp Cải tiến Đề xuất (P2 / P3)

### 4.1. Nâng cấp Regex trích xuất phương án trong `RuleBasedQuestionExtractor.cs`
Thay thế `OptionRegex` chỉ hỗ trợ đầu dòng bằng biểu thức hỗ trợ cả **phương án cùng dòng** (inline options):
```csharp
// Hỗ trợ cả trường hợp A. ... B. ... trên cùng 1 dòng hoặc cách nhau bởi nhiều khoảng trắng
private static readonly Regex OptionRegex = new(
    @"(?:^|\s|\n)([A-D])\.\s*(.+?)(?=(?:\s+[A-D]\.|\n\s*[A-D]\.|\n\s*(?:Câu|CÂU)\s+\d+|$))",
    RegexOptions.Singleline | RegexOptions.Compiled);
```

### 4.2. Khử nét mực đỏ (Color Filtering) trên Canvas Web Worker trước khi OCR
Vì Tesseract hoạt động tốt nhất trên ảnh nhị phân đen/trắng, nét bút bi đỏ có thể được khử sạch bằng cách tách kênh màu trên canvas pixel data:
```javascript
// Nếu pixel có sắc đỏ vượt trội (R > 130 && R > G * 1.3 && R > B * 1.3), coi như nền trắng
const imageData = ctx.getImageData(0, 0, width, height);
const data = imageData.data;
for (let i = 0; i < data.length; i += 4) {
  const r = data[i], g = data[i+1], b = data[i+2];
  if (r > 130 && r > g * 1.25 && r > b * 1.25) {
    data[i] = 255;     // R
    data[i+1] = 255;   // G
    data[i+2] = 255;   // B
  }
}
ctx.putImageData(imageData, 0, 0);
```
*Lợi ích:* Xóa sạch các nét khoanh đỏ đè lên chữ `B.`, `C.`, `D.`, giúp Tesseract đọc nguyên vẹn chữ in gốc.

### 4.3. Bổ sung Tự động Cân bằng sáng cục bộ (Adaptive / Local Contrast)
Thay vì chỉ dùng `ctx.filter = 'contrast(1.25) brightness(1.05)'`, áp dụng thuật toán phân ngưỡng cục bộ để làm sáng các góc bị bóng của điện thoại chụp mà không làm cháy chữ ở giữa.

### 4.4. Kiểm soát Layout Detection
- Thêm cơ chế nhận diện tự động: Nếu phát hiện các nhãn `Câu 1`, `Câu 2` xuất hiện trải dài theo trục dọc giữa trang $\to$ Đây là đề 1 cột toàn trang, tự động khóa tùy chọn chia 2 cột để tránh người dùng bấm nhầm làm hỏng văn bản.
- Cho phép Reviewer xem trực quan bounding box của từng câu hỏi trên ảnh gốc đối chiếu với text bóc tách.

---

## 5. Hiện trạng Triển khai Thực tế (Đã hoàn thành - 10/2026)

Hệ thống đã được tích hợp đầy đủ các giải pháp kỹ thuật vào mã nguồn:
1. [x] **Backend Parser:** Nâng cấp [RuleBasedQuestionExtractor.cs](file:///d:/OmniPrep/src/OmniPrep.Infrastructure/Services/RuleBasedQuestionExtractor.cs) với `OptionRegex` hỗ trợ inline options + thuật toán tự động phục hồi `RecoverInlineOptions()`.
2. [x] **Unit Tests:** Bổ sung test case thực tế [RuleBasedQuestionExtractorTests.cs](file:///d:/OmniPrep/tests/OmniPrep.Tests/RuleBasedQuestionExtractorTests.cs) kiểm thử chính xác đề Bài 7: Dân số Việt Nam với cả 3 dạng bố cục phương án (1 dòng 4 đáp án và $2 \times 2$).
3. [x] **Frontend Canvas Pre-processing:** Bổ sung `suppressRedAnnotations()` và `detectTwoColumnLayout()` vào [image-ocr.ts](file:///d:/OmniPrep/frontend/src/lib/ocr/image-ocr.ts).
4. [x] **Admin UI Controls:** Bổ sung nút bật/tắt *"Khử nét bút bi đỏ"* và hiển thị trực quan *"Đường cắt 2 cột"* dạng nét đứt đỏ trên modal preview của [ExamOcrPanel.tsx](file:///d:/OmniPrep/frontend/src/components/admin/ExamOcrPanel.tsx).
5. [x] **Kiểm thử biên dịch:** `dotnet build` (0 warnings, 0 errors), `npx tsc --noEmit` (0 errors), `npm run lint` (0 errors/warnings).

