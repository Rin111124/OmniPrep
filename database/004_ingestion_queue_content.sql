-- Persist submitted source text so the background worker can resume queued jobs.
ALTER TABLE exam_papers
    ADD COLUMN IF NOT EXISTS raw_content TEXT;

-- Ensure newly created queues always have the source fingerprint available.
CREATE INDEX IF NOT EXISTS idx_exam_papers_pending
    ON exam_papers (submitted_at)
    WHERE status = 'PENDING';
