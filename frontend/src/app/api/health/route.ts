import { NextResponse } from 'next/server';

export const runtime = 'nodejs';

export async function GET() {
  const apiBase = process.env.OMNIPREP_API_URL;
  if (!apiBase) return NextResponse.json({ status: 'Unavailable' }, { status: 503 });

  try {
    const response = await fetch(`${apiBase.replace(/\/$/, '')}/health/ready`, {
      cache: 'no-store',
      signal: AbortSignal.timeout(5000)
    });
    const health = await response.json().catch(() => null);
    if (!health) {
      return NextResponse.json({ status: 'Unavailable' }, { status: 503 });
    }
    return NextResponse.json({
      status: health.status,
      timestamp: health.timestamp,
      database: { connected: Boolean(health.database?.connected) },
      gemini: {
        configured: Boolean(health.gemini?.configured),
        model: health.gemini?.model,
        embeddingModel: health.gemini?.embeddingModel
      }
    }, {
      status: response.status,
      headers: { 'Cache-Control': 'no-store' }
    });
  } catch {
    return NextResponse.json({ status: 'Unavailable' }, { status: 503 });
  }
}
