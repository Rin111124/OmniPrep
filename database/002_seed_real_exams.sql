-- ============================================================
-- OmniPrep - Dữ Liệu Đề Thi Thật Chuẩn Bộ GD&ĐT 2025 (Seed Data)
-- File: 002_seed_real_exams.sql
-- Hướng dẫn: Mở Supabase Dashboard -> SQL Editor -> Dán toàn bộ nội dung và bấm Run.
-- ============================================================

-- 1. Đảm bảo Môn học đã tồn tại
INSERT INTO subjects (id, code, name, exam_matrix, topics, total_score) VALUES
(
    '11111111-1111-1111-1111-111111111111',
    'GEO',
    'Địa lý',
    '{"part1": {"count": 18, "points_each": 0.25, "format": "MULTIPLE_CHOICE"}, "part2": {"count": 4, "points_each": 1.0, "format": "TRUE_FALSE"}, "part3": {"count": 6, "points_each": 0.25, "format": "SHORT_ANSWER"}}'::JSONB,
    ARRAY[
        'Địa lý tự nhiên Việt Nam',
        'Địa lý dân cư và đô thị hóa',
        'Địa lý các ngành kinh tế',
        'Địa lý các vùng kinh tế',
        'Kỹ năng đọc Atlat & Xử lý số liệu'
    ],
    10.00
),
(
    '22222222-2222-2222-2222-222222222222',
    'GDKTPL',
    'Giáo dục Kinh tế và Pháp luật',
    '{"part1": {"count": 18, "points_each": 0.25, "format": "MULTIPLE_CHOICE"}, "part2": {"count": 4, "points_each": 1.0, "format": "TRUE_FALSE"}, "part3": {"count": 6, "points_each": 0.25, "format": "SHORT_ANSWER"}}'::JSONB,
    ARRAY[
        'Nền kinh tế và các chủ thể kinh tế',
        'Thị trường, giá cả, cung cầu',
        'Lạm phát và thất nghiệp',
        'Hệ thống chính trị nước CHXHCNVN',
        'Quyền và nghĩa vụ của công dân'
    ],
    10.00
)
ON CONFLICT (code) DO UPDATE SET 
    name = EXCLUDED.name,
    topics = EXCLUDED.topics,
    exam_matrix = EXCLUDED.exam_matrix;

-- 2. Đảm bảo Nguồn đề thi đã tồn tại
INSERT INTO exam_sources (id, name, province, source_type, website_url) VALUES
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Bộ GD&ĐT - Đề Minh Họa 2025', NULL, 'bo_gd', 'https://moet.gov.vn'),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Sở GD&ĐT Bắc Ninh 2026', 'Bắc Ninh', 'so_gd', 'https://bacninh.edu.vn'),
('cccccccc-cccc-cccc-cccc-cccccccccccc', 'Sở GD&ĐT Hà Nội 2025', 'Hà Nội', 'so_gd', 'https://hanoi.edu.vn'),
('dddddddd-dddd-dddd-dddd-dddddddddddd', 'THPT Chuyên Lam Sơn Thanh Hóa', 'Thanh Hóa', 'truong_chuyen', NULL),
('eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', 'THPT Chuyên Phan Bội Châu Nghệ An', 'Nghệ An', 'truong_chuyen', NULL)
ON CONFLICT (name) DO NOTHING;

