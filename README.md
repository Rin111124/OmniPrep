# OmniPrep (Prep12.vn) 🎓
### Nền tảng ôn luyện thi tốt nghiệp THPT đa môn

> Trạng thái hiện tại: MVP kỹ thuật; chưa xác nhận vận hành production. Chi phí và hạn mức phụ thuộc gói dịch vụ, cấu hình và mức sử dụng.

## Trạng thái các luồng chính

- Xác thực và vai trò dùng Supabase Auth; vai trò đặc quyền lấy từ metadata do máy chủ quản lý.
- Backend tạo attempt, autosave và khôi phục câu trả lời, chấm bài và lưu lịch sử. Chiến lược chấm hiện được đăng ký cho Địa lý và Giáo dục Kinh tế & Pháp luật; các môn khác cần được xác nhận.
- Gia sư AI truyền phản hồi qua SSE khi Gemini được cấu hình.
- Admin gửi đề `.txt` vào hàng đợi. Worker xử lý với SHA-256/pgvector deduplication, lease và retry hữu hạn; dữ liệu web crawl tự động chưa được cấu hình. Giáo viên/admin có hàng chờ duyệt, duyệt hoặc từ chối câu hỏi với lý do; OCR, chỉnh sửa câu và xuất bản chưa được triển khai.
- Trang chủ hiển thị số câu đã duyệt theo từng phần; chỉ bật tạo đề khi môn có chiến lược chấm và đủ câu theo ma trận. Dashboard và mastery lấy dữ liệu từ API nhưng cần kiểm chứng trên staging trước khi dùng làm căn cứ học tập.

Migration `006`–`008` cần được áp dụng trên staging; dừng worker khi chạy migration `007`. Đề thi cần được người có trách nhiệm kiểm duyệt trước khi mở cho học sinh. Workflow crawl chạy theo lịch đã được gỡ bỏ: worker hiện chỉ đọc thư mục local, chưa có nguồn web được phê duyệt.

## CI

Workflow `.github/workflows/ci.yml` chạy restore/build .NET, audit dependency NuGet và npm runtime, lint không cho phép warning, typecheck và build frontend. Workflow không cần secrets production. Repo hiện chưa có automated test suite; CI cần được xác nhận chạy trên GitHub sau khi nối repo.

---
```
OmniPrep/
├── database/
│   └── 001_init_schema.sql         # Supabase PostgreSQL schema + pgvector + HNSW index + RLS + Triggers
├── src/
│   ├── OmniPrep.Domain/            # Entities, ValueObjects, Enums, Interfaces (Clean Architecture)
│   ├── OmniPrep.Infrastructure/    # EF Core DbContext, pgvector, Gemini 2.0 Client, Ingestion & Grading
│   ├── OmniPrep.Api/               # ASP.NET Core Minimal APIs, SSE Streaming, Supabase JWT Auth
│   └── OmniPrep.Worker/            # Background Worker Service xử lý ingestion queue với lease và retry hữu hạn
├── frontend/                       # Next.js 16 + React 19 + TypeScript + Tailwind CSS v4
│   ├── src/app/                    # Pages: Trang chủ, Phòng thi 50p, Heatmap, Admin Ingestion
│   ├── src/components/             # Navbar, QuestionCard, QuestionPalette, ExamHeader, AiTutorDrawer
│   └── src/lib/                    # API client and BGD 2025 grading engine
└── README.md
```

---

## 🚀 HƯỚNG DẪN KHỞI CHẠY (LOCAL DEVELOPMENT)

### 1. Khởi tạo Cơ sở dữ liệu Supabase
1. Đăng ký tài khoản miễn phí tại [supabase.com](https://supabase.com).
2. Tạo dự án mới, vào mục **SQL Editor**.
3. Với database mới, áp dụng SQL migrations trong `database/` theo thứ tự `001` đến `008`; với database hiện có, áp dụng các migration bổ sung chưa chạy theo thứ tự.
4. Lấy connection string từ **Project Settings -> Database**; dùng OIDC/JWKS để xác minh token (không cần JWT secret trong API).

### 2. Backend configuration
Set `ConnectionStrings__Supabase`, `Supabase__Url`, and `Gemini__ApiKey` as process environment variables or deployment secrets. Configure the Supabase project to sign access tokens with an asymmetric key (ES256 or RS256); the API validates tokens from the project OIDC discovery/JWKS endpoint. Do not share a Supabase JWT signing secret with the API.

Run the API:
```powershell
dotnet run --project src/OmniPrep.Api/OmniPrep.Api.csproj
```

In a separate terminal, run the ingestion worker after applying migrations and configuring the same database/provider secrets:
```powershell
dotnet run --project src/OmniPrep.Worker/OmniPrep.Worker.csproj
```
Backend API sẽ lắng nghe tại `http://localhost:5000` (OpenAPI JSON tại `http://localhost:5000/openapi/v1.json`).

### 3. Chạy Frontend Next.js
Mở terminal tại thư mục `frontend`:
```powershell
cd frontend
npm run dev
```
Mở trình duyệt truy cập: **`http://localhost:3000`**

---

## 🎯 CÁC TRANG CHÍNH TRÊN GIAO DIỆN

- `/` — tải danh sách môn học từ API và chọn môn để bắt đầu.
- `/exam` — tạo hoặc tiếp tục attempt đã đăng nhập; câu trả lời được autosave theo attempt.
- `/exam/[id]/result` — hiển thị kết quả đã nộp trên trình duyệt hiện tại.
- `/dashboard` và `/mistakes` — xem dữ liệu mastery và sổ tay; sổ tay hiện lưu localStorage theo user ID.
- `/admin` — ADMIN gửi đề `.txt`; TEACHER/ADMIN duyệt hoặc từ chối câu hỏi; chưa có chỉnh sửa hay xuất bản.
---

## Trạng thái triển khai và quyền truy cập

OmniPrep hiện là MVP kỹ thuật. Tài khoản và quyền được xác thực qua Supabase; vai trò lấy từ `app_metadata` do máy chủ quản lý. Không có nút đổi vai trò thử nghiệm trên giao diện.

- Học sinh đã đăng nhập có thể xem môn học, tạo/nộp bài theo các API hiện có và dùng gia sư AI khi Gemini được cấu hình.
- `/admin` cho `ADMIN` và `TEACHER`; admin mới được nạp `.txt`, cả hai vai trò có thể duyệt hoặc từ chối câu hỏi.
- Giao diện duyệt/từ chối câu hỏi đã có, có ghi nhận người duyệt/thời gian/lý do; chỉnh sửa câu, OCR, xuất bản và quản lý người dùng chưa được triển khai. Không dùng dữ liệu câu hỏi chưa được người có trách nhiệm kiểm duyệt để tổ chức thi thật.
- Câu trả lời trong khi làm bài và sổ tay câu sai hiện lưu ở trình duyệt; chưa hỗ trợ tiếp tục bài trên thiết bị khác hoặc đồng bộ sổ tay theo tài khoản.
- Các quyền `TEACHER`, dashboard, thống kê và tutor cần được xác nhận bằng staging trước khi công bố phạm vi sử dụng.

Xem [lộ trình production readiness](docs/production-readiness-plan.md) để biết các điều kiện phát hành theo giai đoạn.
