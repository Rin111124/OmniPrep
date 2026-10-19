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