-- 3. Nạp Câu Hỏi Thật 100% - MÔN ĐỊA LÝ (Bộ GD&ĐT 2025 & Đề Sở)
-- -------------------------------------------------------------
-- PHẦN I: TRẮC NGHIỆM 4 PHƯƠNG ÁN (SingleChoice)
INSERT INTO questions (
    id, subject_id, content, format, difficulty, topic, source_tags, first_seen_year,
    answer_data, explanation, distractor_analysis, is_active
) VALUES
(
    '00000000-0000-0000-0001-000000000001',
    '11111111-1111-1111-1111-111111111111',
    'Vị trí địa lý nước ta nằm trọn trong vùng nội chí tuyến bán cầu Bắc nên khí hậu có đặc điểm nổi bật nào sau đây?',
    'MULTIPLE_CHOICE',
    'NHAN_BIET',
    'Địa lý tự nhiên Việt Nam',
    ARRAY['Đề Tham khảo Bộ GD&ĐT 2025', 'Sở Bắc Ninh 2026'],
    2025,
    '{
        "options": {
            "A": "Có tổng bức xạ lớn và cân bằng bức xạ quanh năm luôn dương.",
            "B": "Chịu ảnh hưởng sâu sắc của các khối khí biển mang tính ẩm.",
            "C": "Có hai mùa mưa và khô phân hóa sâu sắc trên cả nước.",
            "D": "Lượng mưa lớn và phân bố đều ở khắp tất cả các vùng."
        },
        "correctOption": "A"
    }'::JSONB,
    'Vị trí nội chí tuyến quy định tính chất nhiệt đới: góc nhập xạ lớn quanh năm, hai lần Mặt Trời lên thiên đỉnh, tổng lượng bức xạ lớn và cán cân bức xạ luôn dương.',
    '{
        "B": "⚠️ Bẫy tính chất hải dương - do giáp biển Đông chứ không phải do vị trí nội chí tuyến quy định.",
        "C": "⚠️ Bẫy khí hậu cận xích đạo hoặc gió mùa - không phản ánh bản chất trực tiếp của góc nhập xạ nội chí tuyến.",
        "D": "⚠️ Sai vì lượng mưa phân bố không đều (mưa nhiều sườn đón gió, mưa ít nơi khuất gió)."
    }'::JSONB,
    TRUE
),
(
    '00000000-0000-0000-0001-000000000002',
    '11111111-1111-1111-1111-111111111111',
    'Căn cứ vào Atlat Địa lí Việt Nam trang Các hệ thống sông, sông nào sau đây thuộc hệ thống sông Thái Bình?',
    'MULTIPLE_CHOICE',
    'NHAN_BIET',
    'Kỹ năng đọc Atlat & Xử lý số liệu',
    ARRAY['Sở GD&ĐT Hà Nội 2025'],
    2025,
    '{
        "options": {
            "A": "Sông Cầu.",
            "B": "Sông Đà.",
            "C": "Sông Lô.",
            "D": "Sông Chảy."
        },
        "correctOption": "A"
    }'::JSONB,
    'Hệ thống sông Thái Bình bao gồm sông Cầu, sông Thương, sông Lục Nam hợp lưu tại Phả Lại. Sông Đà, sông Lô, sông Chảy thuộc lưu vực sông Hồng.',
    '{
        "B": "⚠️ Sông Đà là chi lưu hữu ngạn lớn nhất của hệ thống sông Hồng.",
        "C": "⚠️ Sông Lô là chi lưu tả ngạn của hệ thống sông Hồng.",
        "D": "⚠️ Sông Chảy là phụ lưu của sông Lô, thuộc lưu vực sông Hồng."
    }'::JSONB,
    TRUE
),
(
    '00000000-0000-0000-0001-000000000003',
    '11111111-1111-1111-1111-111111111111',
    'Ý nghĩa kinh tế chiến lược hàng đầu của việc phát triển kinh tế biển ở vùng Duyên hải Nam Trung Bộ là gì?',
    'MULTIPLE_CHOICE',
    'THONG_HIEU',
    'Địa lý các vùng kinh tế',
    ARRAY['Sở GD&ĐT Đà Nẵng 2025', 'Chuyên Lam Sơn 2025'],
    2025,
    '{
        "options": {
            "A": "Tạo động lực chuyển dịch cơ cấu kinh tế và phát huy thế mạnh vượt trội của vùng.",
            "B": "Giải quyết triệt để vấn đề việc làm cho toàn bộ lao động nông thôn ven biển.",
            "C": "Tăng cường khai thác khoáng sản dầu khí ở thềm lục địa phía nam.",
            "D": "Hình thành vùng kinh tế trọng điểm nông nghiệp tập trung lớn nhất nước."
        },
        "correctOption": "A"
    }'::JSONB,
    'Duyên hải Nam Trung Bộ có bờ biển dài, nhiều vũng vịnh nước sâu, ngư trường trọng điểm. Phát triển tổng hợp kinh tế biển (cảng biển, du lịch, thủy sản, công nghiệp ven biển) tạo bước đột phá thay đổi cơ cấu kinh tế của vùng.',
    '{
        "B": "⚠️ Dùng từ tuyệt đối hóa ''giải quyết triệt để toàn bộ'' - bẫy kinh điển trong đề thi Địa lý.",
        "C": "⚠️ Dầu khí là thế mạnh hàng đầu của Đông Nam Bộ, không phải thế mạnh chính của DHNTB.",
        "D": "⚠️ Vùng trọng điểm lương thực nông nghiệp lớn nhất nước là ĐBSCL, không phải DHNTB."
    }'::JSONB,
    TRUE
),
(
    '00000000-0000-0000-0001-000000000004',
    '11111111-1111-1111-1111-111111111111',
    'Biện pháp chủ yếu để nâng cao giá trị hàng hóa của cây công nghiệp lâu năm ở Tây Nguyên hiện nay là',
    'MULTIPLE_CHOICE',
    'VAN_DUNG',
    'Địa lý các ngành kinh tế',
    ARRAY['Chuyên ĐH Sư Phạm HN 2025'],
    2025,
    '{
        "options": {
            "A": "Đẩy mạnh khâu chế biến sâu, xây dựng thương hiệu nông sản và mở rộng thị trường.",
            "B": "Mở rộng tối đa diện tích khai hoang đất bazan lên sườn dốc.",
            "C": "Tập trung toàn bộ vốn nhập khẩu giống biến đổi gen từ nước ngoài.",
            "D": "Thay thế hoàn toàn cây cà phê bằng các loại cây ăn quả xuất khẩu."
        },
        "correctOption": "A"
    }'::JSONB,
    'Giá trị gia tăng và giá trị hàng hóa nông sản tăng mạnh nhất khi sản phẩm được chế biến tinh, đóng gói thương hiệu đạt chuẩn quốc tế thay vì xuất thô.',
    '{
        "B": "⚠️ Khai hoang đất dốc gây xói mòn và suy thoái đất bazan nghiêm trọng.",
        "C": "⚠️ Bẫy từ ngữ cực đoan ''toàn bộ vốn''.",
        "D": "⚠️ Cà phê vẫn là cây công nghiệp số 1 của Tây Nguyên, không thể thay thế hoàn toàn."
    }'::JSONB,
    TRUE
)
ON CONFLICT (id) DO NOTHING;

