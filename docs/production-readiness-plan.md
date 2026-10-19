# OmniPrep: lộ trình đưa vào vận hành

## Đánh giá hiện tại

OmniPrep hiện ở mức **MVP kỹ thuật, chưa sẵn sàng mở cho học sinh sử dụng thật**. Đã có luồng xác thực Supabase ở frontend, proxy server-side sang API .NET, tạo/nộp bài theo attempt lưu trong backend và dữ liệu tiến độ lấy từ API. Tuy nhiên chưa có staging được cấu hình để xác nhận toàn luồng, ngân hàng câu hỏi đủ chất lượng, hay quy trình vận hành và bảo vệ dữ liệu hoàn chỉnh. Có giao diện duyệt/từ chối cơ bản nhưng chưa có chỉnh sửa câu, OCR hoặc xuất bản.

Các thay đổi trong lượt này:

- Thêm đăng ký/đăng nhập/đăng xuất Supabase, callback xác nhận email và lấy vai trò từ `app_metadata` do máy chủ quản lý.
- Thêm BFF `/api/backend/*`: yêu cầu session, chuyển tiếp JWT, giới hạn request và chỉ cho phép các nhóm API đã chọn.
- Nối luồng thi và bảng tiến độ với API backend; bỏ chấm điểm giả khi request thất bại.
- Trang chủ tải môn học từ backend thay vì danh sách môn mẫu; sổ tay lỗi không tự tạo dữ liệu mẫu.
- Thêm migration `005_trusted_profile_roles.sql` để trigger đăng ký không nhận vai trò từ metadata do người dùng gửi.
- Cập nhật biến môi trường backend URL thành `OMNIPREP_API_URL`.
- Thêm hàng chờ duyệt/từ chối câu hỏi cho giáo viên/admin; migration `008` lưu người duyệt/thời điểm/lý do. Chưa áp dụng migration này lên staging.
- Hợp nhất tutor qua BFF sang .NET để Gemini chỉ được gọi một lần; BFF giới hạn body 32 KB, API giới hạn request 3 MB và quota 10 request/phút/user. Rate limit còn trong từng API process.
- Catalog môn học giờ trả số câu đã duyệt theo format và trạng thái đủ điều kiện tạo đề; frontend/API chặn môn chưa có strategy hoặc chưa đủ câu theo ma trận.
- Thay workflow cron dùng DB/Gemini credentials bằng CI build/lint/typecheck/audit dependency chỉ đọc; gỡ lịch crawler chưa có nguồn web được duyệt.

## Các điểm chặn trước khi mở cho người dùng

1. **Danh tính và quyền:** xác nhận JWT Supabase thực tế có claim `app_metadata.role` cho quản trị viên/giáo viên; thiết lập quy trình cấp quyền tin cậy. Migration mới không tự xóa các profile đặc quyền hiện có. Kiểm tra lại mọi endpoint, nhất là dữ liệu dashboard, tutor và API quản trị.
2. **Ngân hàng câu hỏi:** có thể đưa tài liệu `.txt` vào hàng đợi; chưa cấu hình nguồn web được phê duyệt. Có hàng chờ giáo viên/admin, duyệt hoặc từ chối kèm lý do và lưu người duyệt/thời điểm ở migration `008`; chưa có OCR, sửa câu, audit đầy đủ lịch sử quyết định, xác minh đáp án tự động hay xuất bản. Không mở bài thi trước khi người phụ trách kiểm duyệt ngân hàng câu hỏi.
3. **Thi cử:** API đã có autosave/khôi phục câu trả lời theo attempt; staging chưa xác nhận mất mạng, request đồng thời, deadline, tính đúng đắn khi nộp lặp và resume trên thiết bị khác.
4. **Dữ liệu học tập:** sổ tay câu sai lưu localStorage theo namespace user ID nhưng chưa đồng bộ hoặc sao lưu từ server; dữ liệu trình duyệt vẫn có thể mất. Chuỗi ngày học và mục tiêu điểm chưa có nguồn dữ liệu thật.
5. **Bảo mật và vận hành:** tutor đi qua một đường BFF → .NET; API giới hạn 10 request/phút/user, BFF chặn body trên 32 KB và Kestrel giới hạn request 3 MB. Rate limit bị reset khi restart và không cộng dồn giữa nhiều replica; trước production cần quota theo ngày/chi phí dùng kho lưu trữ chia sẻ. Còn cần log không chứa dữ liệu nhạy cảm, health/readiness, backup/restore, cảnh báo, quản lý secrets, CORS/domain đúng môi trường, chính sách dữ liệu và rà soát dependency.
6. **Chất lượng:** typecheck, lint với `--max-warnings=0` và Next build đã chạy thành công, không còn cảnh báo ESLint. API và Worker build Release thành công. Đã thêm workflow CI restore/build .NET, audit NuGet/npm runtime, lint/typecheck/build frontend với quyền chỉ đọc; workflow chưa được chạy trên GitHub. Chưa có test suite. Catalog readiness cần được xác nhận trên dữ liệu staging và với người đã làm đúng một phần ngân hàng câu hỏi.

