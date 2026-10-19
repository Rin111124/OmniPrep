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