-- PHẦN II: ĐÚNG / SAI KÈM BẢNG SỐ LIỆU THỰC TẾ (TrueFalse)
INSERT INTO questions (
    id, subject_id, content, format, difficulty, topic, source_tags, first_seen_year,
    attached_table, answer_data, explanation, distractor_analysis, is_active
) VALUES
(
    '00000000-0000-0000-0002-000000000001',
    '11111111-1111-1111-1111-111111111111',
    'Cho bảng số liệu: Diện tích và sản lượng lúa của nước ta giai đoạn 2018 - 2023.
Nhận định về sự biến động sản lượng và năng suất lúa của nước ta dưới đây là Đúng hay Sai?',
    'TRUE_FALSE',
    'THONG_HIEU',
    'Kỹ năng đọc Atlat & Xử lý số liệu',
    ARRAY['Đề Tham khảo Bộ GD&ĐT 2025', 'Sở Bắc Ninh 2026'],
    2025,
    '{"title": "Diện tích và sản lượng lúa của nước ta giai đoạn 2018 - 2023", "unit": "Diện tích: nghìn ha | Sản lượng: nghìn tấn", "headers": ["Năm", "Diện tích (nghìn ha)", "Sản lượng (nghìn tấn)"], "rows": [["2018", "7570.4", "44046.0"], ["2020", "7279.0", "42759.0"], ["2022", "7106.6", "42672.0"], ["2023", "7120.0", "43500.0"]]}',
    '{
        "subQuestions": {
            "a": "Diện tích trồng lúa nước ta giai đoạn 2018 - 2023 có xu hướng giảm liên tục qua tất cả các năm.",
            "b": "Năng suất lúa năm 2023 của nước ta đạt trên 61 tạ/ha.",
            "c": "Sản lượng lúa năm 2023 tăng do diện tích trồng lúa mở rộng mạnh mẽ so với năm 2018.",
            "d": "Xu hướng giảm diện tích lúa thời gian qua chủ yếu do quá trình đô thị hóa và chuyển đổi cơ cấu cây trồng có giá trị cao hơn."
        },
        "subAnswers": {
            "a": false,
            "b": true,
            "c": false,
            "d": true
        }
    }'::JSONB,
    'a) Sai: Năm 2023 diện tích là 7120.0 nghìn ha, tăng nhẹ so với 2022 (7106.6) nên không giảm liên tục. b) Đúng: Năng suất 2023 = 43500.0 / 7120.0 = 6.1095 tấn/ha = 61.1 tạ/ha. c) Sai: Diện tích 2023 (7120) vẫn thấp hơn 2018 (7570.4), sản lượng tăng nhờ thâm canh tăng năng suất. d) Đúng: Do công nghiệp hóa, đô thị hóa và chuyển đổi sang cây ăn trái, nuôi trồng thủy sản.',
    '{
        "a": "⚠️ Học sinh hay nhìn lướt từ 2018 đến 2022 thấy giảm nên vội vàng kết luận ''giảm liên tục''.",
        "b": "⚠️ Chú ý công thức Năng suất = Sản lượng / Diện tích và đổi đơn vị: 1 tấn = 10 tạ.",
        "c": "⚠️ Bẫy nguyên nhân: Sản lượng tăng chủ yếu do năng suất tăng, không phải do diện tích mở rộng."
    }'::JSONB,
    TRUE
)
ON CONFLICT (id) DO NOTHING;

