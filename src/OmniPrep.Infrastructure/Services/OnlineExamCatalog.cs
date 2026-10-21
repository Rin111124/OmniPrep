using OmniPrep.Domain.Enums;

namespace OmniPrep.Infrastructure.Services;

public sealed record OnlineExamItem(
    string Id,
    string Title,
    SubjectCode SubjectCode,
    string SourceName,
    short ExamYear,
    string DocumentType,
    int TotalQuestionsEstimated,
    string Description,
    string RemoteUrl,
    string RawSampleContent)
{
    public string GetContent() => RawSampleContent;
}

/// <summary>
/// Danh mục kho đề thi trực tuyến mở chuẩn cấu trúc 2025 (P2 Lộ trình nạp đề)
/// Cung cấp các đề thi từ Bộ GD&ĐT, Sở GD&ĐT Hà Nội, Bắc Ninh, Nam Định...
/// </summary>
public static class OnlineExamCatalog
{
    private static readonly List<OnlineExamItem> Items =
    [
        // ==================== ĐỊA LÝ 2025 ====================
        new OnlineExamItem(
            Id: "bgd-geo-2025",
            Title: "Đề minh họa Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn Địa lí)",
            SubjectCode: SubjectCode.GEO,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            TotalQuestionsEstimated: 28,
            Description: "Đề minh họa chính thức từ Bộ Giáo Dục & Đào Tạo theo cấu trúc GDPT 2018 (18 câu P.I, 4 câu P.II, 6 câu P.III).",
            RemoteUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8932",
            RawSampleContent: @"BỘ GIÁO DỤC VÀ ĐÀO TẠO
KỲ THI TỐT NGHIỆP TRUNG HỌC PHỔ THÔNG NĂM 2025
BÀI THI: ĐỊA LÍ
Thời gian làm bài: 50 phút, không kể thời gian phát đề

PHẦN I. Thí sinh trả lời từ câu 1 đến câu 18. Mỗi câu hỏi thí sinh chỉ chọn một phương án.
Câu 1: Vị trí địa lí của nước ta nằm hoàn toàn trong
A. vùng nội chí tuyến bán cầu Bắc.
B. vành đai sinh khoáng Thái Bình Dương.
C. khu vực xích đạo bán cầu Đông.
D. vùng cận xích đạo gió mùa châu Á.

Câu 2: Do nằm tiếp giáp Biển Đông nên khí hậu nước ta có đặc điểm nào sau đây?
A. Biên độ nhiệt độ trung bình năm rất lớn.
B. Tính chất hải dương sâu sắc, ẩm lớn.
C. Lượng mưa nhỏ, mùa khô kéo dài sâu sắc.
D. Thường xuyên chịu ảnh hưởng gió mùa cực.

Câu 3: Hướng nghiêng chung của địa hình nước ta là
A. bắc - nam.
B. tây bắc - đông nam.
C. tây nam - đông bắc.
D. vòng cung.

Câu 4: Đồng bằng sông Cửu Long bị ngập lụt chủ yếu do
A. triều cường và mưa lớn thượng nguồn.
B. địa hình thấp trũng, triều cường mạnh.
C. bão lớn kết hợp gió mùa tây nam.
D. mạng lưới sông ngòi quá thưa thớt.

Câu 5: Khu vực có nhiệt độ trung bình năm cao nhất nước ta là
A. Duyên hải Nam Trung Bộ.
B. Đồng bằng sông Hồng.
C. Nam Bộ.
D. Tây Bắc.

Câu 6: Đất feralit ở nước ta có màu đỏ vàng chủ yếu do
A. tích tụ hợp chất sắt và nhôm.
B. lớp phủ thực vật rất rậm rạp.
C. lượng mưa lớn rửa trôi chất kiềm.
D. quá trình phong hóa sinh học yếu.

Câu 7: Quá trình feralit diễn ra mạnh mẽ ở khu vực nào sau đây?
A. Vùng đồi núi thấp trên đá mẹ axit.
B. Đồng bằng châu thổ bồi tụ phù sa.
C. Vùng cát ven biển miền Trung.
D. Vùng núi cao trên 2000m.

Câu 8: Cảnh quan tiêu biểu cho thiên nhiên nhiệt đới ẩm gió mùa nước ta là
A. rừng ngập mặn ven biển.
B. rừng nhiệt đới ẩm gió mùa phát triển trên đất feralit.
C. xavan cây bụi và đồng cỏ khô cằn.
D. rừng thưa họ Dầu rụng lá mùa khô.

Câu 9: Điểm giống nhau chủ yếu giữa gió mùa mùa hạ và gió mùa mùa đông ở nước ta là
A. đều có hướng đông bắc.
B. đều có nguồn gốc từ áp cao chí tuyến.
C. gây mưa lớn trên phạm vi cả nước.
D. làm cho khí hậu phân hóa theo mùa sâu sắc.

Câu 10: Ranh giới tự nhiên phân chia miền Bắc và miền Nam nước ta là
A. dãy Bạch Mã.
B. dãy Hoành Sơn.
C. dãy Hoàng Liên Sơn.
D. sông Bến Hải.

Câu 11: Tài nguyên khoáng sản nổi bật ở vùng thềm lục địa nước ta là
A. dầu khí.
B. than đá.
C. bôxit.
D. titan.

Câu 12: Đô thị hóa ở nước ta hiện nay có đặc điểm là
A. diễn ra rất nhanh, tỉ lệ dân số đô thị cao nhất Đông Nam Á.
B. phân bố đều giữa các vùng kinh tế trong cả nước.
C. trình độ đô thị hóa còn thấp so với thế giới.
D. các đô thị đều là trung tâm công nghiệp lớn.

Câu 13: Cơ cấu ngành kinh tế nước ta chuyển dịch theo hướng
A. giảm tỉ trọng nông - lâm - thủy sản, tăng tỉ trọng công nghiệp và dịch vụ.
B. tăng tỉ trọng nông nghiệp, giảm tỉ trọng công nghiệp.
C. giảm mạnh khu vực dịch vụ, tập trung vào chế biến lương thực.
D. ưu tiên duy nhất phát triển công nghiệp khai thác thô.

Câu 14: Thế mạnh hàng đầu để phát triển công nghiệp chế biến lương thực, thực phẩm ở Đồng bằng sông Hồng là
A. nguồn nguyên liệu phong phú từ nông - thủy sản tại chỗ và thị trường tiêu thụ rộng lớn.
B. có nhiều khu công nghệ cao dẫn đầu thế giới.
C. trữ lượng khoáng sản năng lượng lớn nhất nước ta.
D. địa hình cao thoáng, không bị ngập lụt.

Câu 15: Vùng chuyên canh cây công nghiệp lâu năm lớn nhất nước ta là
A. Tây Nguyên.
B. Đông Nam Bộ.
C. Trung du và miền núi Bắc Bộ.
D. Bắc Trung Bộ.

Câu 16: Ý nghĩa chủ yếu của việc phát triển kinh tế biển ở Duyên hải Nam Trung Bộ là
A. khai thác hiệu quả tiềm năng kinh tế và khẳng định chủ quyền biển đảo thiêng liêng.
B. thu hút 100% vốn đầu tư trực tiếp nước ngoài.
C. giải quyết triệt để vấn đề hoang mạc hóa ven biển.
D. chuyển dịch hoàn toàn lao động nông nghiệp sang dịch vụ tàu biển.

Câu 17: Biện pháp quan trọng hàng đầu để nâng cao chất lượng sản phẩm công nghiệp nước ta là
A. đổi mới công nghệ và hiện đại hóa trang thiết bị.
B. mở rộng ồ ạt các xưởng lắp ráp thủ công.
C. giảm chi phí đào tạo nguồn nhân lực trẻ.
D. chỉ tập trung xuất khẩu tài nguyên thô chưa qua chế biến.

Câu 18: Giao thông vận tải đường biển nước ta phát triển nhanh chủ yếu do
A. hội nhập kinh tế quốc tế sâu rộng và mở rộng giao thương hàng hải toàn cầu.
B. phương tiện vận tải đường bộ bị đình trệ.
C. các cảng nước sâu đều được xây dựng từ thời kỳ phong kiến.
D. nhu cầu vận chuyển hành khách nội tỉnh tăng đột biến.

PHẦN II. Thí sinh trả lời từ câu 1 đến câu 4. Trong mỗi ý a), b), c), d) ở mỗi câu, thí sinh chọn đúng hoặc sai.
Câu 1: Cho thông tin về tài nguyên rừng và đa dạng sinh học nước ta:
Nước ta có diện tích rừng đang dần phục hồi, nhưng chất lượng rừng tự nhiên vẫn còn thấp. Rừng đóng vai trò đặc biệt quan trọng trong việc bảo vệ môi trường sinh thái, chống xói mòn đất vùng đồi núi và chắn cát bay ở dải ven biển.
a) Diện tích rừng giàu tự nhiên hiện nay chiếm đa số diện tích rừng cả nước.
b) Trồng rừng phòng hộ ven biển giúp hạn chế nạn cát bay, cát chảy vào làng mạc.
c) Suy giảm đa dạng sinh học làm suy thoái nguồn gen quý hiếm của sinh vật.
d) Rừng ngập mặn có tác dụng bảo vệ đê biển và cân bằng sinh thái vùng cửa sông.

