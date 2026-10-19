-- Preserve who reviewed generated questions and why rejected items were removed.
ALTER TABLE questions
    ADD COLUMN IF NOT EXISTS reviewed_by UUID REFERENCES profiles(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS reviewed_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS review_note TEXT;

CREATE INDEX IF NOT EXISTS idx_questions_pending_review
    ON questions (created_at, id)
    WHERE is_active = TRUE AND is_verified = FALSE;
