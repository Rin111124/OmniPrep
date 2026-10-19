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