Câu 2: Về vị trí địa lí và lãnh thổ nước ta:
Lãnh thổ Việt Nam gồm vùng đất, vùng biển và vùng trời. Vùng biển có diện tích khoảng 1 triệu km2, tiếp giáp với vùng biển của nhiều quốc gia trong khu vực Biển Đông.
a) Vùng nội thủy được coi như bộ phận lãnh thổ trên đất liền của nước ta.
b) Vùng lãnh hải có chiều rộng 12 hải lý tính từ ranh giới ngoài của vùng đặc quyền kinh tế.
c) Vị trí nằm trong khu vực nội chí tuyến làm cho thiên nhiên nước ta mang tính chất nhiệt đới ẩm gió mùa.
d) Nước ta có chung Biển Đông với tất cả các quốc gia trên thế giới.

Câu 3: Về chuyển dịch cơ cấu ngành kinh tế nước ta:
Cơ cấu kinh tế nước ta đang chuyển dịch tích cực theo hướng công nghiệp hóa, hiện đại hóa nhằm thích ứng với yêu cầu phát triển bền vững và hội nhập quốc tế.
a) Tỉ trọng ngành nông nghiệp giảm phù hợp với xu thế phát triển kinh tế hiện đại.
b) Khu vực có vốn đầu tư nước ngoài có vai trò ngày càng quan trọng trong xuất khẩu.
c) Kinh tế nhà nước giữ vai trò chủ đạo trong nền kinh tế thị trường định hướng xã hội chủ nghĩa.
d) Cơ cấu ngành chuyển dịch đồng nghĩa với việc loại bỏ hoàn toàn ngành nông nghiệp truyền thống.

