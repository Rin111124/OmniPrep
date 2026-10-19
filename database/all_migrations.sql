--- ============================================================
--- MIGRATION: 001_init_schema.sql
--- ============================================================

-- ============================================================
-- OmniPrep - Nền tảng Ôn luyện THPT Thông minh
-- Database Schema v1.0 - Supabase PostgreSQL + pgvector
-- ============================================================
-- Hướng dẫn chạy: Mở SQL Editor trong Supabase Dashboard
-- và thực thi từng block theo thứ tự.
-- ============================================================

-- ============================================================
-- BƯỚC 1: Kích hoạt Extension pgvector (vector similarity search)
-- ============================================================
CREATE EXTENSION IF NOT EXISTS "pgvector";
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================================
-- BƯỚC 2: ENUM TYPES - Các kiểu dữ liệu liệt kê chuẩn
-- ============================================================

-- Danh sách môn học hỗ trợ
CREATE TYPE subject_code AS ENUM (
    'GEO',    -- Địa lý
    'GDKTPL', -- Giáo dục Kinh tế & Pháp luật
    'MATH',   -- Toán
    'PHYS',   -- Vật lý
    'CHEM',   -- Hóa học
    'BIO',    -- Sinh học
    'LIT',    -- Ngữ văn
    'ENG',    -- Tiếng Anh
    'HIS',    -- Lịch sử
    'CIVICS'  -- Giáo dục công dân
);

-- Định dạng câu hỏi theo chuẩn Bộ GD&ĐT 2025
CREATE TYPE question_format AS ENUM (
    'MULTIPLE_CHOICE',   -- Phần I: Trắc nghiệm 4 lựa chọn (0.25đ/câu)
    'TRUE_FALSE',        -- Phần II: Đúng/Sai 4 ý lũy tiến
    'SHORT_ANSWER'       -- Phần III: Trả lời ngắn có dung sai
);

