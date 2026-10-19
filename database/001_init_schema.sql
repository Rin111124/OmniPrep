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
('Sở GD&ĐT Hà Nội', 'Hà Nội', 'so_gd', 'https://hanoi.edu.vn'),
('Sở GD&ĐT TP. Hồ Chí Minh', 'TP. Hồ Chí Minh', 'so_gd', 'https://hcm.edu.vn'),
('Sở GD&ĐT Đà Nẵng', 'Đà Nẵng', 'so_gd', NULL),
('Sở GD&ĐT Hải Phòng', 'Hải Phòng', 'so_gd', NULL),
('THPT Chuyên Lê Hồng Phong TP.HCM', 'TP. Hồ Chí Minh', 'truong_chuyen', NULL),
('THPT Chuyên Hà Nội - Amsterdam', 'Hà Nội', 'truong_chuyen', NULL),
('Bộ GD&ĐT - Đề thi chính thức', NULL, 'bo_gd', 'https://moet.gov.vn')
ON CONFLICT (name) DO NOTHING;
