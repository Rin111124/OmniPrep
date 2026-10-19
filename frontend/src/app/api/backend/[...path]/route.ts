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
  const allowed = ['subjects', 'exams', 'attempts', 'dashboard', 'users/me', 'mistakes', 'admin/ingest', 'admin/questions'];
  if (!allowed.some(prefix => apiPath === prefix || apiPath.startsWith(`${prefix}/`))) {
    return NextResponse.json({ message: 'API route is not available through this proxy.' }, { status: 404 });
  }
  if (!['GET', 'HEAD'].includes(request.method)) {
    const origin = request.headers.get('origin');
    if (origin !== request.nextUrl.origin) {
      return NextResponse.json({ message: 'Cross-origin requests are not allowed.' }, { status: 403 });
    }
  }

  const publicCatalogRead = request.method === 'GET' && apiPath === 'subjects';
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
    if (body.byteLength > 4 * 1024 * 1024) {
      return NextResponse.json({ message: 'Request is too large.' }, { status: 413 });
    }
  }

  const upstream = await fetch(target, {
    method: request.method,
    headers,
    body,
    cache: 'no-store',
    signal: request.signal
  });

  const responseHeaders = new Headers();
  for (const name of ['content-type', 'cache-control', 'retry-after', 'location']) {
    const value = upstream.headers.get(name);
    if (value) responseHeaders.set(name, value);
  }
  responseHeaders.set('cache-control', 'no-store');
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