-- Mức độ nhận thức (Bloom's Taxonomy)
CREATE TYPE difficulty_level AS ENUM (
    'NHAN_BIET',    -- Nhận biết
    'THONG_HIEU',   -- Thông hiểu
    'VAN_DUNG',     -- Vận dụng
    'VAN_DUNG_CAO'  -- Vận dụng cao
);

-- Trạng thái xử lý đề thi
CREATE TYPE ingestion_status AS ENUM (
    'PENDING',     -- Đang chờ xử lý
    'PROCESSING',  -- Đang bóc tách
    'COMPLETED',   -- Hoàn thành
    'FAILED',      -- Thất bại
    'DUPLICATE'    -- Đề bị trùng lặp
);

-- Trạng thái câu trả lời
CREATE TYPE answer_status AS ENUM (
    'CORRECT',    -- Đúng
    'WRONG',      -- Sai
    'SKIPPED'     -- Bỏ qua
);

-- ============================================================
-- BƯỚC 3: BẢNG PROFILES - Thông tin người dùng (extends Supabase Auth)
-- ============================================================
CREATE TABLE IF NOT EXISTS profiles (
    id          UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    full_name   TEXT NOT NULL DEFAULT '',
    grade       SMALLINT CHECK (grade IN (10, 11, 12)),
    school_name TEXT,
    -- Mục tiêu điểm thi theo từng môn (JSONB linh hoạt)
    -- Ví dụ: {"GEO": 8.5, "GDKTPL": 9.0}
    score_goals JSONB DEFAULT '{}'::JSONB,
    avatar_url  TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
COMMENT ON TABLE profiles IS 'Hồ sơ học sinh, liên kết với Supabase Auth';

-- ============================================================
-- BƯỚC 4: BẢNG EXAM_SOURCES - Nguồn đề thi
-- ============================================================
CREATE TABLE IF NOT EXISTS exam_sources (
    id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    -- Tên Sở GD&ĐT hoặc trường: "Sở GD&ĐT Hà Nội", "THPT Chuyên Lê Hồng Phong"
    name          TEXT NOT NULL UNIQUE,
    province      TEXT,              -- Tỉnh/Thành phố
    source_type   TEXT NOT NULL,     -- 'so_gd', 'truong_chuyen', 'bo_gd', 'luyen_de'
    website_url   TEXT,              -- URL để cào đề tự động
    is_active     BOOLEAN DEFAULT TRUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
COMMENT ON TABLE exam_sources IS 'Danh sách nguồn đề thi (Sở GD&ĐT, trường chuyên...)';

-- ============================================================
-- BƯỚC 5: BẢNG SUBJECTS - Cấu hình môn học và ma trận đề
-- ============================================================
CREATE TABLE IF NOT EXISTS subjects (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code            subject_code UNIQUE NOT NULL,
    name            TEXT NOT NULL,                       -- "Địa lý", "Giáo dục KT&PL"
    -- Ma trận đề thi theo chuẩn Bộ GD&ĐT
    -- Ví dụ: {"part1": {"count": 18, "points_each": 0.25},
    --         "part2": {"count": 4, "points_each": 1.0},
    --         "part3": {"count": 6, "points_each": 0.5}}
    exam_matrix     JSONB NOT NULL DEFAULT '{}'::JSONB,
    -- Danh sách chuyên đề (topics) theo từng môn
    -- Ví dụ: ["Địa lý tự nhiên VN", "Địa lý dân cư", "Địa lý kinh tế", ...]
    topics          TEXT[] NOT NULL DEFAULT '{}',
    total_score     NUMERIC(4,2) NOT NULL DEFAULT 10.00,
    is_active       BOOLEAN DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
COMMENT ON TABLE subjects IS 'Cấu hình môn học và ma trận đề thi chuẩn Bộ GD&ĐT';

-- ============================================================
-- BƯỚC 6: BẢNG QUESTIONS - Ngân hàng câu hỏi (CORE TABLE)
-- ============================================================
CREATE TABLE IF NOT EXISTS questions (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    subject_id          UUID NOT NULL REFERENCES subjects(id),

    -- Nội dung câu hỏi (Markdown, hỗ trợ LaTeX)
    content             TEXT NOT NULL,
    -- Bảng số liệu đính kèm (nếu có) - lưu dạng Markdown table
    attached_table      TEXT,
    -- Hình ảnh đính kèm (URL Supabase Storage)
    image_urls          TEXT[] DEFAULT '{}',

    -- Loại câu hỏi và độ khó
    format              question_format NOT NULL,
    difficulty          difficulty_level NOT NULL,
    topic               TEXT NOT NULL,  -- Chuyên đề (phải thuộc subjects.topics[])

    -- Dữ liệu đáp án linh hoạt theo format (JSONB)
    -- MULTIPLE_CHOICE: {"options": {"A":"...","B":"...","C":"...","D":"..."}, "correct": "A"}
    -- TRUE_FALSE: {"statements": [{"id":"a","text":"...","is_correct":true}, ...]}
    -- SHORT_ANSWER: {"value": 1234.56, "tolerance": 0.01, "unit": "km²",
    --                "wrong_traps": [1234, 1235.0, 0]}
    answer_data         JSONB NOT NULL,

    -- Lời giải chi tiết (Markdown)
    explanation         TEXT,
    -- Phân tích distractor (tại sao học sinh hay chọn sai)
    distractor_analysis JSONB DEFAULT '{}'::JSONB,

    -- CHỐNG TRÙNG LẶP: Tầng 1 - SHA-256 Hash
    content_hash        CHAR(64) NOT NULL,
    -- CHỐNG TRÙNG LẶP: Tầng 2 - Vector 768 chiều (text-embedding-004)
    content_embedding   VECTOR(768),

    -- Metadata nguồn gốc
    -- Ví dụ: ["Sở GD&ĐT Hà Nội 2024", "Sở GD&ĐT TP.HCM 2023"]
    source_tags         TEXT[] DEFAULT '{}',
    -- Năm xuất hiện lần đầu
    first_seen_year     SMALLINT,
    -- Số lần xuất hiện trong các đề thi khác nhau (cập nhật khi merge)
    occurrence_count    INT NOT NULL DEFAULT 1,

    -- Metadata quản lý
    is_verified         BOOLEAN DEFAULT FALSE,  -- Đã kiểm duyệt thủ công
    is_active           BOOLEAN DEFAULT TRUE,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE questions IS 'Ngân hàng câu hỏi trung tâm với chống trùng lặp 2 tầng (SHA-256 + pgvector)';
COMMENT ON COLUMN questions.content_hash IS 'SHA-256 của content để phát hiện trùng lặp chính xác (Tầng 1)';
COMMENT ON COLUMN questions.content_embedding IS 'Vector 768 chiều từ text-embedding-004 để phát hiện câu hỏi xào nấu (Tầng 2)';

-- ============================================================
-- BƯỚC 7: BẢNG EXAM_PAPERS - Đề thi gốc được nạp vào hệ thống
-- ============================================================
CREATE TABLE IF NOT EXISTS exam_papers (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    source_id       UUID NOT NULL REFERENCES exam_sources(id),
    subject_id      UUID NOT NULL REFERENCES subjects(id),
    title           TEXT NOT NULL,
    exam_year       SMALLINT,
    exam_type       TEXT,  -- 'thu_tot_nghiep', 'ky_thi_thuong_xuyen', 'tot_nghiep_chinh_thuc'
    -- URL file gốc trong Supabase Storage
    file_url        TEXT,
    file_hash       CHAR(64),  -- SHA-256 của file để tránh nạp lại cùng 1 file
    raw_content     TEXT,
    -- Kết quả bóc tách
    status          ingestion_status NOT NULL DEFAULT 'PENDING',
    total_extracted INT DEFAULT 0,     -- Số câu bóc tách được
    total_new       INT DEFAULT 0,     -- Số câu thực sự mới
    total_merged    INT DEFAULT 0,     -- Số câu bị gộp (trùng)
    error_message   TEXT,
    -- Timestamps
    submitted_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at    TIMESTAMPTZ
);
COMMENT ON TABLE exam_papers IS 'Log đề thi gốc được nạp vào hệ thống bởi Worker Service';

-- ============================================================
-- BƯỚC 8: BẢNG GENERATED_EXAMS - Đề thi được trộn cho học sinh
-- ============================================================
CREATE TABLE IF NOT EXISTS generated_exams (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         UUID NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    subject_id      UUID NOT NULL REFERENCES subjects(id),
    -- Danh sách ID câu hỏi theo thứ tự (lưu thứ tự trộn)
    question_ids    UUID[] NOT NULL,
    -- Snapshot ma trận đề tại thời điểm tạo
    matrix_snapshot JSONB NOT NULL,
    -- Thời gian làm bài (phút)
    duration_minutes SMALLINT DEFAULT 50,
    -- Thời hạn nộp bài
    expires_at      TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
COMMENT ON TABLE generated_exams IS 'Đề thi được trộn ngẫu nhiên theo ma trận, loại câu đã làm đúng';

-- ============================================================
-- BƯỚC 9: BẢNG EXAM_ATTEMPTS - Lần thi của học sinh
-- ============================================================
CREATE TABLE IF NOT EXISTS exam_attempts (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id             UUID NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    exam_id             UUID NOT NULL REFERENCES generated_exams(id),
    subject_id          UUID NOT NULL REFERENCES subjects(id),

    -- Điểm số chi tiết theo từng phần
    score_part1         NUMERIC(4,2) DEFAULT 0,  -- Trắc nghiệm
    score_part2         NUMERIC(4,2) DEFAULT 0,  -- Đúng/Sai
    score_part3         NUMERIC(4,2) DEFAULT 0,  -- Trả lời ngắn
    total_score         NUMERIC(4,2) DEFAULT 0,

    -- Thời gian thực tế làm bài (giây)
    time_spent_seconds  INT,
    -- Đã nộp bài chưa
    is_submitted        BOOLEAN DEFAULT FALSE,
    submitted_at        TIMESTAMPTZ,
    started_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
COMMENT ON TABLE exam_attempts IS 'Lưu vết toàn bộ các lần thi của học sinh';

-- ============================================================
-- BƯỚC 10: BẢNG USER_QUESTION_HISTORY - Chi tiết từng câu học sinh làm
-- ============================================================
CREATE TABLE IF NOT EXISTS user_question_history (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         UUID NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    attempt_id      UUID NOT NULL REFERENCES exam_attempts(id) ON DELETE CASCADE,
    question_id     UUID NOT NULL REFERENCES questions(id),

    -- Câu trả lời của học sinh (JSONB linh hoạt theo format)
    -- MULTIPLE_CHOICE: {"selected": "B"}
    -- TRUE_FALSE: {"answers": {"a": true, "b": false, "c": true, "d": true}}
    -- SHORT_ANSWER: {"value": "1234.5"}
    user_answer     JSONB,

    status          answer_status NOT NULL DEFAULT 'SKIPPED',
    -- Điểm đạt được cho câu này
    points_earned   NUMERIC(4,2) DEFAULT 0,
    -- Thời gian trả lời (giây)
    time_spent_seconds INT,
    -- Học sinh đã xem lời giải từ AI chưa?
    ai_explanation_viewed BOOLEAN DEFAULT FALSE,

    answered_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
COMMENT ON TABLE user_question_history IS 'Chi tiết từng câu hỏi học sinh đã làm - dữ liệu huấn luyện cho AI Tutor';

-- ============================================================
-- BƯỚC 11: BẢNG USER_TOPIC_MASTERY - Heatmap năng lực theo chuyên đề
-- ============================================================
CREATE TABLE IF NOT EXISTS user_topic_mastery (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         UUID NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    subject_id      UUID NOT NULL REFERENCES subjects(id),
    topic           TEXT NOT NULL,  -- Chuyên đề cụ thể

    -- Thống kê tích lũy
    total_attempts  INT NOT NULL DEFAULT 0,
    total_correct   INT NOT NULL DEFAULT 0,
    -- Tỷ lệ làm đúng (0.0 - 1.0) - dùng cho Heatmap
    mastery_rate    NUMERIC(5,4) NOT NULL DEFAULT 0.0,
    -- Điểm mastery cuối cùng (ELO-like, dùng cho adaptive learning)
    mastery_score   NUMERIC(6,2) DEFAULT 0.0,

    last_practiced  TIMESTAMPTZ,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (user_id, subject_id, topic)
);
COMMENT ON TABLE user_topic_mastery IS 'Dữ liệu Heatmap năng lực - cập nhật sau mỗi lần thi';

-- ============================================================
-- BƯỚC 12: BẢNG AI_TUTOR_SESSIONS - Lịch sử hỏi đáp AI Tutor
-- ============================================================
CREATE TABLE IF NOT EXISTS ai_tutor_sessions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id         UUID NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    question_id     UUID NOT NULL REFERENCES questions(id),
    attempt_id      UUID REFERENCES exam_attempts(id),

    -- Context gửi lên Gemini (để audit và cải thiện prompt)
    context_sent    JSONB NOT NULL DEFAULT '{}'::JSONB,
    -- Câu trả lời từ AI (Markdown)
    ai_response     TEXT,
    -- Số token sử dụng (để theo dõi quota)
    tokens_used     INT,

    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
COMMENT ON TABLE ai_tutor_sessions IS 'Lịch sử tương tác với AI Tutor - dùng để audit và tối ưu prompt';

-- ============================================================
-- BƯỚC 13: INDEX - Tối ưu hiệu năng truy vấn
-- ============================================================

-- Index chính cho bảng questions
CREATE UNIQUE INDEX IF NOT EXISTS idx_questions_content_hash
    ON questions(content_hash);
COMMENT ON INDEX idx_questions_content_hash IS 'Tầng 1 chống trùng: Exact match qua SHA-256';

-- HNSW Index cho vector search (nhanh hơn IVFFlat, không cần train trước)
-- ef_construction=128: trade-off giữa build time và recall quality
CREATE INDEX IF NOT EXISTS idx_questions_embedding_hnsw
    ON questions USING hnsw (content_embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 128);
COMMENT ON INDEX idx_questions_embedding_hnsw IS 'Tầng 2 chống trùng: Cosine similarity search qua HNSW index';

-- Index cho truy vấn trộn đề theo môn/chuyên đề/độ khó
CREATE INDEX IF NOT EXISTS idx_questions_subject_topic
    ON questions(subject_id, topic, difficulty, format)
    WHERE is_active = TRUE;

CREATE INDEX IF NOT EXISTS idx_questions_subject_format
    ON questions(subject_id, format)
    WHERE is_active = TRUE;

-- Index cho user_question_history - tránh N+1 khi loại câu đã làm đúng
CREATE INDEX IF NOT EXISTS idx_uqh_user_question
    ON user_question_history(user_id, question_id, status);

CREATE INDEX IF NOT EXISTS idx_uqh_user_attempt
    ON user_question_history(user_id, attempt_id);

-- Index cho user_topic_mastery - Heatmap query
CREATE INDEX IF NOT EXISTS idx_utm_user_subject
    ON user_topic_mastery(user_id, subject_id);

-- Index cho exam_attempts
CREATE INDEX IF NOT EXISTS idx_attempts_user_subject
    ON exam_attempts(user_id, subject_id, started_at DESC);

-- ============================================================
-- BƯỚC 14: ROW LEVEL SECURITY (RLS) - Bảo mật dữ liệu
-- ============================================================

-- Bật RLS cho các bảng chứa dữ liệu người dùng
ALTER TABLE profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE generated_exams ENABLE ROW LEVEL SECURITY;
ALTER TABLE exam_attempts ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_question_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_topic_mastery ENABLE ROW LEVEL SECURITY;
ALTER TABLE ai_tutor_sessions ENABLE ROW LEVEL SECURITY;

-- Chính sách: Người dùng chỉ đọc/ghi dữ liệu của mình
CREATE POLICY "profiles_self" ON profiles
    FOR ALL USING (auth.uid() = id);

CREATE POLICY "generated_exams_self" ON generated_exams
    FOR ALL USING (auth.uid() = user_id);

CREATE POLICY "exam_attempts_self" ON exam_attempts
    FOR ALL USING (auth.uid() = user_id);

CREATE POLICY "user_question_history_self" ON user_question_history
    FOR ALL USING (auth.uid() = user_id);

CREATE POLICY "user_topic_mastery_self" ON user_topic_mastery
    FOR ALL USING (auth.uid() = user_id);

CREATE POLICY "ai_tutor_sessions_self" ON ai_tutor_sessions
    FOR ALL USING (auth.uid() = user_id);

-- Questions, subjects, exam_sources: Public read (không cần auth)
ALTER TABLE questions ENABLE ROW LEVEL SECURITY;
ALTER TABLE subjects ENABLE ROW LEVEL SECURITY;
ALTER TABLE exam_sources ENABLE ROW LEVEL SECURITY;

CREATE POLICY "questions_public_read" ON questions
    FOR SELECT USING (is_active = TRUE);

CREATE POLICY "subjects_public_read" ON subjects
    FOR SELECT USING (is_active = TRUE);

CREATE POLICY "exam_sources_public_read" ON exam_sources
    FOR SELECT USING (is_active = TRUE);

-- ============================================================
-- BƯỚC 15: FUNCTIONS & TRIGGERS - Tự động cập nhật dữ liệu
-- ============================================================

-- Trigger tự động cập nhật updated_at
CREATE OR REPLACE FUNCTION trigger_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER set_updated_at_profiles
    BEFORE UPDATE ON profiles
    FOR EACH ROW EXECUTE FUNCTION trigger_set_updated_at();

CREATE TRIGGER set_updated_at_questions
    BEFORE UPDATE ON questions
    FOR EACH ROW EXECUTE FUNCTION trigger_set_updated_at();

CREATE TRIGGER set_updated_at_user_topic_mastery
    BEFORE UPDATE ON user_topic_mastery
    FOR EACH ROW EXECUTE FUNCTION trigger_set_updated_at();

-- ============================================================
-- Function: Tìm câu hỏi tương đồng (Semantic Deduplication)
-- ============================================================
CREATE OR REPLACE FUNCTION find_similar_questions(
    query_embedding VECTOR(768),
    target_subject_id UUID,
    similarity_threshold FLOAT DEFAULT 0.08,
    result_limit INT DEFAULT 5
)
RETURNS TABLE (
    question_id UUID,
    cosine_distance FLOAT,
    similarity_percent NUMERIC
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        q.id AS question_id,
        (q.content_embedding <=> query_embedding)::FLOAT AS cosine_distance,
        ROUND(((1 - (q.content_embedding <=> query_embedding)) * 100)::NUMERIC, 2) AS similarity_percent
    FROM questions q
    WHERE q.subject_id = target_subject_id
      AND q.is_active = TRUE
      AND q.content_embedding IS NOT NULL
      AND (q.content_embedding <=> query_embedding) < similarity_threshold
    ORDER BY q.content_embedding <=> query_embedding
    LIMIT result_limit;
END;
$$ LANGUAGE plpgsql STABLE;

-- ============================================================
-- Function: Cập nhật mastery sau khi học sinh nộp bài
-- ============================================================
CREATE OR REPLACE FUNCTION update_user_topic_mastery(
    p_user_id UUID,
    p_subject_id UUID,
    p_topic TEXT,
    p_is_correct BOOLEAN
)
RETURNS VOID AS $$
BEGIN
    INSERT INTO user_topic_mastery (user_id, subject_id, topic, total_attempts, total_correct, mastery_rate, last_practiced)
    VALUES (p_user_id, p_subject_id, p_topic, 1, CASE WHEN p_is_correct THEN 1 ELSE 0 END,
            CASE WHEN p_is_correct THEN 1.0 ELSE 0.0 END, NOW())
    ON CONFLICT (user_id, subject_id, topic) DO UPDATE
        SET total_attempts  = user_topic_mastery.total_attempts + 1,
            total_correct   = user_topic_mastery.total_correct + CASE WHEN p_is_correct THEN 1 ELSE 0 END,
            mastery_rate    = ROUND(
                                ((user_topic_mastery.total_correct + CASE WHEN p_is_correct THEN 1 ELSE 0 END)::NUMERIC
                                / (user_topic_mastery.total_attempts + 1)::NUMERIC),
                              4),
            last_practiced  = NOW(),
            updated_at      = NOW();
END;
$$ LANGUAGE plpgsql;

-- ============================================================
-- Function: Trộn đề thông minh (loại câu đã làm đúng)
-- ============================================================
CREATE OR REPLACE FUNCTION draw_exam_questions(
    p_subject_id UUID,
    p_user_id UUID,
    p_format question_format,
    p_count INT,
    p_topic_weights JSONB DEFAULT NULL
)
RETURNS TABLE (question_id UUID, topic TEXT, difficulty difficulty_level) AS $$
BEGIN
    RETURN QUERY
    SELECT
        q.id AS question_id,
        q.topic,
        q.difficulty
    FROM questions q
    WHERE q.subject_id = p_subject_id
      AND q.format = p_format
      AND q.is_active = TRUE
      AND q.id NOT IN (
          SELECT DISTINCT uqh.question_id
          FROM user_question_history uqh
          WHERE uqh.user_id = p_user_id
            AND uqh.status = 'CORRECT'
      )
    ORDER BY RANDOM()
    LIMIT p_count;
END;
$$ LANGUAGE plpgsql VOLATILE;

-- ============================================================
-- BƯỚC 16: DỮ LIỆU MẪU - Seed Data
-- ============================================================

INSERT INTO subjects (code, name, exam_matrix, topics, total_score) VALUES
(
    'GEO',
    'Địa lý',
    '{"part1": {"count": 18, "points_each": 0.25, "format": "MULTIPLE_CHOICE"}, "part2": {"count": 4, "points_each": 1.0, "format": "TRUE_FALSE"}, "part3": {"count": 6, "points_each": 0.5, "format": "SHORT_ANSWER"}}'::JSONB,
    ARRAY[
        'Địa lý tự nhiên Việt Nam',
        'Địa lý dân cư Việt Nam',
        'Địa lý kinh tế Việt Nam',
        'Địa lý các vùng kinh tế',
        'Địa lý biển và hải đảo',
        'Địa lý tự nhiên thế giới',
        'Địa lý kinh tế thế giới',
        'Atlat Địa lý',
        'Kỹ năng đọc biểu đồ',
        'Kỹ năng nhận xét bảng số liệu'
    ],
    10.00
),
(
    'GDKTPL',
    'Giáo dục Kinh tế và Pháp luật',
    '{"part1": {"count": 18, "points_each": 0.25, "format": "MULTIPLE_CHOICE"}, "part2": {"count": 4, "points_each": 1.0, "format": "TRUE_FALSE"}, "part3": {"count": 6, "points_each": 0.5, "format": "SHORT_ANSWER"}}'::JSONB,
    ARRAY[
        'Kinh tế vi mô cơ bản',
        'Kinh tế vĩ mô cơ bản',
        'Tài chính - Ngân hàng',
        'Pháp luật Hình sự',
        'Pháp luật Dân sự',
        'Pháp luật Hành chính',
        'Hiến pháp và quyền công dân',
        'Pháp luật về lao động',
        'Đạo đức kinh doanh',
        'Tình huống pháp luật thực tiễn'
    ],
    10.00
)
ON CONFLICT (code) DO NOTHING;

INSERT INTO exam_sources (name, province, source_type, website_url) VALUES
-- 1. BỘ GIÁO DỤC VÀ ĐÀO TẠO
('Bộ GD&ĐT - Đề thi chính thức', NULL, 'bo_gd', 'https://moet.gov.vn'),
('Bộ GD&ĐT - Đề minh họa & Tham khảo', NULL, 'bo_gd', 'https://moet.gov.vn'),
('Bộ GD&ĐT - Đề thử nghiệm GDPT 2018', NULL, 'bo_gd', 'https://moet.gov.vn'),

-- 2. CÁC SỞ GIÁO DỤC VÀ ĐÀO TẠO (MIỀN BẮC - 25 TỈNH/THÀNH)
('Sở GD&ĐT Hà Nội', 'Hà Nội', 'so_gd', 'https://hanoi.edu.vn'),
('Sở GD&ĐT Hải Phòng', 'Hải Phòng', 'so_gd', 'https://haiphong.edu.vn'),
('Sở GD&ĐT Hải Dương', 'Hải Dương', 'so_gd', 'https://haiduong.edu.vn'),
('Sở GD&ĐT Hưng Yên', 'Hưng Yên', 'so_gd', 'https://hungyen.edu.vn'),
('Sở GD&ĐT Hà Nam', 'Hà Nam', 'so_gd', 'https://hanam.edu.vn'),
('Sở GD&ĐT Nam Định', 'Nam Định', 'so_gd', 'https://namdinh.edu.vn'),
('Sở GD&ĐT Thái Bình', 'Thái Bình', 'so_gd', 'https://thaibinh.edu.vn'),
('Sở GD&ĐT Ninh Bình', 'Ninh Bình', 'so_gd', 'https://ninhbinh.edu.vn'),
('Sở GD&ĐT Vĩnh Phúc', 'Vĩnh Phúc', 'so_gd', 'https://vinhphuc.edu.vn'),
('Sở GD&ĐT Bắc Ninh', 'Bắc Ninh', 'so_gd', 'https://bacninh.edu.vn'),
('Sở GD&ĐT Bắc Giang', 'Bắc Giang', 'so_gd', 'https://bacgiang.edu.vn'),
('Sở GD&ĐT Quảng Ninh', 'Quảng Ninh', 'so_gd', 'https://quangninh.edu.vn'),
('Sở GD&ĐT Phú Thọ', 'Phú Thọ', 'so_gd', 'https://phutho.edu.vn'),
('Sở GD&ĐT Thái Nguyên', 'Thái Nguyên', 'so_gd', 'https://thainguyen.edu.vn'),
('Sở GD&ĐT Tuyên Quang', 'Tuyên Quang', 'so_gd', 'https://tuyenquang.edu.vn'),
('Sở GD&ĐT Lạng Sơn', 'Lạng Sơn', 'so_gd', 'https://langson.edu.vn'),
('Sở GD&ĐT Bắc Kạn', 'Bắc Kạn', 'so_gd', 'https://backan.edu.vn'),
('Sở GD&ĐT Cao Bằng', 'Cao Bằng', 'so_gd', 'https://caobang.edu.vn'),
('Sở GD&ĐT Hà Giang', 'Hà Giang', 'so_gd', 'https://hagiang.edu.vn'),
('Sở GD&ĐT Lào Cai', 'Lào Cai', 'so_gd', 'https://laocai.edu.vn'),
('Sở GD&ĐT Yên Bái', 'Yên Bái', 'so_gd', 'https://yenbai.edu.vn'),
('Sở GD&ĐT Hòa Bình', 'Hòa Bình', 'so_gd', 'https://hoabinh.edu.vn'),
('Sở GD&ĐT Sơn La', 'Sơn La', 'so_gd', 'https://sonla.edu.vn'),
('Sở GD&ĐT Điện Biên', 'Điện Biên', 'so_gd', 'https://dienbien.edu.vn'),
('Sở GD&ĐT Lai Châu', 'Lai Châu', 'so_gd', 'https://laichau.edu.vn'),

-- 3. CÁC SỞ GIÁO DỤC VÀ ĐÀO TẠO (MIỀN TRUNG & TÂY NGUYÊN - 19 TỈNH/THÀNH)
('Sở GD&ĐT Thanh Hóa', 'Thanh Hóa', 'so_gd', 'https://thanhhoa.edu.vn'),
('Sở GD&ĐT Nghệ An', 'Nghệ An', 'so_gd', 'https://nghean.edu.vn'),
('Sở GD&ĐT Hà Tĩnh', 'Hà Tĩnh', 'so_gd', 'https://hatinh.edu.vn'),
('Sở GD&ĐT Quảng Bình', 'Quảng Bình', 'so_gd', 'https://quangbinh.edu.vn'),
('Sở GD&ĐT Quảng Trị', 'Quảng Trị', 'so_gd', 'https://quangtri.edu.vn'),
('Sở GD&ĐT Thừa Thiên Huế', 'Thừa Thiên Huế', 'so_gd', 'https://thuathienhue.edu.vn'),
('Sở GD&ĐT Đà Nẵng', 'Đà Nẵng', 'so_gd', 'https://danang.edu.vn'),
('Sở GD&ĐT Quảng Nam', 'Quảng Nam', 'so_gd', 'https://quangnam.edu.vn'),
('Sở GD&ĐT Quảng Ngãi', 'Quảng Ngãi', 'so_gd', 'https://quangngai.edu.vn'),
('Sở GD&ĐT Bình Định', 'Bình Định', 'so_gd', 'https://binhdinh.edu.vn'),
('Sở GD&ĐT Phú Yên', 'Phú Yên', 'so_gd', 'https://phuyen.edu.vn'),
('Sở GD&ĐT Khánh Hòa', 'Khánh Hòa', 'so_gd', 'https://khanhhoa.edu.vn'),
('Sở GD&ĐT Ninh Thuận', 'Ninh Thuận', 'so_gd', 'https://ninhthuan.edu.vn'),
('Sở GD&ĐT Bình Thuận', 'Bình Thuận', 'so_gd', 'https://binhthuan.edu.vn'),
('Sở GD&ĐT Kon Tum', 'Kon Tum', 'so_gd', 'https://kontum.edu.vn'),
('Sở GD&ĐT Gia Lai', 'Gia Lai', 'so_gd', 'https://gialai.edu.vn'),
('Sở GD&ĐT Đắk Lắk', 'Đắk Lắk', 'so_gd', 'https://daklak.edu.vn'),
('Sở GD&ĐT Đắk Nông', 'Đắk Nông', 'so_gd', 'https://daknong.edu.vn'),
('Sở GD&ĐT Lâm Đồng', 'Lâm Đồng', 'so_gd', 'https://lamdong.edu.vn'),

-- 4. CÁC SỞ GIÁO DỤC VÀ ĐÀO TẠO (MIỀN NAM - 19 TỈNH/THÀNH)
('Sở GD&ĐT TP. Hồ Chí Minh', 'TP. Hồ Chí Minh', 'so_gd', 'https://hcm.edu.vn'),
('Sở GD&ĐT Cần Thơ', 'Cần Thơ', 'so_gd', 'https://cantho.edu.vn'),
('Sở GD&ĐT Bình Phước', 'Bình Phước', 'so_gd', 'https://binhphuoc.edu.vn'),
('Sở GD&ĐT Tây Ninh', 'Tây Ninh', 'so_gd', 'https://tayninh.edu.vn'),
('Sở GD&ĐT Bình Dương', 'Bình Dương', 'so_gd', 'https://binhduong.edu.vn'),
('Sở GD&ĐT Đồng Nai', 'Đồng Nai', 'so_gd', 'https://dongnai.edu.vn'),
('Sở GD&ĐT Bà Rịa - Vũng Tàu', 'Bà Rịa - Vũng Tàu', 'so_gd', 'https://bariavungtau.edu.vn'),
('Sở GD&ĐT Long An', 'Long An', 'so_gd', 'https://longan.edu.vn'),
('Sở GD&ĐT Tiền Giang', 'Tiền Giang', 'so_gd', 'https://tiengiang.edu.vn'),
('Sở GD&ĐT Bến Tre', 'Bến Tre', 'so_gd', 'https://bentre.edu.vn'),
('Sở GD&ĐT Trà Vinh', 'Trà Vinh', 'so_gd', 'https://travinh.edu.vn'),
('Sở GD&ĐT Vĩnh Long', 'Vĩnh Long', 'so_gd', 'https://vinhlong.edu.vn'),
('Sở GD&ĐT Đồng Tháp', 'Đồng Tháp', 'so_gd', 'https://dongthap.edu.vn'),
('Sở GD&ĐT An Giang', 'An Giang', 'so_gd', 'https://angiang.edu.vn'),
('Sở GD&ĐT Kiên Giang', 'Kiên Giang', 'so_gd', 'https://kiengiang.edu.vn'),
('Sở GD&ĐT Hậu Giang', 'Hậu Giang', 'so_gd', 'https://haugiang.edu.vn'),
('Sở GD&ĐT Sóc Trăng', 'Sóc Trăng', 'so_gd', 'https://soctrang.edu.vn'),
('Sở GD&ĐT Bạc Liêu', 'Bạc Liêu', 'so_gd', 'https://baclieu.edu.vn'),
('Sở GD&ĐT Cà Mau', 'Cà Mau', 'so_gd', 'https://camau.edu.vn'),

-- 5. CÁC TRƯỜNG THPT CHUYÊN DANH TIẾNG TOÀN QUỐC (65+ TRƯỜNG)
('THPT Chuyên Khoa học Tự nhiên - ĐHQGHN', 'Hà Nội', 'truong_chuyen', 'https://hsgs.edu.vn'),
('THPT Chuyên Đại học Sư phạm Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://chuyensupham.edu.vn'),
('THPT Chuyên Ngoại ngữ - ĐHQGHN', 'Hà Nội', 'truong_chuyen', 'https://flss.vnu.edu.vn'),
('THPT Chuyên Khoa học Xã hội và Nhân văn - ĐHQGHN', 'Hà Nội', 'truong_chuyen', 'https://hssh.ussh.edu.vn'),
('THPT Chuyên Hà Nội - Amsterdam', 'Hà Nội', 'truong_chuyen', 'https://hn-ams.edu.vn'),
('THPT Chu Văn An Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://c3chuvanan.edu.vn'),
('THPT Chuyên Nguyễn Huệ', 'Hà Nội', 'truong_chuyen', 'https://chuyennguyenhue.edu.vn'),
('THPT Chuyên Sơn Tây', 'Hà Nội', 'truong_chuyen', 'https://thptsontay.edu.vn'),
('THPT Chuyên Lê Hồng Phong TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://thpt-lehongphong-tphcm.edu.vn'),
('THPT Chuyên Trần Đại Nghĩa', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://trandainghia.edu.vn'),
('THPT Phổ thông Năng khiếu - ĐHQG TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://ptnk.edu.vn'),
('THPT Chuyên Lam Sơn', 'Thanh Hóa', 'truong_chuyen', 'https://chuyenlamson.edu.vn'),
('THPT Chuyên Phan Bội Châu', 'Nghệ An', 'truong_chuyen', 'https://chuyenphanboichau.edu.vn'),
('THPT Chuyên Quốc Học Huế', 'Thừa Thiên Huế', 'truong_chuyen', 'https://quochochue.edu.vn'),
('THPT Chuyên Lê Quý Đôn Đà Nẵng', 'Đà Nẵng', 'truong_chuyen', 'https://thpt-lequydon-danang.edu.vn'),
('THPT Chuyên Trần Phú', 'Hải Phòng', 'truong_chuyen', 'https://chuyentranphu.edu.vn'),
('THPT Chuyên Lê Hồng Phong Nam Định', 'Nam Định', 'truong_chuyen', 'https://chuyenlehongphong.edu.vn'),
('THPT Chuyên Vĩnh Phúc', 'Vĩnh Phúc', 'truong_chuyen', 'https://chuyenvinhphuc.edu.vn'),
('THPT Chuyên Bắc Ninh', 'Bắc Ninh', 'truong_chuyen', 'https://chuyenbacninh.edu.vn'),
('THPT Chuyên Hùng Vương Phú Thọ', 'Phú Thọ', 'truong_chuyen', 'https://chuyenhungvuong.edu.vn'),
('THPT Chuyên Hạ Long', 'Quảng Ninh', 'truong_chuyen', 'https://chuyenhalong.edu.vn'),
('THPT Chuyên Thái Bình', 'Thái Bình', 'truong_chuyen', 'https://chuyenthaibinh.edu.vn'),
('THPT Chuyên Bắc Giang', 'Bắc Giang', 'truong_chuyen', 'https://chuyenbacgiang.edu.vn'),
('THPT Chuyên Lương Văn Tụy', 'Ninh Bình', 'truong_chuyen', 'https://chuyenluongvantuy.edu.vn'),
('THPT Chuyên Biên Hòa', 'Hà Nam', 'truong_chuyen', 'https://chuyenbienhoa.edu.vn'),
('THPT Chuyên Nguyễn Trãi', 'Hải Dương', 'truong_chuyen', 'https://chuyennguyentrai.edu.vn'),
('THPT Chuyên Hưng Yên', 'Hưng Yên', 'truong_chuyen', 'https://chuyenhungyen.edu.vn'),
('THPT Chuyên Hà Tĩnh', 'Hà Tĩnh', 'truong_chuyen', 'https://chuyenhatinh.edu.vn'),
('THPT Chuyên Võ Nguyên Giáp', 'Quảng Bình', 'truong_chuyen', 'https://chuyenvonguyengiap.edu.vn'),
('THPT Chuyên Lê Quý Đôn Quảng Trị', 'Quảng Trị', 'truong_chuyen', 'https://chuyenlequydonquangtri.edu.vn'),
('THPT Chuyên Nguyễn Bỉnh Khiêm', 'Quảng Nam', 'truong_chuyen', 'https://chuyennguyenbinhkhiem.edu.vn'),
('THPT Chuyên Lê Thánh Tông', 'Quảng Nam', 'truong_chuyen', 'https://thptchuyenlethanhtong.edu.vn'),
('THPT Chuyên Lê Khiết', 'Quảng Ngãi', 'truong_chuyen', 'https://chuyenlekhiet.edu.vn'),
('THPT Chuyên Lê Quý Đôn Bình Định', 'Bình Định', 'truong_chuyen', 'https://chuyenlequydonbinhdinh.edu.vn'),
('THPT Chuyên Lương Văn Chánh', 'Phú Yên', 'truong_chuyen', 'https://luongvanchanh.edu.vn'),
('THPT Chuyên Lê Quý Đôn Nha Trang', 'Khánh Hòa', 'truong_chuyen', 'https://lequydon-nhatrang.edu.vn'),
('THPT Chuyên Lê Quý Đôn Ninh Thuận', 'Ninh Thuận', 'truong_chuyen', 'https://chuyenlequydonninhthuan.edu.vn'),
('THPT Chuyên Trần Hưng Đạo', 'Bình Thuận', 'truong_chuyen', 'https://tranhungdaobt.edu.vn'),
('THPT Chuyên Nguyễn Tất Thành Kon Tum', 'Kon Tum', 'truong_chuyen', 'https://chuyennguyentattthanh.edu.vn'),
('THPT Chuyên Hùng Vương Gia Lai', 'Gia Lai', 'truong_chuyen', 'https://chuyenhungvuonggialai.edu.vn'),
('THPT Chuyên Nguyễn Du Đắk Lắk', 'Đắk Lắk', 'truong_chuyen', 'https://chuyennguyendu.edu.vn'),
('THPT Chuyên Thăng Long Đà Lạt', 'Lâm Đồng', 'truong_chuyen', 'https://chuyenthanglongdalat.edu.vn'),
('THPT Chuyên Bảo Lộc', 'Lâm Đồng', 'truong_chuyen', 'https://chuyenbaoloc.edu.vn'),
('THPT Chuyên Quang Trung Bình Phước', 'Bình Phước', 'truong_chuyen', 'https://chuyenquangtrung.edu.vn'),
('THPT Chuyên Hoàng Lê Kha', 'Tây Ninh', 'truong_chuyen', 'https://chuyenhoanglekha.edu.vn'),
('THPT Chuyên Hùng Vương Bình Dương', 'Bình Dương', 'truong_chuyen', 'https://chuyenhungvuongbinhduong.edu.vn'),
('THPT Chuyên Lương Thế Vinh Đồng Nai', 'Đồng Nai', 'truong_chuyen', 'https://chuyenluongthevinh.edu.vn'),
('THPT Chuyên Lê Quý Đôn Vũng Tàu', 'Bà Rịa - Vũng Tàu', 'truong_chuyen', 'https://chuyenlequydon.edu.vn'),
('THPT Chuyên Long An', 'Long An', 'truong_chuyen', 'https://chuyenlongan.edu.vn'),
('THPT Chuyên Tiền Giang', 'Tiền Giang', 'truong_chuyen', 'https://chuyentiengiang.edu.vn'),
('THPT Chuyên Bến Tre', 'Bến Tre', 'truong_chuyen', 'https://chuyenbentre.edu.vn'),
('THPT Chuyên Nguyễn Bỉnh Khiêm Vĩnh Long', 'Vĩnh Long', 'truong_chuyen', 'https://chuyennguyenbinhkhiemvinhlong.edu.vn'),
('THPT Chuyên Nguyễn Đình Chiểu Đồng Tháp', 'Đồng Tháp', 'truong_chuyen', 'https://chuyennguyendinhchieu.edu.vn'),
('THPT Chuyên Nguyễn Quang Diêu', 'Đồng Tháp', 'truong_chuyen', 'https://chuyennguyenquangdieu.edu.vn'),
('THPT Chuyên Thoại Ngọc Hầu An Giang', 'An Giang', 'truong_chuyen', 'https://chuyenthoaingochau.edu.vn'),
('THPT Chuyên Thủ Khoa Nghĩa', 'An Giang', 'truong_chuyen', 'https://chuyenthukhoanghia.edu.vn'),
('THPT Chuyên Lý Tự Trọng Cần Thơ', 'Cần Thơ', 'truong_chuyen', 'https://chuyenlytutrong.edu.vn'),
('THPT Chuyên Huỳnh Mẫn Đạt', 'Kiên Giang', 'truong_chuyen', 'https://chuyenhuynhmandat.edu.vn'),
('THPT Chuyên Vị Thanh', 'Hậu Giang', 'truong_chuyen', 'https://chuyenvithanh.edu.vn'),
('THPT Chuyên Nguyễn Thị Minh Khai Sóc Trăng', 'Sóc Trăng', 'truong_chuyen', 'https://chuyennguyenthiminhkhai.edu.vn'),
('THPT Chuyên Bạc Liêu', 'Bạc Liêu', 'truong_chuyen', 'https://chuyenbaclieu.edu.vn'),
('THPT Chuyên Phan Ngọc Hiển Cà Mau', 'Cà Mau', 'truong_chuyen', 'https://chuyenphanngochien.edu.vn'),
('THPT Chuyên Thái Nguyên', 'Thái Nguyên', 'truong_chuyen', 'https://chuyenthainguyen.edu.vn'),
('THPT Chuyên Tuyên Quang', 'Tuyên Quang', 'truong_chuyen', 'https://chuyentuyenquang.edu.vn'),
('THPT Chuyên Chu Văn An Lạng Sơn', 'Lạng Sơn', 'truong_chuyen', 'https://chuyenchuvananlangson.edu.vn'),
('THPT Chuyên Lào Cai', 'Lào Cai', 'truong_chuyen', 'https://chuyenlaocai.edu.vn'),
('THPT Chuyên Hoàng Văn Thụ Hòa Bình', 'Hòa Bình', 'truong_chuyen', 'https://chuyenhoangvanthu.edu.vn'),
('THPT Chuyên Sơn La', 'Sơn La', 'truong_chuyen', 'https://chuyensonla.edu.vn'),
('THPT Chuyên Lê Quý Đôn Điện Biên', 'Điện Biên', 'truong_chuyen', 'https://chuyenlequydondienbien.edu.vn'),

-- 6. CÁC TRƯỜNG THPT TRỌNG ĐIỂM CHẤT LƯỢNG CAO (16 TRƯỜNG)
('THPT Kim Liên Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://c3kimlien.edu.vn'),
('THPT Yên Hòa Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://c3yenhoa.edu.vn'),
('THPT Thăng Long Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://thptthanglonghanoi.edu.vn'),
('THPT Phan Đình Phùng Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://c3phandinhphung.edu.vn'),
('THPT Việt Đức Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://c3vietduc.edu.vn'),
('THPT Lương Thế Vinh Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://luongthevinh.com.vn'),
('THPT Nguyễn Tất Thành Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://ntt.edu.vn'),
('THPT Cầu Giấy Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://thptcaugiay.edu.vn'),
('THPT Nhân Chính Hà Nội', 'Hà Nội', 'truong_chuyen', 'https://thptnhanchinh.edu.vn'),
('THPT Nguyễn Thượng Hiền TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://thptnguyenthuonghien.hcm.edu.vn'),
('THPT Gia Định TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://thptgiadinh.hcm.edu.vn'),
('THPT Bùi Thị Xuân TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://thptbuithixuan.hcm.edu.vn'),
('THPT Nguyễn Thị Minh Khai TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://thptminhkhai.hcm.edu.vn'),
('THPT Mạc Đĩnh Chi TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://thptmacdinhchi.hcm.edu.vn'),
('THPT Phú Nhuận TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://thptphunhuan.hcm.edu.vn'),
('THPT Lê Quý Đôn TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', 'https://thptlequydonhcm.edu.vn'),

-- 7. CÁC KỲ THI ĐÁNH GIÁ NĂNG LỰC & ĐÁNH GIÁ TƯ DUY ĐẠI HỌC
('Kỳ thi ĐGNL ĐHQG Hà Nội (HSA)', 'Hà Nội', 'luyen_de', 'https://hsa.edu.vn'),
('Kỳ thi ĐGNL ĐHQG TP.HCM (APT)', 'TP. Hồ Chí Minh', 'luyen_de', 'https://cete.vnuhcm.edu.vn'),
('Kỳ thi Đánh giá Tư duy ĐH Bách Khoa Hà Nội (TSA)', 'Hà Nội', 'luyen_de', 'https://tsa.hust.edu.vn'),
('Kỳ thi ĐGNL Trường ĐH Sư phạm Hà Nội', 'Hà Nội', 'luyen_de', 'https://hnue.edu.vn'),
('Kỳ thi ĐGNL Trường ĐH Sư phạm TP.HCM', 'TP. Hồ Chí Minh', 'luyen_de', 'https://hcmue.edu.vn'),
('Kỳ thi Đánh giá Tuyển sinh Bộ Công An', NULL, 'luyen_de', 'https://bocongan.gov.vn'),

-- 8. CÁC CỤM LIÊN TRƯỜNG & KHẢO SÁT CHẤT LƯỢNG UY TÍN TOÀN QUỐC
('Cụm các trường THPT Chuyên Khu vực Duyên hải và Đồng bằng Bắc Bộ', NULL, 'luyen_de', NULL),
('Cụm các trường THPT Chuyên Đại học Quốc gia', 'Hà Nội', 'luyen_de', NULL),
('Cụm thi thử tốt nghiệp THPT Tỉnh Nam Định', 'Nam Định', 'luyen_de', NULL),
('Cụm thi thử tốt nghiệp THPT Tỉnh Nghệ An', 'Nghệ An', 'luyen_de', NULL),
('Cụm thi thử tốt nghiệp THPT Tỉnh Thanh Hóa', 'Thanh Hóa', 'luyen_de', NULL),
('Cụm thi thử tốt nghiệp THPT Tỉnh Bắc Giang', 'Bắc Giang', 'luyen_de', NULL),
('Cụm thi thử tốt nghiệp THPT Tỉnh Hà Tĩnh', 'Hà Tĩnh', 'luyen_de', NULL),
('Cụm thi thử tốt nghiệp THPT Tỉnh Vĩnh Phúc', 'Vĩnh Phúc', 'luyen_de', NULL),
('Cụm thi thử tốt nghiệp THPT Tỉnh Hải Dương', 'Hải Dương', 'luyen_de', NULL),
('Cụm thi thử tốt nghiệp THPT Tỉnh Thái Bình', 'Thái Bình', 'luyen_de', NULL),
('Cụm liên trường THPT TP. Hà Nội', 'Hà Nội', 'luyen_de', NULL),
('Cụm liên trường THPT TP. Hồ Chí Minh', 'TP. Hồ Chí Minh', 'luyen_de', NULL),
('Khảo sát chất lượng THPT Quốc gia Sở GD&ĐT Hà Nội', 'Hà Nội', 'luyen_de', NULL),
('Khảo sát chất lượng THPT Quốc gia Sở GD&ĐT Nam Định', 'Nam Định', 'luyen_de', NULL),
('Khảo sát chất lượng THPT Quốc gia Sở GD&ĐT Vĩnh Phúc', 'Vĩnh Phúc', 'luyen_de', NULL),
('Khảo sát chất lượng THPT Quốc gia Sở GD&ĐT Hải Phòng', 'Hải Phòng', 'luyen_de', NULL),

-- 9. CÁC CỔNG HỌC LIỆU MỞ & NỀN TẢNG LUYỆN THI TRỰC TUYẾN
('Thư viện Học liệu', NULL, 'luyen_de', 'https://thuvienhoclieu.com'),
('Tuyensinh247', NULL, 'luyen_de', 'https://tuyensinh247.com'),
('VietJack', NULL, 'luyen_de', 'https://vietjack.com'),
('Hocmai.vn', NULL, 'luyen_de', 'https://hocmai.vn'),
('OLM - Online Learning', NULL, 'luyen_de', 'https://olm.vn'),
('Đề thi Thử THPT Quốc gia', NULL, 'luyen_de', 'https://thithuthpt.vn'),
('VTV7 - Chinh phục Kỳ thi', NULL, 'luyen_de', 'https://vtv.vn')

ON CONFLICT (name) DO NOTHING;


--- ============================================================
--- MIGRATION: 002_seed_real_exams.sql
--- ============================================================

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


--- ============================================================
--- MIGRATION: 003_rbac_system.sql
--- ============================================================

-- ============================================================
-- OmniPrep - Hệ Thống Phân Quyền Người Dùng (RBAC Schema v1.0)
-- File: 003_rbac_system.sql
-- Hướng dẫn: Mở Supabase Dashboard -> SQL Editor -> Thực thi script.
-- ============================================================

-- ============================================================
-- 1. TẠO ENUM USER_ROLE (3 Vai trò phân cấp)
-- ============================================================
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'user_role') THEN
        CREATE TYPE user_role AS ENUM (
            'STUDENT',   -- Học sinh: Luyện đề, xem điểm, AI Tutor, sổ tay câu sai
            'TEACHER',   -- Giáo viên: Quản lý ngân hàng câu hỏi, duyệt đề, xem phổ điểm
            'ADMIN'      -- Quản trị viên: Toàn quyền hệ thống, quản trị Bot & Ingestion 24/7
        );
    END IF;
END $$;

-- ============================================================
-- 2. BỔ SUNG CỘT ROLE VÀO BẢNG PROFILES
-- ============================================================
ALTER TABLE profiles 
    ADD COLUMN IF NOT EXISTS role user_role NOT NULL DEFAULT 'STUDENT';

COMMENT ON COLUMN profiles.role IS 'Vai trò người dùng trong hệ thống: STUDENT, TEACHER, ADMIN';

-- Index hỗ trợ truy vấn lọc theo vai trò
CREATE INDEX IF NOT EXISTS idx_profiles_role ON profiles(role);

-- ============================================================
-- 3. HÀM HELPER HỖ TRỢ KIỂM TRA PHÂN QUYỀN TRONG RLS & API
-- ============================================================

-- Hàm lấy vai trò của người dùng hiện tại (Current Auth User)
CREATE OR REPLACE FUNCTION auth.current_user_role()
RETURNS user_role AS $$
DECLARE
    v_role user_role;
BEGIN
    SELECT role INTO v_role
    FROM public.profiles
    WHERE id = auth.uid();
    
    RETURN COALESCE(v_role, 'STUDENT'::user_role);
END;
$$ LANGUAGE plpgsql SECURITY DEFINER STABLE;

-- Hàm kiểm tra người dùng có phải Quản trị viên (ADMIN) không
CREATE OR REPLACE FUNCTION is_admin()
RETURNS BOOLEAN AS $$
BEGIN
    RETURN (auth.current_user_role() = 'ADMIN'::user_role);
END;
$$ LANGUAGE plpgsql SECURITY DEFINER STABLE;

-- Hàm kiểm tra người dùng có quyền Giảng viên trở lên (TEACHER hoặc ADMIN)
CREATE OR REPLACE FUNCTION is_teacher_or_admin()
RETURNS BOOLEAN AS $$
BEGIN
    RETURN (auth.current_user_role() IN ('TEACHER'::user_role, 'ADMIN'::user_role));
END;
$$ LANGUAGE plpgsql SECURITY DEFINER STABLE;

-- ============================================================
-- 4. CẬP NHẬT ROW LEVEL SECURITY (RLS) POLICIES
-- ============================================================

-- Bảng profiles:
-- Học sinh chỉ xem và sửa thông tin cá nhân của mình, KHÔNG tự nâng role
DROP POLICY IF EXISTS "profiles_self" ON profiles;
DROP POLICY IF EXISTS "profiles_admin_all" ON profiles;

CREATE POLICY "profiles_self_select" ON profiles
    FOR SELECT USING (auth.uid() = id OR is_admin());

CREATE POLICY "profiles_self_update" ON profiles
    FOR UPDATE USING (auth.uid() = id)
    WITH CHECK (
        -- Không cho phép user tự thay đổi role của mình, trừ khi là Admin
        (role = (SELECT p.role FROM profiles p WHERE p.id = auth.uid())) 
        OR is_admin()
    );

CREATE POLICY "profiles_admin_all" ON profiles
    FOR ALL USING (is_admin());

-- Bảng questions:
-- Mọi người được đọc câu hỏi active.
-- Chỉ TEACHER hoặc ADMIN mới được tạo mới, cập nhật, hoặc duyệt câu hỏi (is_verified).
DROP POLICY IF EXISTS "questions_public_read" ON questions;
DROP POLICY IF EXISTS "questions_teacher_admin_write" ON questions;

CREATE POLICY "questions_public_read" ON questions
    FOR SELECT USING (is_active = TRUE OR is_teacher_or_admin());

CREATE POLICY "questions_teacher_admin_insert" ON questions
    FOR INSERT WITH CHECK (is_teacher_or_admin());

CREATE POLICY "questions_teacher_admin_update" ON questions
    FOR UPDATE USING (is_teacher_or_admin());

CREATE POLICY "questions_admin_delete" ON questions
    FOR DELETE USING (is_admin());

-- Bảng exam_papers (Đề thi cào & nạp):
-- Chỉ ADMIN có quyền ghi và kích hoạt Ingestion.
-- TEACHER có quyền xem để duyệt đề.
DROP POLICY IF EXISTS "exam_papers_admin_all" ON exam_papers;
DROP POLICY IF EXISTS "exam_papers_teacher_read" ON exam_papers;

ALTER TABLE exam_papers ENABLE ROW LEVEL SECURITY;

CREATE POLICY "exam_papers_admin_all" ON exam_papers
    FOR ALL USING (is_admin());

CREATE POLICY "exam_papers_teacher_read" ON exam_papers
    FOR SELECT USING (is_teacher_or_admin());

-- Bảng exam_attempts & user_topic_mastery:
-- Học sinh xem bài thi của chính mình.
-- Giáo viên & Admin có quyền xem phổ điểm và lịch sử làm bài để đánh giá chất lượng dạy học.
DROP POLICY IF EXISTS "exam_attempts_teacher_read" ON exam_attempts;
CREATE POLICY "exam_attempts_teacher_read" ON exam_attempts
    FOR SELECT USING (is_teacher_or_admin());

-- ============================================================
-- 5. TRIGGER TỰ ĐỘNG KHỞI TẠO PROFILE VỚI ROLE MẶC ĐỊNH
-- Khi người dùng đăng ký tài khoản qua Supabase Auth
-- ============================================================
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO public.profiles (id, full_name, role, avatar_url, created_at, updated_at)
    VALUES (
        NEW.id,
        COALESCE(NEW.raw_user_meta_data->>'full_name', NEW.email),
        'STUDENT'::user_role,
        NEW.raw_user_meta_data->>'avatar_url',
        NOW(),
        NOW()
    )
    ON CONFLICT (id) DO UPDATE
    SET 
        full_name = EXCLUDED.full_name,
        avatar_url = EXCLUDED.avatar_url;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- Đăng ký trigger trên auth.users
DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;
CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW EXECUTE FUNCTION public.handle_new_user();

-- Privileged roles must be assigned by a trusted administrator, not user metadata.


--- ============================================================
--- MIGRATION: 004_ingestion_queue_content.sql
--- ============================================================

-- Persist submitted source text so the background worker can resume queued jobs.
ALTER TABLE exam_papers
    ADD COLUMN IF NOT EXISTS raw_content TEXT;

-- Ensure newly created queues always have the source fingerprint available.
CREATE INDEX IF NOT EXISTS idx_exam_papers_pending
    ON exam_papers (submitted_at)
    WHERE status = 'PENDING';


--- ============================================================
--- MIGRATION: 005_trusted_profile_roles.sql
--- ============================================================

-- Existing installations: stop copying a user-controlled role from signup metadata.
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    INSERT INTO public.profiles (id, full_name, role, avatar_url, created_at, updated_at)
    VALUES (
        NEW.id,
        COALESCE(NEW.raw_user_meta_data->>'full_name', NEW.email),
        'STUDENT'::public.user_role,
        NEW.raw_user_meta_data->>'avatar_url',
        NOW(),
        NOW()
    )
    ON CONFLICT (id) DO UPDATE
    SET full_name = EXCLUDED.full_name,
        avatar_url = EXCLUDED.avatar_url;
    RETURN NEW;
END;
$$;


--- ============================================================
--- MIGRATION: 006_attempt_answer_autosave.sql
--- ============================================================

-- Persist in-progress answers separately from submitted grading history.
CREATE TABLE IF NOT EXISTS attempt_answers (
    attempt_id UUID NOT NULL REFERENCES exam_attempts(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    question_id UUID NOT NULL REFERENCES questions(id) ON DELETE CASCADE,
    answer JSONB,
    time_spent_seconds INTEGER,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (attempt_id, question_id)
);

CREATE INDEX IF NOT EXISTS idx_attempt_answers_user_attempt
    ON attempt_answers(user_id, attempt_id);

ALTER TABLE attempt_answers ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS attempt_answers_self ON attempt_answers;
CREATE POLICY attempt_answers_self ON attempt_answers
    USING (auth.uid() = user_id)
    WITH CHECK (auth.uid() = user_id);


--- ============================================================
--- MIGRATION: 007_ingestion_leases.sql
--- ============================================================

-- Apply with all ingestion workers stopped to avoid overlapping lease versions.
ALTER TABLE exam_papers
    ADD COLUMN IF NOT EXISTS processing_attempts SMALLINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS processing_started_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS processing_lease_id UUID;

-- Give in-flight records a fresh lease during rollout so abandoned jobs can be reclaimed.
UPDATE exam_papers
SET processing_started_at = NOW(),
    processing_lease_id = gen_random_uuid()
WHERE status = 'PROCESSING'
  AND (processing_started_at IS NULL OR processing_lease_id IS NULL);

CREATE INDEX IF NOT EXISTS idx_exam_papers_processing_lease
    ON exam_papers(processing_started_at)
    WHERE status = 'PROCESSING';


--- ============================================================
--- MIGRATION: 008_question_moderation_audit.sql
--- ============================================================

-- Preserve who reviewed generated questions and why rejected items were removed.
ALTER TABLE questions
    ADD COLUMN IF NOT EXISTS reviewed_by UUID REFERENCES profiles(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS reviewed_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS review_note TEXT;

CREATE INDEX IF NOT EXISTS idx_questions_pending_review
    ON questions (created_at, id)
    WHERE is_active = TRUE AND is_verified = FALSE;


--- ============================================================
--- MIGRATION: 009_user_mistakes_and_privacy.sql
--- ============================================================

-- ============================================================
-- 009: Bảng lưu trữ Sổ tay bẫy đề thi (Mistake Notebook) trên máy chủ
-- Đồng bộ dữ liệu học tập cá nhân và bảo vệ quyền riêng tư
-- ============================================================

CREATE TABLE IF NOT EXISTS user_mistakes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
    question_id UUID NOT NULL REFERENCES questions(id) ON DELETE CASCADE,
    subject_code subject_code NOT NULL,
    student_answer JSONB,
    mastered BOOLEAN NOT NULL DEFAULT FALSE,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_user_mistakes_user_question UNIQUE (user_id, question_id)
);

CREATE INDEX IF NOT EXISTS idx_user_mistakes_user_subject
    ON user_mistakes (user_id, subject_code);

CREATE INDEX IF NOT EXISTS idx_user_mistakes_user_mastered
    ON user_mistakes (user_id, mastered);

-- RLS: Học sinh chỉ xem và chỉnh sửa câu sai của chính mình
ALTER TABLE user_mistakes ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS user_mistakes_manage_own ON user_mistakes;
CREATE POLICY user_mistakes_manage_own ON user_mistakes
    FOR ALL TO authenticated
    USING (auth.uid() = user_id)
    WITH CHECK (auth.uid() = user_id);

