'use client';

import React, { Suspense, useEffect, useState, use } from 'react';
import { useRouter } from 'next/navigation';
import { Navbar } from '@/components/Navbar';
import { GradingSummary } from '@/components/GradingSummary';
import { AiTutorDrawer } from '@/components/AiTutorDrawer';
import { ExamGradingResult, Question, StudentAnswer } from '@/types';
import { Loader2, ArrowLeft, RotateCcw, BarChart2 } from 'lucide-react';
import Link from 'next/link';
import { useAuth } from '@/context/AuthContext';
import { examStorageKey, readExamStorage, removeExamStorage } from '@/lib/examStorage';

interface ResultPageProps {
  params: Promise<{ id: string }>;
}

function ExamResultContent({ examId }: { examId: string }) {
  const { user, isLoading: authLoading } = useAuth();
  const router = useRouter();
  const [result, setResult] = useState<ExamGradingResult | null>(null);
  const [questions, setQuestions] = useState<Question[]>([]);
  const [flaggedIds, setFlaggedIds] = useState<Set<string>>(new Set());
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [loadError, setLoadError] = useState<string>('');

  // AI Tutor Drawer State
  const [isTutorOpen, setIsTutorOpen] = useState<boolean>(false);
  const [tutorQuestion, setTutorQuestion] = useState<Question | null>(null);
  const [tutorStudentAnswer, setTutorStudentAnswer] = useState<StudentAnswer | undefined>(undefined);

  useEffect(() => {
    async function loadResult() {
      if (authLoading) return;
      setIsLoading(true);
      setLoadError('');
      setResult(null);
      setQuestions([]);
      setFlaggedIds(new Set());

      if (!user.isAuthenticated) {
        setLoadError('Sign in to view this exam result.');
        setIsLoading(false);
        return;
      }

      // Check localStorage for result from the exam submission
      const storedResult = readExamStorage(examStorageKey(user.id, 'result', examId));
      const storedQuestions = readExamStorage(examStorageKey(user.id, 'questions', examId));
      const storedFlagged = readExamStorage(examStorageKey(user.id, 'flagged', examId));

      if (storedResult && storedQuestions) {
        try {
          const parsedRes = JSON.parse(storedResult) as ExamGradingResult;
          const parsedQs = JSON.parse(storedQuestions) as Question[];
          setResult(parsedRes);
          setQuestions(parsedQs);
          if (storedFlagged) {
            setFlaggedIds(new Set(JSON.parse(storedFlagged)));
          }
          setIsLoading(false);
          return;
        } catch {
          setLoadError('Không đọc được kết quả đã lưu trên thiết bị này.');
        }
      } else {
        setLoadError('Không tìm thấy kết quả trên thiết bị này. Kết quả hiện chưa có màn hình tải lại từ máy chủ.');
      }
      setIsLoading(false);
    }

    loadResult();
  }, [examId, authLoading, user.id, user.isAuthenticated]);

  const handleOpenAiTutor = (q: Question, ans?: StudentAnswer) => {
    setTutorQuestion(q);
    setTutorStudentAnswer(ans);
    setIsTutorOpen(true);
  };

  const handleRetryExam = () => {
    // Clear previous timer and answers
    removeExamStorage(examStorageKey(user.id, 'timer', examId));
    removeExamStorage(examStorageKey(user.id, 'answers', examId));
    router.push(`/exam/${examId}`);
  };

  if (isLoading) {
    return (
      <div className="min-h-screen flex flex-col bg-[#07090e]">
        <Navbar />
        <div className="flex-1 flex flex-col items-center justify-center text-center p-6 space-y-4">
          <Loader2 className="w-10 h-10 text-indigo-400 animate-spin" />
          <h2 className="text-lg font-bold text-white">Đang tổng hợp báo cáo phân tích lỗi sai...</h2>
          <p className="text-xs text-slate-400 max-w-sm">
            Hệ thống đang đối chiếu ma trận 3 phần, kích hoạt công cụ bắt bệnh bẫy đề thi và tính điểm lũy tiến.
          </p>
        </div>
      </div>
    );
  }

  if (loadError || !result) {
    return (
      <div className="min-h-screen flex flex-col bg-[#07090e] text-white">
        <Navbar />
        <main className="mx-auto mt-16 max-w-xl rounded-3xl border border-amber-400/20 bg-slate-950/80 p-8 text-center">
          <h1 className="text-xl font-bold">Kết quả chưa khả dụng</h1>
          <p role="alert" className="mt-3 text-sm text-slate-300">{loadError || 'Không có kết quả để hiển thị.'}</p>
          <Link href="/" className="mt-5 inline-flex rounded-xl bg-indigo-600 px-5 py-2.5 text-sm font-bold">Về trang chủ</Link>
        </main>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex flex-col bg-[#07090e]">
      <Navbar />

      {/* Result Subheader */}
      <div className="glass-panel border-b border-white/10 px-4 sm:px-8 py-3 sticky top-16 z-30 backdrop-blur-xl">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          <div className="flex items-center gap-3">
            <Link
              href="/dashboard"
              className="w-8 h-8 rounded-xl bg-white/5 border border-white/10 flex items-center justify-center text-slate-400 hover:text-white hover:bg-white/10 transition-colors"
            >
              <ArrowLeft className="w-4 h-4" />
            </Link>
            <div>
              <h1 className="text-sm sm:text-base font-bold text-white flex items-center gap-2">
                <span>Kết quả & Phân tích lỗi sai: {examId}</span>
                <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-indigo-500/20 text-indigo-300 border border-indigo-500/30">
                  Đã chấm điểm
                </span>
              </h1>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={handleRetryExam}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold bg-white/5 border border-white/10 text-slate-300 hover:bg-white/10 transition-colors"
            >
              <RotateCcw className="w-3.5 h-3.5" />
              <span>Thi lại</span>
            </button>
            <Link
              href="/dashboard"
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-bold bg-indigo-600 hover:bg-indigo-500 text-white shadow-md shadow-indigo-600/30 transition-all"
            >
              <BarChart2 className="w-3.5 h-3.5" />
              <span>Dashboard</span>
            </Link>
          </div>
        </div>
      </div>

      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <GradingSummary
          result={result}
          questions={questions}
          flaggedQuestionIds={flaggedIds}
          onRetry={handleRetryExam}
          onOpenAiTutor={handleOpenAiTutor}
        />
      </main>

      {/* AI Tutor Drawer (Context-Aware) */}
      {tutorQuestion && (
        <AiTutorDrawer
          isOpen={isTutorOpen}
          onClose={() => setIsTutorOpen(false)}
          question={tutorQuestion}
          studentAnswer={tutorStudentAnswer}
        />
      )}
    </div>
  );
}

function ResultWrapper({ params }: { params: Promise<{ id: string }> }) {
  const unwrappedParams = use(params);
  return <ExamResultContent examId={unwrappedParams.id} />;
}

export default function ExamResultPage(props: ResultPageProps) {
  return (
    <Suspense
      fallback={
        <div className="min-h-screen flex items-center justify-center bg-[#07090e] text-white">
          <Loader2 className="w-8 h-8 text-indigo-400 animate-spin" />
        </div>
      }
    >
      <ResultWrapper params={props.params} />
    </Suspense>
  );
}