Câu 4: Về phát triển cây công nghiệp ở Tây Nguyên:
Tây Nguyên là vùng chuyên canh cây công nghiệp lâu năm lớn với các cây trồng chủ lực như cà phê, hồ tiêu, cao su.
a) Đất bazan màu mỡ tầng phong hóa sâu là điều kiện tự nhiên thuận lợi hàng đầu.
b) Khó khăn lớn nhất về tự nhiên đối với sản xuất nông nghiệp ở Tây Nguyên là thiếu nước vào mùa khô.
c) Tất cả các sản phẩm cây công nghiệp Tây Nguyên hiện nay đều được chế biến sâu tại chỗ trước khi xuất khẩu.
d) Phát triển công nghiệp chế biến gắn với vùng nguyên liệu giúp nâng cao giá trị nông sản.

PHẦN III. Thí sinh trả lời từ câu 1 đến câu 6.
Câu 1: Cho biết năm 2023, diện tích rừng trồng nước ta là 4,5 triệu ha, diện tích rừng tự nhiên là 10,2 triệu ha. Tính tỉ lệ (%) diện tích rừng tự nhiên trong tổng diện tích rừng nước ta (làm tròn kết quả đến một chữ số thập phân).
Đáp án: 69.4

Câu 2: Cho biết tổng dân số nước ta năm 2023 là 100,3 triệu người, trong đó dân số thành thị là 38,2 triệu người. Tính tỉ lệ dân số nông thôn nước ta (%) năm 2023 (làm tròn kết quả đến một chữ số thập phân).
Đáp án: 61.9

