-- ============================================================
-- OmniPrep - Vá Lỗ Hổng Bảo Mật RLS Bảng Questions
-- File: 010_secure_questions_rls.sql
-- Mục đích: Thu hồi quyền SELECT trực tiếp từ role anon / student
--           qua PostgREST API nhằm bảo vệ đáp án (answer_data) và lời giải (explanation).
--           Học sinh bắt buộc phải nhận đề qua .NET Backend API (đã lọc đáp án).
-- ============================================================

-- 1. Xóa bỏ chính sách đọc công khai cũ cho phép bất kỳ ai đọc câu hỏi active
DROP POLICY IF EXISTS "questions_public_read" ON questions;
DROP POLICY IF EXISTS "questions_privileged_select" ON questions;

-- 2. Chỉ cho phép Giảng viên (TEACHER) và Quản trị viên (ADMIN) đọc trực tiếp từ bảng questions
CREATE POLICY "questions_privileged_select" ON questions
    FOR SELECT USING (is_teacher_or_admin());

COMMENT ON POLICY "questions_privileged_select" ON questions IS 
    'Chỉ TEACHER và ADMIN mới được đọc trực tiếp từ bảng questions qua PostgREST. Học sinh phải làm bài qua .NET API.';