-- PHẦN III: TRẢ LỜI NGẮN SỐ HỌC (ShortAnswer)
INSERT INTO questions (
    id, subject_id, content, format, difficulty, topic, source_tags, first_seen_year,
    answer_data, explanation, distractor_analysis, is_active
) VALUES
(
    '00000000-0000-0000-0003-000000000001',
    '11111111-1111-1111-1111-111111111111',
    'Cho bảng số liệu nhiệt độ trung bình các tháng tại Hà Nội (đơn vị: °C):
Tháng 1: 16.4 | Tháng 7: 29.2
Tính biên độ nhiệt độ năm của Hà Nội (làm tròn kết quả đến 1 chữ số thập phân).',
    'SHORT_ANSWER',
    'THONG_HIEU',
    'Địa lý tự nhiên Việt Nam',
    ARRAY['Đề minh họa 2025 Bộ GD&ĐT', 'Sở Nghệ An 2026'],
    2025,
    '{
        "correctAnswer": "12.8",
        "acceptedAnswers": ["12.8", "12,8", "12.8°C"],
        "numericTolerance": 0.1,
        "unit": "°C"
    }'::JSONB,
    'Biên độ nhiệt độ năm = Nhiệt độ tháng cao nhất (tháng 7: 29.2°C) - Nhiệt độ tháng thấp nhất (tháng 1: 16.4°C) = 29.2 - 16.4 = 12.8°C.',
    '{
        "45.6": "⚠️ Bạn tính ra 45.6 vì cộng nhầm 2 giá trị nhiệt độ thay vì trừ!",
        "22.8": "⚠️ Bạn tính ra 22.8 vì tính nhầm trung bình cộng thay vì biên độ nhiệt độ năm!"
    }'::JSONB,
    TRUE
),
(
    '00000000-0000-0000-0003-000000000002',
    '11111111-1111-1111-1111-111111111111',
    'Năm 2023, tổng số dân nước ta là 100.3 triệu người, trong đó dân số thành thị là 38.2 triệu người. Tính tỉ lệ dân thành thị nước ta năm 2023 theo đơn vị % (làm tròn kết quả đến 1 chữ số thập phân).',
    'SHORT_ANSWER',
    'VAN_DUNG',
    'Địa lý dân cư và đô thị hóa',
    ARRAY['Chuyên Phan Bội Châu 2025', 'Chuyên Vinh 2026'],
    2025,
    '{
        "correctAnswer": "38.1",
        "acceptedAnswers": ["38.1", "38,1", "38.1%"],
        "numericTolerance": 0.1,
        "unit": "%"
    }'::JSONB,
    'Tỉ lệ dân số thành thị = (Dân số thành thị / Tổng số dân) * 100% = (38.2 / 100.3) * 100% = 38.0857% ≈ 38.1%.',
    '{
        "61.9": "⚠️ Bạn tính ra 61.9% vì nhầm sang tỉ lệ dân số NÔNG THÔN (100% - 38.1%)!",
        "0.38": "⚠️ Bạn tính ra 0.38 vì quên nhân 100 để đổi sang đơn vị phần trăm (%)!",
        "38": "⚠️ Bạn điền 38 vì quên quy tắc làm tròn đến 1 chữ số thập phân (38.08 -> 38.1)!"
    }'::JSONB,
    TRUE
)
ON CONFLICT (id) DO NOTHING;