Câu 3: Cho bảng số liệu nhiệt độ trung bình các tháng tại Hà Nội (đơn vị: °C): Tháng 1: 16.4; Tháng 7: 29.2. Biên độ nhiệt độ trung bình năm của Hà Nội là bao nhiêu °C (làm tròn kết quả đến một chữ số thập phân)?
Đáp án: 12.8

Câu 4: Năm 2023, tổng trị giá xuất khẩu hàng hóa nước ta đạt 355,5 tỉ USD, nhập khẩu đạt 327,5 tỉ USD. Cán cân thương mại hàng hóa nước ta năm 2023 xuất siêu bao nhiêu tỉ USD (làm tròn đến một chữ số thập phân)?
Đáp án: 28.0

Câu 5: Chiều dài đường bờ biển của nước ta (tính từ Móng Cái đến Hà Tiên) là bao nhiêu km (làm tròn đến hàng đơn vị)?
Đáp án: 3260

Câu 6: Vùng biển nước ta có khoảng bao nhiêu hòn đảo lớn nhỏ (làm tròn đến hàng trăm gần đúng)?
Đáp án: 4000"),

        // ==================== TOÁN HỌC 2025 ====================
        new OnlineExamItem(
            Id: "bgd-math-2025",
            Title: "Đề minh họa Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn Toán)",
            SubjectCode: SubjectCode.MATH,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            TotalQuestionsEstimated: 22,
            Description: "Đề minh họa chính thức môn Toán năm 2025 cấu trúc 3 phần (12 câu P.I, 4 câu P.II, 6 câu P.III).",
            RemoteUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8931",
            RawSampleContent: @"BỘ GIÁO DỤC VÀ ĐÀO TẠO
KỲ THI TỐT NGHIỆP TRUNG HỌC PHỔ THÔNG NĂM 2025
BÀI THI: TOÁN HỌC
Thời gian làm bài: 90 phút

PHẦN I. Thí sinh trả lời từ câu 1 đến câu 12. Mỗi câu hỏi thí sinh chỉ chọn một phương án.
Câu 1: Cho hàm số y = f(x) có bảng biến thiên với f'(x) > 0 trên (-∞, 1) và f'(x) < 0 trên (1, +∞). Điểm cực đại của hàm số là
A. x = 1.
B. x = 0.
C. x = 2.
D. x = -1.

Câu 2: Đạo hàm của hàm số y = e^(2x) là
A. y' = 2e^(2x).
B. y' = e^(2x).
C. y' = (1/2)e^(2x).
D. y' = 2xe^(2x-1).

Câu 3: Tập nghiệm của bất phương trình log2(x - 1) < 3 là
A. (1; 9).
B. (-∞; 9).
C. (1; +∞).
D. (0; 9).

Câu 4: Họ tất cả các nguyên hàm của hàm số f(x) = 3x^2 + 2x là
A. x^3 + x^2 + C.
B. 6x + 2 + C.
C. 3x^3 + 2x^2 + C.
D. x^3 + 2x^2 + C.

Câu 5: Trong không gian Oxyz, cho điểm M(2; -1; 3). Hình chiếu vuông góc của M lên mặt phẳng (Oxy) có tọa độ là
A. (2; -1; 0).
B. (2; 0; 3).
C. (0; -1; 3).
D. (0; 0; 3).

Câu 6: Cho hình chóp S.ABC có đáy ABC là tam giác vuông tại B, SA vuông góc với mặt phẳng (ABC). Góc giữa đường thẳng SB và mặt phẳng (ABC) là
A. Góc SBA.
B. Góc SCA.
C. Góc SAB.
D. Góc SCB.

Câu 7: Tiệm cận đứng của đồ thị hàm số y = (2x + 1)/(x - 1) là đường thẳng
A. x = 1.
B. x = 2.
C. y = 2.
D. y = 1.

Câu 8: Cho cấp số cộng (un) có u1 = 3 và công sai d = 4. Giá trị của u2 bằng
A. 7.
B. 12.
C. -1.
D. 1.

Câu 9: Trong không gian Oxyz, vectơ pháp tuyến của mặt phẳng (P): 2x - 3y + z - 5 = 0 là
A. n = (2; -3; 1).
B. n = (2; 3; 1).
C. n = (2; -3; -5).
D. n = (-3; 1; -5).

Câu 10: Thể tích khối lăng trụ có diện tích đáy B và chiều cao h là
A. V = B*h.
B. V = (1/3)*B*h.
C. V = (1/2)*B*h.
D. V = 3*B*h.

Câu 11: Một hộp chứa 5 quả cầu đỏ và 7 quả cầu xanh. Chọn ngẫu nhiên 1 quả cầu. Xác suất để chọn được quả cầu màu đỏ là
A. 5/12.
B. 7/12.
C. 5/7.
D. 1/5.

Câu 12: Nghiệm của phương trình 3^(x - 2) = 9 là
A. x = 4.
B. x = 3.
C. x = 5.
D. x = 2.

PHẦN II. Thí sinh trả lời từ câu 1 đến câu 4. Trong mỗi ý a), b), c), d), thí sinh chọn đúng hoặc sai.
Câu 1: Cho hàm số f(x) = x^3 - 3x^2 + 2.
a) Đạo hàm của hàm số là f'(x) = 3x^2 - 6x.
b) Hàm số đồng biến trên khoảng (0; 2).
c) Điểm cực tiểu của đồ thị hàm số là (2; -2).
d) Giá trị lớn nhất của hàm số trên đoạn [0; 3] bằng 2.

