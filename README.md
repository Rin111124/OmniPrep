# OmniPrep (Prep12.vn)

Nền tảng ôn luyện thi tốt nghiệp THPT đa môn, gồm frontend Next.js, API ASP.NET Core, Supabase PostgreSQL/Auth, worker nhập đề và tích hợp Gemini.

> **Trạng thái:** MVP kỹ thuật. Chưa xác nhận triển khai production. Chỉ bật môn có chiến lược chấm tương ứng và ngân hàng câu hỏi đã được người có chuyên môn duyệt.

## Chức năng hiện có

- Đăng ký/đăng nhập Supabase; API xác thực access token qua OIDC/JWKS. Role đặc quyền được cấp qua metadata do máy chủ quản lý.
- Tạo attempt, autosave/khôi phục đáp án, nộp bài và xem kết quả qua API.
- Chiến lược chấm hiện được đăng ký cho Địa lý (`GEO`) và GDKT&PL (`GDKTPL`). Môn khác chưa sẵn sàng chỉ vì xuất hiện trong danh mục.
- Gia sư AI SSE khi Gemini được cấu hình.
- Admin nạp tài liệu `.txt`; worker trích xuất và đưa câu hỏi vào hàng chờ. Giáo viên/admin có thể chỉnh sửa, duyệt hoặc từ chối câu hỏi.
- Dashboard và sổ tay câu sai lấy dữ liệu từ API; cần xác nhận đầy đủ trên staging trước khi dùng làm căn cứ học tập.

## Cấu trúc

```text
database/                 PostgreSQL schema, migrations và seed
src/OmniPrep.Api/         ASP.NET Core API
src/OmniPrep.Worker/      Background ingestion worker
src/OmniPrep.Infrastructure/ EF Core, Supabase/PostgreSQL và Gemini
src/OmniPrep.Domain/      Entities, enums, interfaces và domain services
frontend/                 Next.js 16, React 19, TypeScript
tests/                    .NET automated tests
Dockerfile.*              Container images
docker-compose.yml        Local/VPS deployment
```

## Chạy local

### 1. Database

Tạo Supabase project dành cho development. Với database mới, rà soát rồi chạy `database/all_migrations.sql` một lần trong SQL Editor. Với database đã tồn tại, chỉ áp dụng migration còn thiếu theo thứ tự `001`–`010`; dừng worker khi áp dụng migration `007`. Xem [hướng dẫn triển khai](docs/deployment-guide.md) trước khi chạy migrations trên dữ liệu có giá trị.

### 2. API và worker

Đặt các biến sau trong process environment hoặc user secrets:

- `ConnectionStrings__Supabase`
- `Supabase__Url`
- `Gemini__ApiKey` (nếu dùng tutor/ingestion)
- `Cors__AllowedOrigins` và `Frontend__Url` theo domain local/staging

Cấu hình Supabase Auth dùng signing key bất đối xứng ES256/RS256 tương thích OIDC/JWKS. Không đưa JWT signing secret hoặc Gemini key vào frontend.

```powershell
dotnet run --project src/OmniPrep.Api/OmniPrep.Api.csproj
dotnet run --project src/OmniPrep.Worker/OmniPrep.Worker.csproj
```

API local mặc định lắng nghe ở `http://localhost:5000`.

### 3. Frontend

Đặt `NEXT_PUBLIC_SUPABASE_URL`, `NEXT_PUBLIC_SUPABASE_ANON_KEY` và `OMNIPREP_API_URL` trong môi trường frontend, rồi chạy:

```powershell
cd frontend
npm ci
npm run dev
```

Mở `http://localhost:3000`.

## Kiểm tra chất lượng

GitHub Actions workflow `.github/workflows/ci.yml` định nghĩa .NET restore/build/test, dependency audit, ESLint, TypeScript check và Next.js build. Chưa khẳng định workflow đã chạy xanh trên GitHub; kiểm tra trạng thái CI của đúng commit trước khi phát hành.

## Triển khai và production readiness

- [Lộ trình cải thiện và tiêu chí Go/No-Go](docs/production-readiness-plan.md)
- [Hướng dẫn triển khai staging/production](docs/deployment-guide.md)
- [Kế hoạch tìm nguồn và bóc tách đề trực tuyến](docs/online-exam-discovery-plan.md)
- [Lộ trình nâng độ tin cậy kỹ thuật](docs/engineering-readiness-roadmap.md)

Các điểm cần xác nhận trước khi mở beta: health/readiness, Supabase role/RLS, migrations, khôi phục bài thi khi mất mạng, chất lượng câu hỏi/đáp án, backup/restore, quota Gemini, quyền riêng tư và rollback. Endpoint xóa dữ liệu hiện xóa dữ liệu học tập trong ứng dụng nhưng không xóa người dùng Supabase Auth.