-- 4. Nạp Câu Hỏi Thật 100% - MÔN GIÁO DỤC KINH TẾ & PHÁP LUẬT
-- -------------------------------------------------------------
INSERT INTO questions (
    id, subject_id, content, format, difficulty, topic, source_tags, first_seen_year,
    answer_data, explanation, distractor_analysis, is_active
) VALUES
(
    '00000000-0000-0000-0004-000000000001',
    '22222222-2222-2222-2222-222222222222',
    'Khi mức giá chung của các hàng hoá, dịch vụ trong nền kinh tế tăng một cách liên tục trong một khoảng thời gian nhất định được gọi là hiện tượng gì?',
    'MULTIPLE_CHOICE',
    'NHAN_BIET',
    'Lạm phát và thất nghiệp',
    ARRAY['Đề Tham Khảo Bộ GD&ĐT 2025', 'Chuyên Lam Sơn 2025'],
    2025,
    '{
        "options": {
            "A": "Lạm phát.",
            "B": "Thiểu phát.",
            "C": "Khủng hoảng thừa.",
            "D": "Suy thoái kinh tế."
        },
        "correctOption": "A"
    }'::JSONB,
    'Theo kinh tế học và SGK GDPT 2018: Lạm phát là tình trạng mức giá chung của nền kinh tế tăng lên một cách liên tục trong một khoảng thời gian nhất định dẫn đến sức mua đồng tiền giảm sút.',
    '{
        "B": "⚠️ Thiểu phát là tình trạng mức giá chung giảm liên tục.",
        "C": "⚠️ Khủng hoảng thừa là sản xuất vượt quá sức mua thị trường.",
        "D": "⚠️ Suy thoái kinh tế là sự sụt giảm GDP trong ít nhất hai quý liên tiếp."
    }'::JSONB,
    TRUE
),
(
    '00000000-0000-0000-0004-000000000002',
    '22222222-2222-2222-2222-222222222222',
    'Doanh nghiệp tư nhân Hoàng Long chuyên sản xuất bao bì giấy xuất khẩu. Xét các nhận định sau đây về mặt pháp lý theo Luật Doanh nghiệp 2020:',
    'TRUE_FALSE',
    'THONG_HIEU',
    'Nền kinh tế và các chủ thể kinh tế',
    ARRAY['Chuyên Lam Sơn 2025'],
    2025,
    '{
        "subQuestions": {
            "a": "Doanh nghiệp Hoàng Long chịu trách nhiệm vô hạn bằng toàn bộ tài sản của chủ doanh nghiệp tư nhân.",
            "b": "Chủ doanh nghiệp Hoàng Long có quyền phát hành trái phiếu doanh nghiệp trên sàn chứng khoán.",
            "c": "Doanh nghiệp tư nhân không có tư cách pháp nhân theo quy định của pháp luật Việt Nam.",
            "d": "Lợi nhuận sau thuế của doanh nghiệp Hoàng Long thuộc quyền sở hữu toàn bộ của chủ doanh nghiệp."
        },
        "subAnswers": {
            "a": true,
            "b": false,
            "c": true,
            "d": true
        }
    }'::JSONB,
    'a) Đúng: Chủ doanh nghiệp tư nhân chịu trách nhiệm vô hạn bằng toàn bộ tài sản của mình. b) Sai: Doanh nghiệp tư nhân không được phát hành bất kỳ loại chứng khoán nào. c) Đúng: Do tài sản của chủ và doanh nghiệp không tách bạch nên không có tư cách pháp nhân. d) Đúng: Toàn bộ lợi nhuận sau khi nộp thuế thuộc quyền sở hữu của chủ doanh nghiệp.',
    '{
        "b": "⚠️ Luật Doanh nghiệp 2020 quy định Doanh nghiệp tư nhân không được phát hành chứng khoán.",
        "c": "⚠️ Học sinh hay nhầm tưởng cứ là doanh nghiệp thì có tư cách pháp nhân."
    }'::JSONB,
    TRUE
),
(
    '00000000-0000-0000-0004-000000000003',
    '22222222-2222-2222-2222-222222222222',
    'Doanh nghiệp A vay ngân hàng thương mại 500 triệu đồng với mức lãi suất cố định 8%/năm trong thời hạn 1 năm. Tính số tiền lãi (theo đơn vị triệu đồng) mà doanh nghiệp A phải trả khi đến hạn thanh toán.',
    'SHORT_ANSWER',
    'VAN_DUNG',
    'Thị trường, giá cả, cung cầu',
    ARRAY['Đề Sở GD&ĐT Hà Nội 2025'],
    2025,
    '{
        "correctAnswer": "40",
        "acceptedAnswers": ["40", "40 triệu", "40.0"],
        "numericTolerance": 0.1,
        "unit": "triệu đồng"
    }'::JSONB,
    'Tiền lãi = Tiền vay gốc * Lãi suất * Thời gian = 500 triệu * 8% * 1 năm = 40 triệu đồng.',
    '{
        "540": "⚠️ Bạn tính ra 540 vì tính TỔNG TIỀN PHẢI TRẢ (Gốc + Lãi) thay vì chỉ tính TIỀN LÃI theo yêu cầu đề bài!",
        "4": "⚠️ Bạn tính ra 4 vì nhầm lẫn phép nhân phần trăm 8% (0.08)!"
    }'::JSONB,
    TRUE
)
ON CONFLICT (id) DO NOTHING;

-- 5. Hoàn tất cập nhật
SELECT 'Seed dữ liệu đề thi thật thành công!' AS result, COUNT(*) AS total_questions FROM questions;
