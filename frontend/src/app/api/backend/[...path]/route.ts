import { createServerClient } from '@supabase/ssr';
import { cookies } from 'next/headers';
import { NextRequest, NextResponse } from 'next/server';

export const runtime = 'nodejs';

async function forward(request: NextRequest, path: string[]) {
  const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
  const anonKey = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;
  const apiBase = process.env.OMNIPREP_API_URL;
  if (!supabaseUrl || !anonKey || !apiBase) {
    return NextResponse.json({ message: 'Authentication or backend is not configured.' }, { status: 503 });
  }
  if (path.some(segment => segment === '.' || segment === '..')) {
    return NextResponse.json({ message: 'Invalid API path.' }, { status: 400 });
  }
  const apiPath = path.join('/');
  const allowed = ['subjects', 'exams', 'attempts', 'dashboard', 'users/me', 'mistakes', 'admin/ingest', 'admin/questions', 'exam-sources'];
  if (!allowed.some(prefix => apiPath === prefix || apiPath.startsWith(`${prefix}/`))) {
    return NextResponse.json({ message: 'API route is not available through this proxy.' }, { status: 404 });
  }
  if (!['GET', 'HEAD'].includes(request.method)) {
    const origin = request.headers.get('origin');
    if (origin !== request.nextUrl.origin) {
      return NextResponse.json({ message: 'Cross-origin requests are not allowed.' }, { status: 403 });
    }
  }

  const publicCatalogRead = request.method === 'GET' && (apiPath === 'subjects' || apiPath === 'exam-sources' || apiPath.startsWith('exam-sources/'));
  let accessToken: string | undefined;
  if (!publicCatalogRead) {
    const cookieStore = await cookies();
    const supabase = createServerClient(supabaseUrl, anonKey, {
      cookies: {
        getAll: () => cookieStore.getAll(),
        setAll: (updates) => updates.forEach(({ name, value, options }) => cookieStore.set(name, value, options))
      }
    });

    const { data: { user }, error: userError } = await supabase.auth.getUser();
    if (userError || !user) {
      return NextResponse.json({ message: 'Sign in is required.' }, { status: 401 });
    }

    // Defense-in-depth: Verify role at proxy layer for admin endpoints
    if (apiPath.startsWith('admin/')) {
      const userRole = (user.app_metadata?.role || user.user_metadata?.role || '').toString().toUpperCase();
      if (userRole !== 'ADMIN' && userRole !== 'TEACHER') {
        return NextResponse.json({ message: 'Access denied. Administrator privileges required.' }, { status: 403 });
      }
    }

    const { data: { session } } = await supabase.auth.getSession();
    accessToken = session?.access_token;
    if (!accessToken) {
      return NextResponse.json({ message: 'Your session has expired. Sign in again.' }, { status: 401 });
    }
  }

  const target = new URL(`${apiBase.replace(/\/$/, '')}/${path.map(encodeURIComponent).join('/')}`);
  target.search = request.nextUrl.search;

  const headers = new Headers();
  const contentType = request.headers.get('content-type');
  if (contentType) headers.set('content-type', contentType);
  if (accessToken) headers.set('authorization', `Bearer ${accessToken}`);

  let body: ArrayBuffer | undefined;
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    body = await request.arrayBuffer();
    const maxBodyBytes = 4.2 * 1024 * 1024;
    if (body.byteLength > maxBodyBytes) {
      return NextResponse.json({
        message: 'Dung lượng dữ liệu (vượt quá 4.2 MB) chạm ngưỡng giới hạn của hạ tầng đám mây Vercel Serverless. Hệ thống hỗ trợ tối ưu ảnh tự động, vui lòng nén hoặc giảm bớt số trang ảnh trước khi tải lên.'
      }, { status: 413 });
    }
  }

  let upstream: Response;
  try {
    upstream = await fetch(target, {
      method: request.method,
      headers,
      body,
      cache: 'no-store',
      signal: request.signal
    });
  } catch (error) {
    const errorMsg = error instanceof Error ? error.message : 'Connection failed';
    return NextResponse.json(
      {
        message: `Không thể kết nối đến backend (.NET API tại ${target.origin}): ${errorMsg}. Vui lòng kiểm tra backend service đã được khởi động chưa.`
      },
      { status: 502 }
    );
  }

  const responseHeaders = new Headers();
  for (const name of ['content-type', 'cache-control', 'retry-after', 'location']) {
    const value = upstream.headers.get(name);
    if (value) responseHeaders.set(name, value);
  }
  responseHeaders.set('cache-control', 'no-store');
  // Essential HTTP Security Headers
  responseHeaders.set('X-Content-Type-Options', 'nosniff');
  responseHeaders.set('X-Frame-Options', 'DENY');
  responseHeaders.set('Referrer-Policy', 'strict-origin-when-cross-origin');
  responseHeaders.set('Permissions-Policy', 'camera=(), microphone=(), geolocation=()');
  responseHeaders.set('X-XSS-Protection', '1; mode=block');

  const responseBody = [204, 205, 304].includes(upstream.status) ? null : upstream.body;
  return new NextResponse(responseBody, { status: upstream.status, headers: responseHeaders });
}

type RouteParams = { params: Promise<{ path: string[] }> };

export async function GET(request: NextRequest, { params }: RouteParams) {
  return forward(request, (await params).path);
}

export async function POST(request: NextRequest, { params }: RouteParams) {
  return forward(request, (await params).path);
}

export async function PUT(request: NextRequest, { params }: RouteParams) {
  return forward(request, (await params).path);
}

export async function PATCH(request: NextRequest, { params }: RouteParams) {
  return forward(request, (await params).path);
}

export async function DELETE(request: NextRequest, { params }: RouteParams) {
  return forward(request, (await params).path);
}