- Thay trang admin mẫu bằng form nhận `.txt`; bỏ fallback môn học giả, phản hồi tutor mẫu, nguồn web placeholder, API role mẫu và API pipeline demo.
- Chuyển xác minh JWT sang Supabase OIDC/JWKS (yêu cầu khóa ký bất đối xứng), ẩn chi tiết lỗi database khỏi health, thêm health proxy và yêu cầu Origin cùng nguồn cho request thay đổi dữ liệu qua cookie.
- Claim job ingestion nguyên tử bằng PostgreSQL `FOR UPDATE SKIP LOCKED`; sửa ánh xạ enum và nâng `Microsoft.OpenApi` lên `2.7.5`.
- Sửa thứ tự cú pháp `LIMIT ... FOR UPDATE SKIP LOCKED` trong câu claim PostgreSQL; lỗi Gemini lúc trích xuất được ném lại để worker áp dụng retry hữu hạn.
- Worker tiếp tục lấy job ngay sau khi xử lý thành công; chỉ chờ chu kỳ poll khi hàng đợi rỗng hoặc job lỗi, tránh trì hoãn/backoff không cần thiết. Scraper hiện chỉ đọc thư mục drop local, chưa có nguồn web được phê duyệt; đã gỡ workflow cron cấp credentials DB/Gemini.
- Dữ liệu bài thi hiện được autosave/khôi phục từ API theo attempt. Migration `006_attempt_answer_autosave.sql` bổ sung bảng draft answer có RLS.
- Sổ tay câu sai đã được đưa lên server với migration `009_user_mistakes_and_privacy.sql` (bảng `user_mistakes` có RLS), entity `UserMistake` trong EF Core, và endpoint `/api/mistakes` (get, sync, toggle-mastered, delete). Thư viện frontend `mistakes.ts` và trang `mistakes/page.tsx` đồng bộ hai chiều với máy chủ, fallback localStorage khi offline.
- Bổ sung các endpoint quyền riêng tư và dữ liệu cá nhân theo GDPR: `GET /api/users/me/export` (xuất toàn bộ hồ sơ, bài thi, mastery, câu sai dạng JSON) và `DELETE /api/users/me` (xóa toàn bộ lịch sử thi và học tập cá nhân).
- Cấu hình CORS linh hoạt theo danh sách domain môi trường (`Cors:AllowedOrigins` / `Frontend:Url`) thay vì hardcode.
- Đã thiết lập bộ test tự động .NET (`tests/OmniPrep.Tests` với 46 tests) bao phủ toàn diện: chấm điểm 3 phần theo quy chế Bộ GD&ĐT (trắc nghiệm, đúng/sai lũy tiến, trả lời ngắn dung sai & chuẩn hóa số), SubjectStrategyFactory, phân quyền JWT/Supabase metadata, và thuật toán tính chuỗi ngày học StreakCalculator.
- Bổ sung chức năng hiệu đính câu hỏi: endpoint `PUT /api/admin/questions/{id}`, `GET /api/admin/questions/stats`, và modal chỉnh sửa câu hỏi trên UI Admin cho phép giáo viên sửa lỗi trước khi xuất bản.
- Tính toán chuỗi ngày học thực tế (`StreakCalculator`) và tóm tắt tiến độ qua `GET /api/dashboard/summary`, hiển thị số liệu thật trên Dashboard.
- Tạo file `.gitignore` gốc chuẩn bị cho việc quản lý monorepo và chạy CI tự động.
- Xác minh: API/Worker/Tests build Release thành công; 46 unit tests đều PASS; `npx tsc --noEmit` và `next build` thành công; ESLint không lỗi/cảnh báo (`--max-warnings=0`).
## Kế hoạch triển khai theo giai đoạn

### Giai đoạn 0 — Cấu hình staging và nền tảng

- Tạo Supabase staging, áp dụng schema/migration theo thứ tự (`001`–`008`, áp dụng các migration bổ sung theo thứ tự cho database đã tồn tại).
- Cấu hình `NEXT_PUBLIC_SUPABASE_URL`, `NEXT_PUBLIC_SUPABASE_ANON_KEY`, `OMNIPREP_API_URL`; cấu hình connection string và `Supabase__Url`/`Supabase OIDC/JWKS (project cần ký ES256/RS256)` riêng cho API/worker; lưu Gemini key trong secret store.
- Cập nhật/đánh giá dependency có cảnh báo bảo mật, xác nhận lại cảnh báo dependency npm trước phát hành; `Microsoft.OpenApi` đã nâng lên `2.7.5`; không nâng phiên bản mù quáng.
- Đã tập trung rate limit tutor 10 request/phút/user ở API, giới hạn body BFF 32 KB và request API 3 MB; còn phải bổ sung quota chia sẻ có hạn mức ngày/ngân sách trước production. Hoàn thiện health/readiness, log có cấu trúc, CORS theo domain, backup và diễn tập khôi phục.

