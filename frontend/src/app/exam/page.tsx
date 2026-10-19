'use client';

import { Suspense, useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Loader2 } from 'lucide-react';

function ExamRedirect() {
  const router = useRouter();
  const searchParams = useSearchParams();

  useEffect(() => {
    const q = searchParams.toString();
    const dest = q ? `/exam/exam-bgd-2025?${q}` : `/exam/exam-bgd-2025`;
    router.replace(dest);
  }, [router, searchParams]);

  return (
    <div className="min-h-screen flex flex-col items-center justify-center bg-[#07090e] text-white">
      <Loader2 className="w-8 h-8 text-indigo-400 animate-spin mb-3" />
      <p className="text-xs text-slate-400">Đang khởi tạo phòng thi ảo...</p>
    </div>
  );
}

export default function ExamIndexPage() {
  return (
    <Suspense
      fallback={
        <div className="min-h-screen flex items-center justify-center bg-[#07090e] text-white">
          <Loader2 className="w-8 h-8 text-indigo-400 animate-spin" />
        </div>
      }
    >
      <ExamRedirect />
    </Suspense>
  );
}
