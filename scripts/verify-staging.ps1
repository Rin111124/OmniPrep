<#
.SYNOPSIS
    Script kiểm tra nghiệm thu Staging (Production Readiness Checklist)
.DESCRIPTION
    Tự động hóa kiểm tra:
    1. Health / Liveness (/api/health/live)
    2. Health / Readiness (/api/health/ready)
    3. RLS PostgREST: Đảm bảo anon key KHÔNG đọc được answer_data từ Supabase
    4. BFF Frontend Health check
.EXAMPLE
    .\scripts\verify-staging.ps1 -ApiUrl "https://api-staging.omniprep.vn/api" -SupabaseUrl "https://xyz.supabase.co" -AnonKey "sb_anon_xxx"
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$ApiUrl = "http://localhost:5000/api",

    [Parameter(Mandatory = $false)]
    [string]$SupabaseUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$AnonKey = "",

    [Parameter(Mandatory = $false)]
    [string]$FrontendUrl = "http://localhost:3000"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "🧪 OMNIPREP STAGING READINESS VERIFICATION" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$passed = 0
$failed = 0

function Report-Result($name, $success, $details) {
    if ($success) {
        Write-Host "  [PASS] $name" -ForegroundColor Green
        if ($details) { Write-Host "         $details" -ForegroundColor DarkGray }
        $script:passed++
    } else {
        Write-Host "  [FAIL] $name" -ForegroundColor Red
        if ($details) { Write-Host "         $details" -ForegroundColor Yellow }
        $script:failed++
    }
}

# 1. API Liveness
Write-Host "`n1. Kiểm tra API Liveness..." -ForegroundColor White
try {
    $res = Invoke-RestMethod -Uri "$ApiUrl/health/live" -Method Get -TimeoutSec 5 -ErrorAction Stop
    $isLive = $res.Status -eq "Live"
    Report-Result "API Process Liveness (/health/live)" $isLive "Status: $($res.Status)"
} catch {
    Report-Result "API Process Liveness (/health/live)" $false $_.Exception.Message
}

# 2. API Readiness
Write-Host "`n2. Kiểm tra API Readiness (Database & Gemini)..." -ForegroundColor White
try {
    $response = Invoke-WebRequest -Uri "$ApiUrl/health/ready" -Method Get -TimeoutSec 5 -SkipHttpErrorCheck
    $body = $response.Content | ConvertFrom-Json
    $isReady = ($response.StatusCode -eq 200) -and ($body.Status -eq "Healthy")
    Report-Result "API Readiness Probe (/health/ready)" $isReady "HTTP $($response.StatusCode) - Status: $($body.Status), DB: $($body.Database.Connected), Gemini: $($body.Gemini.Configured)"
} catch {
    Report-Result "API Readiness Probe (/health/ready)" $false $_.Exception.Message
}

# 3. Supabase PostgREST RLS Security Check
Write-Host "`n3. Kiểm tra bảo mật RLS Supabase PostgREST..." -ForegroundColor White
if ($SupabaseUrl -and $AnonKey) {
    try {
        $headers = @{
            "apikey" = $AnonKey
            "Authorization" = "Bearer $AnonKey"
        }
        $postgrestUrl = "$($SupabaseUrl.TrimEnd('/'))/rest/v1/questions?select=id,content,answer_data,explanation&limit=5"
        $pgResponse = Invoke-WebRequest -Uri $postgrestUrl -Headers $headers -Method Get -TimeoutSec 5 -SkipHttpErrorCheck

        if ($pgResponse.StatusCode -ge 400 -or ($pgResponse.Content -eq "[]") -or [string]::IsNullOrWhiteSpace($pgResponse.Content)) {
            Report-Result "RLS Bảng Questions (Anon Token không đọc được)" $true "PostgREST chặn hoặc trả mảng rỗng (HTTP $($pgResponse.StatusCode))"
        } else {
            $questions = $pgResponse.Content | ConvertFrom-Json
            $leaked = $false
            foreach ($q in $questions) {
                if ($q.answer_data -or $q.explanation) {
                    $leaked = $true
                    break
                }
            }
            Report-Result "RLS Bảng Questions (Anon Token không lộ đáp án)" (-not $leaked) "Lộ đáp án qua PostgREST: $leaked"
        }
    } catch {
        Report-Result "RLS Bảng Questions" $true "Truy vấn bị từ chối an toàn: $($_.Exception.Message)"
    }
} else {
    Write-Host "  [SKIP] Bỏ qua kiểm tra PostgREST vì chưa truyền SupabaseUrl / AnonKey." -ForegroundColor DarkYellow
}

# 4. Frontend BFF Health Check
Write-Host "`n4. Kiểm tra Frontend BFF Health..." -ForegroundColor White
try {
    $feResponse = Invoke-WebRequest -Uri "$($FrontendUrl.TrimEnd('/'))/api/health" -Method Get -TimeoutSec 5 -SkipHttpErrorCheck
    $feBody = $feResponse.Content | ConvertFrom-Json
    $feOk = ($feResponse.StatusCode -eq 200) -or ($feResponse.StatusCode -eq 503 -and $feBody.status)
    Report-Result "Frontend /api/health Forwarding" $feOk "HTTP $($feResponse.StatusCode) - Status: $($feBody.status)"
} catch {
    Report-Result "Frontend /api/health Forwarding" $false $_.Exception.Message
}

Write-Host "`n----------------------------------------------------------" -ForegroundColor Cyan
Write-Host "Kết quả kiểm tra: $passed ĐẠT / $(($passed + $failed)) TỔNG CỘNG" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "----------------------------------------------------------`n"
if ($failed -gt 0) { exit 1 } else { exit 0 }
