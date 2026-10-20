# 🚀 Hướng Dẫn Triển Khai OmniPrep (Prep12.vn) Vào Vận Hành Thực Tế

Tài liệu này cung cấp hướng dẫn từng bước từ chuẩn bị hạ tầng, cấu hình bảo mật, nạp cơ sở dữ liệu đến triển khai Backend API, Background Worker và Frontend Next.js lên môi trường Staging / Production.

---

## 🏗️ 1. KIẾN TRÚC TỔNG THỂ HỆ THỐNG

```
[ Học sinh / Giáo viên / Admin ]
               │
               ▼
   [ Next.js 16 Frontend (Vercel / VPS:3000) ]
   ├── Giao diện làm bài thi, KaTeX, Sổ tay câu sai
   └── BFF Proxy (/api/backend/*) ── kiểm tra CSRF & Token
               │
               ▼
   [ ASP.NET Core 10 Web API (Port 8080/5000) ]
   ├── Supabase OIDC/JWKS Authentication (ES256/RS256)
   ├── Rate Limiting (10 req/phút/user cho AI Tutor)
   ├── Sinh đề thi có Fallback Spaced Repetition
   └── Chấm điểm 3 phần chuẩn Bộ GD&ĐT 2025
         │                 │
         │ (pgvector)      │ (SSE Streaming / Embedding)
         ▼                 ▼
[ Supabase PostgreSQL ]    [ Google Gemini 2.0 API ]
├── Extension vector       ├── gemini-2.0-flash (Tutor & Bóc tách)
├── HNSW Index Cosine      └── text-embedding-004 (Vector 768d)
├── Row Level Security
└── RBAC Profiles
         ▲
         │ (Claim Job & Dedup)
[ Background Ingestion Worker (.NET 10 Daemon) ]
```

---

## 📦 2. CHUẨN BỊ TÀI NGUYÊN & SECRET KEYS

Trước khi triển khai, bạn cần chuẩn bị:

| Dịch vụ | Nơi đăng ký | Dữ liệu cần lấy |
| :--- | :--- | :--- |
| **Supabase** | [supabase.com](https://supabase.com) | `Project URL`, `Anon Public Key`, `PostgreSQL Connection String` |
| **Google AI Studio** | [aistudio.google.com](https://aistudio.google.com) | `Gemini API Key` |
| **Hosting Frontend** | [vercel.com](https://vercel.com) hoặc VPS | Domain (ví dụ: `https://prep12.vn`) |
| **Hosting Backend** | Cloud Run / Render / Railway / VPS | Domain API (ví dụ: `https://api.prep12.vn`) |

---

## 🗄️ 3. BƯỚC 1: KHỞI TẠO CƠ SỞ DỮ LIỆU SUPABASE

1. **Tạo Project mới trên Supabase**:
   - Chọn Region gần Việt Nam nhất: **Singapore (`ap-southeast-1`)** để đạt độ trễ thấp nhất (< 40ms).
   - Đặt mật khẩu database mạnh và lưu lại.

2. **Chạy Migration SQL**:
   - Mở **Supabase Dashboard** -> vào tab **SQL Editor**.
   - Mở tệp [database/all_migrations.sql](file:///d:/OmniPrep/database/all_migrations.sql), sao chép toàn bộ nội dung và bấm **Run**.
   - Script sẽ tự động:
     - Kích hoạt extension `vector` (pgvector) và `uuid-ossp`.
     - Tạo toàn bộ bảng: `profiles`, `subjects`, `questions`, `exam_papers`, `exam_attempts`, `user_mistakes`,...
     - Cấu hình chỉ mục HNSW cho vector embedding 768 chiều.
     - Cài đặt RLS bảo mật phân quyền (áp dụng `010_secure_questions_rls.sql` để chống lộ đáp án qua PostgREST).
     - Nạp dữ liệu seed chuẩn ban đầu cho môn Địa lý và GDKT&PL.

3. **Cấu hình Supabase Auth**:
   - Vào **Authentication** -> **URL Configuration**:
     - **Site URL**: `https://prep12.vn` (hoặc URL frontend staging).
     - **Redirect URLs**: Thêm `https://prep12.vn/auth/callback` và `http://localhost:3000/auth/callback`.
   - Vào **Authentication** -> **Providers** -> **Email**:
     - Bật tính năng xác thực email (Confirm Email).

4. **Lấy Connection String cho .NET Backend**:
   - Vào **Project Settings** -> **Database** -> mục **Connection string**.
   - Chọn tab **URI** -> Chọn chế độ **Direct Connection** (hoặc **Session Pooler** cổng 5432).
   - Định dạng chuẩn:
     ```text
     Host=aws-0-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-id>;Password=<your-password>;SSL Mode=Require
     ```

---

## 🔑 4. BƯỚC 2: CẤP QUYỀN ADMIN & GIÁO VIÊN BAN ĐẦU

Mặc định khi đăng ký tài khoản qua web, mọi người dùng đều có vai trò là `STUDENT`. Để cấp quyền Quản trị viên (`ADMIN`) hoặc Giáo viên (`TEACHER`):

1. Học sinh đăng ký tài khoản qua trang `/login`.
2. Mở **Supabase Dashboard** -> **Authentication** -> tìm User ID (`UUID`) của người cần phân quyền.
3. Mở **SQL Editor** và chạy câu lệnh sau:
   ```sql
   -- Cấp quyền ADMIN:
   UPDATE public.profiles 
   SET role = 'ADMIN'::public.user_role 
   WHERE id = 'UUID_NGUOI_DUNG_CAN_CAP_QUYEN';

   -- Cập nhật app_metadata trong Auth để JWT chứa claim role chính xác:
   UPDATE auth.users
   SET raw_app_meta_data = raw_app_meta_data || '{"role": "ADMIN"}'::jsonb
   WHERE id = 'UUID_NGUOI_DUNG_CAN_CAP_QUYEN';
   ```

---

## 🖥️ 5. BƯỚC 3: TRIỂN KHAI BACKEND API & WORKER

### Lựa chọn A: Triển khai bằng Docker Compose trên VPS (Khuyến nghị cho quản lý trọn gói)

1. Cài đặt Docker & Docker Compose trên Ubuntu VPS:
   ```bash
   sudo apt update && sudo apt install -y docker.io docker-compose-v2
   ```

2. Tạo tệp `.env` tại thư mục gốc của dự án trên VPS:
   ```env
   ASPNETCORE_ENVIRONMENT=Production
   ConnectionStrings__Supabase="Host=db.your-id.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=your-password;SSL Mode=Require"
   Supabase__Url=https://your-id.supabase.co
   Gemini__ApiKey=AIzaSy...your-gemini-key
   Cors__AllowedOrigins=https://prep12.vn,https://omniprep.vercel.app
   Frontend__Url=https://prep12.vn
   NEXT_PUBLIC_SUPABASE_URL=https://your-id.supabase.co
   NEXT_PUBLIC_SUPABASE_ANON_KEY=eyJhbGci...your-anon-key
   OMNIPREP_API_URL=http://api:8080/api
   ```

3. Khởi chạy toàn bộ hệ thống bằng Docker Compose:
   ```bash
   docker compose up -d --build
   ```
   Hệ thống sẽ chạy:
   - `api`: Cổng `5000` (Map sang 8080 container).
   - `worker`: Chạy ngầm xử lý hàng đợi nạp đề.
   - `frontend`: Cổng `3000`.

---

### Lựa chọn B: Triển khai trên Cloud PaaS (Google Cloud Run / Render)

#### Triển khai Backend API (.NET 10 Web API):
- **Base image**: Dockerfile [Dockerfile.api](file:///d:/OmniPrep/Dockerfile.api).
- **Port**: `8080`.
- **Environment Variables**:
  - `ConnectionStrings__Supabase`: Chuỗi kết nối Supabase PostgreSQL.
  - `Supabase__Url`: `https://<project-id>.supabase.co`.
  - `Gemini__ApiKey`: Khóa API Google AI Studio.
  - `Cors__AllowedOrigins`: `https://prep12.vn`.
  - `Frontend__Url`: `https://prep12.vn`.
  - `ASPNETCORE_ENVIRONMENT`: `Production`.

#### Triển khai Ingestion Worker (.NET 10 Background Service):
- **Base image**: Dockerfile [Dockerfile.worker](file:///d:/OmniPrep/Dockerfile.worker).
- **Loại hình**: Background Service Daemon (Không mở port HTTP).
- **Environment Variables**:
  - `ConnectionStrings__Supabase`: Chuỗi kết nối Supabase PostgreSQL.
  - `Gemini__ApiKey`: Khóa API Google AI Studio.
  - `DOTNET_ENVIRONMENT`: `Production`.

---

## 🌐 6. BƯỚC 4: TRIỂN KHAI FRONTEND (NEXT.JS 16) LÊN VERCEL

1. Truy cập [vercel.com](https://vercel.com) và kết nối với GitHub repository `OmniPrep`.
2. Trong phần cài đặt dự án (**Project Settings**):
   - **Root Directory**: Chọn `frontend`.
   - **Framework Preset**: `Next.js`.
3. Khai báo **Environment Variables**:
   - `NEXT_PUBLIC_SUPABASE_URL`: `https://<your-project-id>.supabase.co`
   - `NEXT_PUBLIC_SUPABASE_ANON_KEY`: `<your-supabase-anon-key>`
   - `OMNIPREP_API_URL`: `https://api.prep12.vn/api` (Địa chỉ public của Backend API).
4. Bấm **Deploy**. Vercel sẽ tự động build và cấp chứng chỉ SSL miễn phí.

---

## 🧪 7. BƯỚC 5: KIỂM TRA VẬN HÀNH (GO-LIVE VERIFICATION CHECKLIST)

Sau khi hoàn tất triển khai, thực hiện các bước kiểm tra thực tế:

- [ ] **1. Kiểm tra trạng thái hệ thống**:
  Truy cập `https://api.prep12.vn/api/health` -> Phải trả về:
  ```json
  {
    "status": "Healthy",
    "database": { "connected": true },
    "gemini": { "configured": true, "model": "gemini-2.0-flash" }
  }
  ```
- [ ] **2. Kiểm tra Đăng nhập & RBAC**:
  - Đăng ký một tài khoản học sinh -> Đăng nhập thành công.
  - Đăng nhập tài khoản có quyền `ADMIN` -> Kiểm tra quyền truy cập trang `/admin`.
- [ ] **3. Kiểm tra Nạp đề thi (Ingestion Pipeline)**:
  - Trên trang `/admin`, chọn nạp tệp văn bản `.txt` đề thi môn Địa lý.
  - Kiểm tra log của `OmniPrep.Worker`: Worker tự động claim job, bóc tách câu hỏi qua Gemini, lọc trùng SHA-256 / vector và lưu câu hỏi vào hàng chờ duyệt.
- [ ] **4. Kiểm tra Kiểm duyệt câu hỏi**:
  - Trên tab Hàng chờ duyệt (`/admin`), duyệt các câu hỏi đạt chuẩn.
- [ ] **5. Kiểm tra Phòng thi 50 phút**:
  - Vào trang chủ `/` -> chọn môn Địa lý -> Bấm **Vào phòng thi 50 phút**.
  - Kiểm tra đồng hồ đếm ngược, thanh Palette câu hỏi, Autosave tiến độ khi chọn đáp án.
  - Bấm **Nộp bài** -> Kiểm tra bảng kết quả chấm điểm 3 phần theo chuẩn Bộ GD&ĐT 2025.
- [ ] **6. Kiểm tra Gia sư AI Streaming**:
  - Mở câu hỏi bị sai -> Bấm **Hỏi gia sư AI Prep12**.
  - Phản hồi giải thích chi tiết phải được stream theo thời gian thực (SSE).
- [ ] **7. Kiểm tra Sổ tay câu sai & Dashboard**:
  - Vào `/mistakes` -> Câu hỏi làm sai vừa nộp phải tự động xuất hiện.
  - Vào `/dashboard` -> Kiểm tra Heatmap và Chuỗi ngày học (Streak).

---

## 🛡️ 8. GIÁM SÁT & QUẢN LÝ CHI PHÍ PRODUCTION

1. **Hạn mức Google Gemini API**:
   - Thiết lập Budget Alert trong Google Cloud Console để kiểm soát chi phí gọi `gemini-2.0-flash` và `text-embedding-004`.
   - Backend API đã tích hợp Rate Limiter 10 requests/phút/user cho tính năng AI Tutor nhằm chống spam và cạn quota.
2. **Sao lưu Cơ sở dữ liệu (Database Backup)**:
   - Supabase tự động sao lưu hàng ngày (Daily Backup).
   - Đối với dữ liệu lớn, có thể thiết lập lệnh backup tự động bằng `pg_dump` định kỳ:
     ```bash
     pg_dump -d "your_connection_string" -F c -b -v -f omniprep_backup_$(date +%F).dump
     ```
3. **Giám sát Log**:
   - Xem log API và Worker qua Docker:
     ```bash
     docker logs -f omniprep-api
     docker logs -f omniprep-worker
     ```
