import { NextResponse } from 'next/server';

export const runtime = 'nodejs';

export async function GET() {
  const apiBase = process.env.OMNIPREP_API_URL;
  if (!apiBase) return NextResponse.json({ status: 'Unavailable' }, { status: 503 });

  try {
    const response = await fetch(`${apiBase.replace(/\/$/, '')}/health`, {
      cache: 'no-store',
      signal: AbortSignal.timeout(5000)
    });
    if (!response.ok) return NextResponse.json({ status: 'Unavailable' }, { status: 503 });
    const health = await response.json();
    return NextResponse.json({
      status: health.status,
      timestamp: health.timestamp,
      database: { connected: Boolean(health.database?.connected) },
      gemini: {
        configured: Boolean(health.gemini?.configured),
        model: health.gemini?.model,
        embeddingModel: health.gemini?.embeddingModel
      }
    }, { headers: { 'Cache-Control': 'no-store' } });
  } catch {
    return NextResponse.json({ status: 'Unavailable' }, { status: 503 });
  }
}
