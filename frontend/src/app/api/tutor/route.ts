import { createServerClient } from '@supabase/ssr';
import { cookies } from 'next/headers';
import { NextRequest, NextResponse } from 'next/server';

export const runtime = 'nodejs';

const MAX_TUTOR_BODY_BYTES = 32_000;

async function readBodyLimited(request: NextRequest): Promise<
  { kind: 'empty' } | { kind: 'too-large' } | { kind: 'ok'; body: Uint8Array }
> {
  if (!request.body) return { kind: 'empty' };
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let byteLength = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      byteLength += value.byteLength;
      if (byteLength > MAX_TUTOR_BODY_BYTES) {
        await reader.cancel();
        return { kind: 'too-large' };
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  if (!byteLength) return { kind: 'empty' };
  const body = new Uint8Array(byteLength);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return { kind: 'ok', body };
}

export async function POST(request: NextRequest) {
  try {
    if (request.headers.get('origin') !== request.nextUrl.origin) {
      return NextResponse.json({ error: 'CROSS_ORIGIN_REQUEST' }, { status: 403 });
    }

    const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
    const anonKey = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;
    const apiBase = process.env.OMNIPREP_API_URL;
    if (!supabaseUrl || !anonKey || !apiBase) {
      return NextResponse.json({ error: 'TUTOR_NOT_CONFIGURED' }, { status: 503 });
    }

    const cookieStore = await cookies();
    const supabase = createServerClient(supabaseUrl, anonKey, {
      cookies: {
        getAll: () => cookieStore.getAll(),
        setAll: updates => updates.forEach(({ name, value, options }) => cookieStore.set(name, value, options))
      }
    });
    const { data: { user }, error: authError } = await supabase.auth.getUser();
    if (authError || !user) return NextResponse.json({ error: 'UNAUTHORIZED' }, { status: 401 });

    const { data: { session } } = await supabase.auth.getSession();
    if (!session?.access_token) return NextResponse.json({ error: 'UNAUTHORIZED' }, { status: 401 });

    const contentLength = Number(request.headers.get('content-length'));
    if (Number.isFinite(contentLength) && contentLength > MAX_TUTOR_BODY_BYTES) {
      return NextResponse.json({ error: 'CONTEXT_TOO_LARGE' }, { status: 413 });
    }
    const body = await readBodyLimited(request);
    if (body.kind === 'empty') return NextResponse.json({ error: 'INVALID_REQUEST' }, { status: 400 });
    if (body.kind === 'too-large') {
      return NextResponse.json({ error: 'CONTEXT_TOO_LARGE' }, { status: 413 });
    }
    const forwardedBody = new ArrayBuffer(body.body.byteLength);
    new Uint8Array(forwardedBody).set(body.body);

    const target = new URL(`${apiBase.replace(/\/$/, '')}/tutor/chat-stream`);
    const upstream = await fetch(target, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${session.access_token}`,
        'Content-Type': 'application/json',
        Accept: 'text/event-stream'
      },
      body: forwardedBody,
      cache: 'no-store',
      signal: request.signal
    });

    const headers = new Headers();
    for (const name of ['content-type', 'cache-control', 'retry-after']) {
      const value = upstream.headers.get(name);
      if (value) headers.set(name, value);
    }
    headers.set('Cache-Control', 'no-cache, no-transform');
    headers.set('X-Accel-Buffering', 'no');
    return new NextResponse(upstream.body, { status: upstream.status, headers });
  } catch {
    return NextResponse.json({ error: 'AI tutor is temporarily unavailable.' }, { status: 503 });
  }
}
