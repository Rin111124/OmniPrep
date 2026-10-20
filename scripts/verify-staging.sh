#!/usr/bin/env bash
# ============================================================
# OmniPrep - Automated Staging Verification Script
# ============================================================

set -euo pipefail

API_URL="${API_URL:-http://localhost:5000/api}"
SUPABASE_URL="${SUPABASE_URL:-}"
ANON_KEY="${ANON_KEY:-}"
FRONTEND_URL="${FRONTEND_URL:-http://localhost:3000}"

echo "=========================================================="
echo "🧪 OMNIPREP STAGING READINESS VERIFICATION"
echo "=========================================================="

FAILED=0

# 1. API Liveness
echo -e "\n1. Checking API Liveness (/health/live)..."
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$API_URL/health/live" || echo "000")
if [ "$HTTP_CODE" = "200" ]; then
    echo "  [PASS] API Liveness probe returns 200 OK"
else
    echo "  [FAIL] API Liveness probe failed (HTTP $HTTP_CODE)"
    FAILED=$((FAILED + 1))
fi

# 2. API Readiness
echo -e "\n2. Checking API Readiness (/health/ready)..."
HTTP_CODE=$(curl -s -o /tmp/health_ready.json -w "%{http_code}" "$API_URL/health/ready" || echo "000")
if [ "$HTTP_CODE" = "200" ]; then
    echo "  [PASS] API Readiness probe returns 200 OK (DB & Gemini healthy)"
else
    echo "  [WARN/FAIL] API Readiness probe returned HTTP $HTTP_CODE"
    cat /tmp/health_ready.json || true
    echo ""
    FAILED=$((FAILED + 1))
fi

# 3. PostgREST RLS Check
echo -e "\n3. Checking Supabase PostgREST Questions RLS..."
if [ -n "$SUPABASE_URL" ] && [ -n "$ANON_KEY" ]; then
    PG_CODE=$(curl -s -o /tmp/pg_questions.json -w "%{http_code}" \
        -H "apikey: $ANON_KEY" \
        -H "Authorization: Bearer $ANON_KEY" \
        "$SUPABASE_URL/rest/v1/questions?select=id,answer_data,explanation&limit=5" || echo "000")
    
    CONTENT=$(cat /tmp/pg_questions.json 2>/dev/null || echo "")
    if [ "$PG_CODE" != "200" ] || [ "$CONTENT" = "[]" ] || [ -z "$CONTENT" ]; then
        echo "  [PASS] PostgREST blocked or returned empty list for anon key"
    else
        if grep -q "answer_data" /tmp/pg_questions.json; then
            echo "  [FAIL] Security alert: answer_data is leaked to anon token via PostgREST!"
            FAILED=$((FAILED + 1))
        else
            echo "  [PASS] No answers leaked in PostgREST response"
        fi
    fi
else
    echo "  [SKIP] SUPABASE_URL and ANON_KEY not provided"
fi

# 4. Frontend BFF Health
echo -e "\n4. Checking Frontend /api/health forwarding..."
FE_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$FRONTEND_URL/api/health" || echo "000")
if [ "$FE_CODE" = "200" ] || [ "$FE_CODE" = "503" ]; then
    echo "  [PASS] Frontend BFF health route responds (HTTP $FE_CODE)"
else
    echo "  [FAIL] Frontend BFF health route unreachable (HTTP $FE_CODE)"
    FAILED=$((FAILED + 1))
fi

echo "=========================================================="
if [ "$FAILED" -eq 0 ]; then
    echo "🎉 ALL STAGING READINESS CHECKS PASSED!"
    exit 0
else
    echo "❌ $FAILED CHECKS FAILED. See details above."
    exit 1
fi
