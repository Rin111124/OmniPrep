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
CREATE EXTENSION IF NOT EXISTS "vector";
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
    subject_id          UUID NOT NULL REFERENCES subjects(id) ON UPDATE CASCADE,

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
    content_hash        CHAR(64) NOT NULL DEFAULT '',
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

-- Đảm bảo cột content_hash luôn có default và trigger tự động băm SHA-256
ALTER TABLE questions ALTER COLUMN content_hash SET DEFAULT '';

CREATE OR REPLACE FUNCTION set_question_content_hash()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.content_hash IS NULL OR LENGTH(TRIM(NEW.content_hash)) = 0 THEN
        NEW.content_hash := encode(sha256(NEW.content::bytea), 'hex');
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_set_question_content_hash ON questions;
CREATE TRIGGER trg_set_question_content_hash
BEFORE INSERT OR UPDATE ON questions
FOR EACH ROW
EXECUTE FUNCTION set_question_content_hash();

-- ============================================================
-- BƯỚC 7: BẢNG EXAM_PAPERS - Đề thi gốc được nạp vào hệ thống
-- ============================================================
CREATE TABLE IF NOT EXISTS exam_papers (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    source_id       UUID NOT NULL REFERENCES exam_sources(id),
    subject_id      UUID NOT NULL REFERENCES subjects(id) ON UPDATE CASCADE,
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
    subject_id      UUID NOT NULL REFERENCES subjects(id) ON UPDATE CASCADE,
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
    subject_id          UUID NOT NULL REFERENCES subjects(id) ON UPDATE CASCADE,

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
    subject_id      UUID NOT NULL REFERENCES subjects(id) ON UPDATE CASCADE,
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

INSERT INTO subjects (id, code, name, exam_matrix, topics, total_score) VALUES
(
    '11111111-1111-1111-1111-111111111111',
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
    '22222222-2222-2222-2222-222222222222',
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
ON CONFLICT (code) DO UPDATE SET
    id = EXCLUDED.id,
    name = EXCLUDED.name,
    topics = EXCLUDED.topics,
    exam_matrix = EXCLUDED.exam_matrix,
    total_score = EXCLUDED.total_score;

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