Câu 2: Trong không gian Oxyz, cho điểm A(1; 2; -1) và mặt phẳng (P): x + 2y - 2z + 1 = 0.
a) Điểm A thuộc mặt phẳng (P).
b) Vectơ pháp tuyến của (P) có độ dài bằng 3.
c) Khoảng cách từ điểm A đến mặt phẳng (P) bằng 8/3.
d) Mặt phẳng đi qua A và song song với (P) có phương trình là x + 2y - 2z - 7 = 0.

Câu 3: Một chất điểm chuyển động với phương trình vận tốc v(t) = 3t^2 + 2t (m/s).
a) Gia tốc của chất điểm tại thời điểm t = 2s là a(2) = 14 m/s2.
b) Vận tốc ban đầu của chất điểm là v(0) = 0 m/s.
c) Quãng đường chất điểm đi được trong 3 giây đầu tiên là 36 m.
d) Vận tốc trung bình trong khoảng thời gian từ t = 1s đến t = 3s là 17 m/s.

Câu 4: Một nhóm học sinh gồm 6 nam và 4 nữ. Chọn ngẫu nhiên 3 học sinh.
a) Số cách chọn 3 học sinh tùy ý là 120 cách.
b) Số cách chọn 3 học sinh toàn nam là 20 cách.
c) Xác suất để chọn được ít nhất 1 học sinh nữ là 5/6.
d) Số cách chọn có đúng 2 học sinh nữ là 24 cách.

PHẦN III. Thí sinh trả lời từ câu 1 đến câu 6.
Câu 1: Cho hàm số y = f(x) có đạo hàm f'(x) = x(x - 1)^2(x + 2). Hàm số có bao nhiêu điểm cực trị?
Đáp án: 2

Câu 2: Một người gửi tiết kiệm 100 triệu đồng vào ngân hàng với lãi suất 6%/năm theo hình thức lãi kép. Hỏi sau 3 năm người đó nhận được số tiền lãi là bao nhiêu triệu đồng (làm tròn kết quả đến một chữ số thập phân)?
Đáp án: 19.1

Câu 3: Cho hình chóp tứ giác đều S.ABCD có cạnh đáy bằng 2 và chiều cao bằng 3. Thể tích của khối chóp S.ABCD bằng bao nhiêu?
Đáp án: 4

Câu 4: Cho tích phân từ 0 đến 2 của (2x + 1)dx. Giá trị của tích phân này bằng bao nhiêu?
Đáp án: 6

Câu 5: Trong không gian Oxyz, bán kính mặt cầu (S): x^2 + y^2 + z^2 - 2x + 4y - 6z + 5 = 0 bằng bao nhiêu?
Đáp án: 3

