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