**Điều kiện hoàn tất:** migration chạy sạch trên staging; API/worker khởi động bằng secrets; health, backup và khôi phục được xác nhận.

### Giai đoạn 1 — Xác thực và phân quyền đầu cuối

- Cấu hình Supabase xác nhận email, đổi/đặt lại mật khẩu và redirect URL theo môi trường.
- Tạo tài khoản đặc quyền bằng quy trình admin; đảm bảo chỉ claim do máy chủ cấp mới tạo vai trò teacher/admin. Kiểm tra API với token student, teacher và admin.
- Rà soát quyền sở hữu ở tất cả truy vấn; thêm bảo vệ CSRF/Origin cho route nhận cookie; ghi nhận và xử lý tài khoản bị xóa/khóa.

**Điều kiện hoàn tất:** người dùng thường không thể nâng quyền hoặc xem/sửa dữ liệu người khác; luồng đăng nhập và hết phiên hoạt động ổn định.

### Giai đoạn 2 — Hoàn chỉnh vòng đời đề thi

- Đã thêm API autosave/khôi phục câu trả lời theo attempt bằng bảng `attempt_answers`; cần xác nhận retry, đồng bộ nhiều tab, hết giờ và submit đồng thời trên staging.
- Đối chiếu công thức chấm và đủ số câu theo ma trận cho từng môn; xác nhận kết quả trả về khớp dữ liệu đã lưu.
- Chạy luồng từ hai trình duyệt/thiết bị trên dữ liệu staging, bao gồm reload, mất mạng và hết hạn.

**Điều kiện hoàn tất:** bài làm có thể khôi phục từ server, được chấm đúng một lần và kết quả nhất quán trên các thiết bị.

### Giai đoạn 3 — Nạp và duyệt câu hỏi

- Cứng hóa pipeline trích xuất Gemini theo schema nghiêm ngặt; admin nhận tài liệu và đưa vào hàng đợi, giáo viên/admin duyệt hoặc từ chối. Chưa có OCR, chỉnh sửa câu hoặc xuất bản.
- Hoàn thiện chỉnh sửa câu và workflow xuất bản; thêm phân trang hàng đợi, lịch sử audit bất biến và bước kiểm tra đáp án/nguồn trước khi kích hoạt. Đã có quyết định duyệt/từ chối và metadata người duyệt/thời gian/lý do; migration `008` phải chạy trên staging.
- Bounded retries and stale-processing lease recovery are implemented in migration `007`; stop workers while applying it. Continue with ingestion idempotency, file retention, approved sources, and labeled data for deduplication tuning. Claim locking and heartbeat ownership across worker replicas are implemented. Removed the daily GitHub Actions job that supplied database/Gemini credentials; the worker scraper only reads a local drop folder and no approved remote sources are configured.

**Điều kiện hoàn tất:** chỉ câu đã duyệt và có nguồn rõ ràng mới được đưa vào đề; job lỗi có thể retry an toàn.

### Giai đoạn 4 — Dữ liệu học tập và quyền riêng tư

- Lưu sổ tay câu sai theo tài khoản trên server; bổ sung export/xóa tài khoản và thông báo quyền riêng tư.
- Lưu phiên tutor, giới hạn quota/thời gian/token theo user và IP; theo dõi lỗi/chi phí.
- Chỉ hiển thị streak, mục tiêu và thống kê khi chúng được tính từ dữ liệu thật.

**Điều kiện hoàn tất:** dữ liệu đồng bộ giữa thiết bị, có thể xuất/xóa, tutor có hạn mức và thống kê có thể giải thích.

### Giai đoạn 5 — Phát hành có kiểm soát

- Workflow CI đã cấu hình .NET restore/build, NuGet/npm runtime audit, lint, typecheck và frontend build; cần xác nhận run trên GitHub.
- Bổ sung kiểm tra tự động cho grading, quyền, migration, worker và luồng thi.
- Phát hành beta giới hạn sau khi staging đạt exit criteria; đặt ngưỡng tải, cảnh báo lỗi và quy trình rollback/hỗ trợ.
- Chỉ mở rộng sau khi có người phụ trách vận hành, nội dung câu hỏi, quyền riêng tư và khôi phục sự cố.

**Điều kiện phát hành:** checklist staging đạt, restore đã diễn tập, không còn blocker bảo mật hoặc lỗi build, có ngân hàng câu hỏi đủ chất lượng cho phạm vi môn công bố.

## Đầu vào cần chuẩn bị

- Supabase staging/production, redirect URL, asymmetric JWT signing key và quy trình cấp claim đặc quyền.
- Gemini key với quota/chi phí tối đa; hosting cho Next.js, API, worker và database.
- Nguồn đề có quyền sử dụng, người duyệt nội dung và bộ câu hỏi ban đầu.
- Chính sách lưu file/nội dung đề, dữ liệu học sinh, thời gian lưu và quy trình xóa.