Câu 6: Một lớp có 40 học sinh. Xác suất để chọn được 1 học sinh giỏi là 0.25. Lớp đó có bao nhiêu học sinh giỏi?
Đáp án: 10"),

        // ==================== LỊCH SỬ 2025 ====================
        new OnlineExamItem(
            Id: "bgd-hist-2025",
            Title: "Đề minh họa Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn Lịch sử)",
            SubjectCode: SubjectCode.HIS,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            TotalQuestionsEstimated: 28,
            Description: "Đề minh họa môn Lịch sử chuẩn cấu trúc mới 2025 theo Chương trình GDPT 2018.",
            RemoteUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8933",
            RawSampleContent: @"BỘ GIÁO DỤC VÀ ĐÀO TẠO
KỲ THI TỐT NGHIỆP TRUNG HỌC PHỔ THÔNG NĂM 2025
BÀI THI: LỊCH SỬ
Thời gian làm bài: 50 phút

PHẦN I. Thí sinh trả lời từ câu 1 đến câu 18. Mỗi câu hỏi thí sinh chỉ chọn một phương án.
Câu 1: Hội nghị Ianta (tháng 2-1945) có sự tham gia của nguyên thủ ba cường quốc là
A. Liên Xô, Mỹ, Anh.
B. Liên Xô, Mỹ, Pháp.
C. Mỹ, Anh, Pháp.
D. Liên Xô, Trung Quốc, Mỹ.

Câu 2: Mục tiêu chính của tổ chức Liên Hợp Quốc là
A. duy trì hòa bình và an ninh quốc tế.
B. thiết lập trật tự thế giới đơn cực.
C. ngăn cản sự phát triển của các nước nghèo.
D. thúc đẩy chiến tranh kinh tế giữa các khối.

Câu 3: Sự kiện nào đánh dấu sự sụp đổ hoàn toàn của chủ nghĩa thực dân cũ ở châu Phi?
A. Nhân dân Nam Phi lật đổ chế độ Apácthai (1993).
B. Thắng lợi của nhân dân Môdămbích và Ănggôla (1975).
C. Năm châu Phi (1960) với 17 quốc gia độc lập.
D. Thắng lợi của cách mạng Angiêri (1962).

Câu 4: Trọng tâm của công cuộc Đổi mới ở Việt Nam từ năm 1986 là đổi mới trên lĩnh vực
A. kinh tế.
B. chính trị.
C. văn hóa.
D. quốc phòng.

Câu 5: Chiến dịch Điện Biên Phủ năm 1954 của quân dân Việt Nam đã đập tan kế hoạch quân sự nào của thực dân Pháp?
A. Kế hoạch Nava.
B. Kế hoạch Rơve.
C. Kế hoạch Đờ Lát đơ Tátxinhi.
D. Kế hoạch Bôlae.

PHẦN II. Thí sinh trả lời từ câu 1 đến câu 4. Chọn Đúng hoặc Sai.
Câu 1: Về ý nghĩa lịch sử của Cách mạng tháng Tám năm 1945:
a) Phá tan xiềng xích nô lệ của thực dân Pháp hơn 80 năm và ách thống trị của phát xít Nhật.
b) Đưa Việt Nam bước vào kỷ nguyên độc lập, tự do và đi lên chủ nghĩa xã hội.
c) Là thắng lợi đầu tiên của một dân tộc thuộc địa tự giải phóng trước chủ nghĩa phát xít.
d) Cách mạng tháng Tám chỉ mang tính chất giải phóng dân tộc cục bộ ở vùng đồng bằng.

PHẦN III. Thí sinh trả lời từ câu 1 đến câu 6.
Câu 1: Đảng Cộng sản Việt Nam được thành lập vào năm nào?
Đáp án: 1930

Câu 2: Chiến dịch Hồ Chí Minh lịch sử giải phóng hoàn toàn miền Nam diễn ra vào năm nào?
Đáp án: 1975

Câu 3: Hiệp định Giơ-ne-vơ về chấm dứt chiến tranh, lập lại hòa bình ở Đông Dương được ký kết vào năm nào?
Đáp án: 1954")
    ];

    public static IReadOnlyList<OnlineExamItem> GetCatalog() => Items;

    public static OnlineExamItem? FindById(string id)
        => Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
}
