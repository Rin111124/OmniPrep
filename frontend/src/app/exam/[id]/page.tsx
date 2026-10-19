'use client';

import React, { Suspense, useEffect, useRef, useState, use } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { Navbar } from '@/components/Navbar';
import { QuestionCard } from '@/components/QuestionCard';
import { QuestionPalette } from '@/components/QuestionPalette';
import { AiTutorDrawer } from '@/components/AiTutorDrawer';
import { generateExam, getAttempt, saveAttemptAnswers, submitExam } from '@/lib/api';
import { saveMistakesFromResult } from '@/lib/mistakes';
import { examStorageKey, readExamStorage, removeExamStorage, writeExamStorage } from '@/lib/examStorage';
import { useAuth } from '@/context/AuthContext';
import { Question, StudentAnswer, SubjectCode } from '@/types';
import { ChevronLeft, ChevronRight, Loader2, BookMarked, Target, Clock, Send, AlertTriangle } from 'lucide-react';

interface ExamPageProps {
  params: Promise<{ id: string }>;
}

function VirtualExamRoom({ examIdParam }: { examIdParam: string }) {
  const { user, isLoading: authLoading } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();
  const attemptIdParam = searchParams.get('attemptId');
  const searchQuery = searchParams.toString();
  const subjectParam = (searchParams.get('subject') as SubjectCode) || 'GEOGRAPHY';
  const modeParam = searchParams.get('mode'); // 'mistakes' | 'drill' | null
  const topicParam = searchParams.get('topic');

  const subjectCode = subjectParam;
  const [examId, setExamId] = useState<string>(examIdParam);
  const [attemptId, setAttemptId] = useState<string>('');
  const [questions, setQuestions] = useState<Question[]>([]);
  const [currentIndex, setCurrentIndex] = useState<number>(0);
  const [answers, setAnswers] = useState<Map<string, StudentAnswer>>(new Map());
  const [flaggedIds, setFlaggedIds] = useState<Set<string>>(new Set());
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [showSubmitModal, setShowSubmitModal] = useState<boolean>(false);
  const [timeLeft, setTimeLeft] = useState<number>(50 * 60);
  const [durationSeconds, setDurationSeconds] = useState<number>(50 * 60);
  const [examError, setExamError] = useState<string>('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [hasLoadedAttempt, setHasLoadedAttempt] = useState(false);
  const [saveState, setSaveState] = useState<'saved' | 'saving' | 'error'>('saved');
  const submitHandlerRef = useRef<() => Promise<void>>(async () => undefined);
  const submittingRef = useRef(false);
  const saveQueueRef = useRef<Promise<void>>(Promise.resolve());
  const answersRef = useRef<StudentAnswer[]>([]);
  const timeLeftRef = useRef(timeLeft);

  // AI Tutor Drawer State
  const [isTutorOpen, setIsTutorOpen] = useState<boolean>(false);
  const [tutorQuestion, setTutorQuestion] = useState<Question | null>(null);
  const [tutorStudentAnswer, setTutorStudentAnswer] = useState<StudentAnswer | undefined>(undefined);

  useEffect(() => {
    answersRef.current = Array.from(answers.values());
    timeLeftRef.current = timeLeft;
  }, [answers, timeLeft]);

  // Initialize and load exam questions
  useEffect(() => {
    let active = true;
    async function initExam() {
      if (authLoading) return;
      if (!user.isAuthenticated) {
        setAttemptId('');
        setQuestions([]);
        setAnswers(new Map());
        setFlaggedIds(new Set());
        setExamError('Sign in to start or resume an exam.');
        setIsLoading(false);
        return;
      }
      setIsLoading(true);
      setExamError('');
      setExamId(examIdParam);
      setAttemptId('');
      setQuestions([]);
      setAnswers(new Map());
      setFlaggedIds(new Set());
      setCurrentIndex(0);
      setTimeLeft(50 * 60);
      setDurationSeconds(50 * 60);
      setHasLoadedAttempt(false);
      try {
        if (modeParam === 'mistakes' || modeParam === 'drill') {
          throw new Error('Chế độ luyện câu sai và luyện chuyên đề chưa được nối với lượt thi lưu trên máy chủ.');
        }

        const activeKey = examStorageKey(user.id, 'active_exam', examIdParam);
        const savedSession = readExamStorage(activeKey);
        const saved = savedSession ? JSON.parse(savedSession) as { examId: string; attemptId: string } : null;
        const resumeAttemptId = attemptIdParam || saved?.attemptId;
        if (resumeAttemptId) {
          const state = await getAttempt(resumeAttemptId);
          if (!active) return;
          const elapsed = Math.floor((Date.now() - new Date(state.startedAt).getTime()) / 1000);
          setExamId(state.examId);
          setAttemptId(state.attemptId);
          setQuestions(state.questions);
          setAnswers(new Map(state.answers.map(answer => [answer.questionId, answer])));
          const limit = state.durationMinutes * 60;
          setDurationSeconds(limit);
          setTimeLeft(Math.max(0, limit - elapsed));
          writeExamStorage(activeKey, JSON.stringify({ examId: state.examId, attemptId: state.attemptId }));
          writeExamStorage(examStorageKey(user.id, 'answers', state.examId), JSON.stringify(state.answers.map(answer => [answer.questionId, answer])));
          setHasLoadedAttempt(true);
          setIsLoading(false);
          return;
        }

        const examData = await generateExam(subjectCode);
        if (!active) return;
        setExamId(examData.examId);
        setAttemptId(examData.attemptId);
        setQuestions(examData.questions);
        const limit = (examData.durationMinutes || 50) * 60;
        setDurationSeconds(limit);
        setTimeLeft(limit);
        writeExamStorage(activeKey, JSON.stringify({
          examId: examData.examId,
          attemptId: examData.attemptId,
          questions: examData.questions
        }));
        const nextParams = new URLSearchParams(searchQuery);
        nextParams.set('attemptId', examData.attemptId);
        router.replace(`/exam/${examIdParam}?${nextParams.toString()}`, { scroll: false });
        setHasLoadedAttempt(true);
        setIsLoading(false);
      } catch (error) {
        if (!active) return;
        setExamError(error instanceof Error ? error.message : 'Không thể tạo lượt thi.');
        setIsLoading(false);
      }
    }

    void initExam();
    return () => { active = false; };
  }, [examIdParam, subjectCode, modeParam, topicParam, authLoading, user.id, user.isAuthenticated, attemptIdParam, searchQuery, router]);

  useEffect(() => {
    if (!hasLoadedAttempt || !attemptId || isLoading || isSubmitting) return;
    const snapshot = Array.from(answers.values());
    const timeSpentSeconds = Math.max(0, durationSeconds - timeLeftRef.current);
    const timeout = setTimeout(() => {
      setSaveState('saving');
      saveQueueRef.current = saveQueueRef.current
        .catch(() => undefined)
        .then(() => saveAttemptAnswers(attemptId, snapshot, timeSpentSeconds))
        .then(() => setSaveState('saved'))
        .catch(() => setSaveState('error'));
    }, 600);
    return () => clearTimeout(timeout);
  }, [answers, attemptId, durationSeconds, hasLoadedAttempt, isLoading, isSubmitting]);

  useEffect(() => {
    if (!hasLoadedAttempt || !attemptId || isLoading || isSubmitting) return;
    const interval = setInterval(() => {
      if (submittingRef.current) return;
      const elapsed = Math.max(0, durationSeconds - timeLeftRef.current);
      saveQueueRef.current = saveQueueRef.current
        .catch(() => undefined)
        .then(() => saveAttemptAnswers(attemptId, answersRef.current, elapsed))
        .then(() => setSaveState('saved'))
        .catch(() => setSaveState('error'));
    }, 10_000);
    return () => clearInterval(interval);
  }, [attemptId, durationSeconds, hasLoadedAttempt, isLoading, isSubmitting]);

  // Timer countdown and localStorage sync
  useEffect(() => {
    if (isLoading || questions.length === 0) return;

    const timer = setInterval(() => {
      setTimeLeft(prev => {
        if (prev <= 1) {
          clearInterval(timer);
          void submitHandlerRef.current();
          return 0;
        }
        const updated = prev - 1;
        // Periodic localStorage save every 5 seconds
        if (updated % 5 === 0) {
          writeExamStorage(examStorageKey(user.id, 'timer', examId), updated.toString());
        }
        return updated;
      });
    }, 1000);

    return () => clearInterval(timer);
  }, [isLoading, questions.length, examId, user.id]);

  // Answer change with anti-loss auto-save to localStorage
  const handleAnswerChange = (newAnswer: StudentAnswer) => {
    setAnswers(prev => {
      const next = new Map(prev);
      next.set(newAnswer.questionId, newAnswer);
      try {
        writeExamStorage(examStorageKey(user.id, 'answers', examId), JSON.stringify(Array.from(next.entries())));
      } catch {
        // storage quota
      }
      return next;
    });
  };

  const handleToggleFlag = (questionId: string) => {
    setFlaggedIds(prev => {
      const next = new Set(prev);
      if (next.has(questionId)) {
        next.delete(questionId);
      } else {
        next.add(questionId);
      }
      return next;
    });
  };

  const handleSubmitExam = async () => {
    if (!attemptId || submittingRef.current) return;
    submittingRef.current = true;
    setShowSubmitModal(false);
    setIsSubmitting(true);
    setExamError('');
    try {
      await saveQueueRef.current.catch(() => undefined);
      const submissionAnswers = Array.from(answers.values());
      const spentTime = durationSeconds - timeLeft;
      const result = await submitExam(attemptId, questions, {
        examId,
        timeSpentSeconds: Math.max(spentTime, 0),
        answers: submissionAnswers
      });
      const reviewedQuestions = questions.map(question => {
        const detail = result.details.find(item => item.questionId === question.id);
        return detail ? { ...question, answerData: { ...question.answerData, ...detail.correctAnswerData }, explanation: detail.explanation } : question;
      });
      saveMistakesFromResult(result, reviewedQuestions, user.id, subjectCode);
      writeExamStorage(examStorageKey(user.id, 'result', examId), JSON.stringify(result));
      writeExamStorage(examStorageKey(user.id, 'questions', examId), JSON.stringify(reviewedQuestions));
      writeExamStorage(examStorageKey(user.id, 'flagged', examId), JSON.stringify(Array.from(flaggedIds)));
      removeExamStorage(examStorageKey(user.id, 'active_exam', examIdParam));
      router.push(`/exam/${examId}/result`);
    } catch (error) {
      setExamError(error instanceof Error ? error.message : 'Không thể nộp bài.');
      submittingRef.current = false;
      setIsSubmitting(false);
    }
  };

  useEffect(() => {
    submitHandlerRef.current = handleSubmitExam;
  });

  const openAiTutorForQuestion = (q: Question, ans?: StudentAnswer) => {
    setTutorQuestion(q);
    setTutorStudentAnswer(ans || answers.get(q.id));
    setIsTutorOpen(true);
  };

  if (isLoading) {
    return (
      <div className="min-h-screen flex flex-col bg-[#07090e]">
        <Navbar />
        <div className="flex-1 flex flex-col items-center justify-center text-center p-6 space-y-4">
          <Loader2 className="w-10 h-10 text-indigo-400 animate-spin" />
          <h2 className="text-lg font-bold text-white">Đang tải và sinh đề thi chuẩn Bộ GD&ĐT...</h2>
          <p className="text-xs text-slate-400 max-w-sm">
            Hệ thống đang rút ngẫu nhiên các câu hỏi theo ma trận 3 phần và loại trừ các câu em đã làm đúng.
          </p>
        </div>
      </div>
    );
  }

  if (examError || !attemptId || questions.length === 0) {
    return (
      <div className="min-h-screen bg-[#07090e] text-white">
        <Navbar />
        <section className="mx-auto mt-16 max-w-xl rounded-3xl border border-rose-500/20 bg-slate-950/80 p-8 text-center">
          <h1 className="text-xl font-bold">Không thể mở lượt thi</h1>
          <p role="alert" className="mt-3 text-sm text-slate-300">{examError || 'Lượt thi không hợp lệ.'}</p>
          {examError.toLowerCase().includes('sign in') && <Link href="/login" className="mt-5 inline-flex rounded-xl bg-indigo-600 px-5 py-2.5 text-sm font-bold">Đăng nhập</Link>}
        </section>
      </div>
    );
  }

  const currentQuestion = questions[currentIndex];
  const subjectDisplayName =
    subjectCode === 'GEOGRAPHY'
      ? 'Địa lý'
      : subjectCode === 'LAW_ECONOMICS'
      ? 'Giáo dục Kinh tế & Pháp luật'
      : subjectCode === 'MATHEMATICS'
      ? 'Toán học'
      : 'Tiếng Anh';

  const minutes = Math.floor(timeLeft / 60);
  const seconds = timeLeft % 60;
  const isUrgent = timeLeft < 5 * 60; // Dưới 5 phút

  return (
    <div className="min-h-screen flex flex-col bg-[#07090e]">
      <Navbar />

      {/* Mode Subheaders */}
      {modeParam === 'mistakes' && (
        <div className="bg-gradient-to-r from-rose-950/80 via-slate-900 to-indigo-950/80 border-b border-rose-500/30 px-4 py-2.5 text-center text-xs text-rose-300 font-semibold flex items-center justify-center gap-2">
          <BookMarked className="w-4 h-4 text-rose-400" />
          <span>Đang ở chế độ: <strong>Rèn luyện Sổ tay bẫy đề thi</strong> ({questions.length} câu hỏi cần khắc phục)</span>
        </div>
      )}

      {modeParam === 'drill' && (
        <div className="bg-gradient-to-r from-cyan-950/80 via-slate-900 to-indigo-950/80 border-b border-cyan-500/30 px-4 py-2.5 text-center text-xs text-cyan-300 font-semibold flex items-center justify-center gap-2">
          <Target className="w-4 h-4 text-cyan-400" />
          <span>Đang ở chế độ: <strong>Luyện chuyên đề thích ứng</strong> {topicParam ? `(${topicParam})` : ''} ({questions.length} câu)</span>
        </div>
      )}

      {/* Sticky Exam Toolbar with Countdown and Submit Button */}
      <div className="glass-panel border-b border-white/10 px-4 sm:px-8 py-3.5 sticky top-16 z-40 backdrop-blur-xl">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div>
              <div className="flex items-center gap-2">
                <h1 className="text-base sm:text-lg font-bold text-white">
                  Đề thi: {subjectDisplayName}
                </h1>
                <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                  GDPT 2018
                </span>
              </div>
              <p className="text-xs text-slate-400">
                Thời gian làm bài: 50 phút • 3 phần thi theo ma trận chuẩn
              </p>
            </div>
          </div>

          <div className="flex items-center gap-3 sm:gap-4">
            {/* Countdown Clock */}
            <div
              className={`flex items-center gap-2 px-3.5 py-1.5 sm:py-2 rounded-xl border font-mono text-xs sm:text-base font-bold transition-all ${
                isUrgent
                  ? 'bg-rose-500/20 text-rose-300 border-rose-500/40 animate-pulse'
                  : 'bg-slate-900/80 text-cyan-300 border-cyan-500/30'
              }`}
            >
              <Clock className="w-4 h-4 text-cyan-400" />
              <span>
                {String(minutes).padStart(2, '0')}:{String(seconds).padStart(2, '0')}
              </span>
              <span className="sr-only" aria-live="polite">
                {saveState === 'saving' ? 'Đang đồng bộ câu trả lời' : saveState === 'error' ? 'Chưa đồng bộ được câu trả lời' : 'Câu trả lời đã đồng bộ'}
              </span>
              <span className={`hidden sm:inline text-[10px] font-sans ${saveState === 'error' ? 'text-rose-300' : 'text-slate-500'}`}>
                {saveState === 'saving' ? 'Đang lưu' : saveState === 'error' ? 'Chưa đồng bộ' : 'Đã lưu'}
              </span>
            </div>

            {/* NỘP BÀI & CHẤM ĐIỂM */}
            <button
              onClick={() => setShowSubmitModal(true)}
              className="flex items-center gap-2 px-4 py-2 rounded-xl text-xs sm:text-sm font-bold bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white shadow-lg shadow-indigo-600/30 transition-all hover:scale-105 cursor-pointer"
            >
              <Send className="w-4 h-4" />
              <span>Nộp bài & Chấm điểm</span>
            </button>
          </div>
        </div>
      </div>

      {/* Main 2-Column Layout (Trái 75%: Nội dung câu hỏi | Phải 25%: Bảng điều hướng & Đồng hồ) */}
      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-6 sm:py-8">
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
          {/* Trái 75% (8-9 cols): Question Card and Next/Prev Navigation */}
          <div className="lg:col-span-8 space-y-6">
            {currentQuestion && (
              <QuestionCard
                key={currentQuestion.id}
                question={currentQuestion}
                questionNumber={currentIndex + 1}
                currentAnswer={answers.get(currentQuestion.id)}
                isFlagged={flaggedIds.has(currentQuestion.id)}
                onAnswerChange={handleAnswerChange}
                onToggleFlag={() => handleToggleFlag(currentQuestion.id)}
                onAskTutor={() => openAiTutorForQuestion(currentQuestion)}
              />
            )}

            {/* Navigation Controls: Prev / Next */}
            <div className="flex items-center justify-between pt-2">
              <button
                disabled={currentIndex === 0}
                onClick={() => setCurrentIndex(prev => Math.max(0, prev - 1))}
                className="flex items-center gap-2 px-5 py-2.5 rounded-xl text-xs sm:text-sm font-semibold bg-slate-900/80 border border-white/10 text-slate-300 hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
              >
                <ChevronLeft className="w-4 h-4" />
                <span>Câu trước</span>
              </button>

              <div className="text-xs font-semibold text-slate-400">
                Câu <strong className="text-white">{currentIndex + 1}</strong> trên {questions.length}
              </div>

              <button
                disabled={currentIndex === questions.length - 1}
                onClick={() => setCurrentIndex(prev => Math.min(questions.length - 1, prev + 1))}
                className="flex items-center gap-2 px-5 py-2.5 rounded-xl text-xs sm:text-sm font-bold bg-indigo-600 hover:bg-indigo-500 text-white shadow-lg shadow-indigo-600/30 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
              >
                <span>Câu tiếp theo</span>
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>

          {/* Phải 25% (3-4 cols): Question Palette & Progress */}
          <div className="lg:col-span-4">
            <QuestionPalette
              questions={questions}
              currentQuestionIndex={currentIndex}
              answers={answers}
              flaggedQuestionIds={flaggedIds}
              onSelectQuestion={(idx) => setCurrentIndex(idx)}
            />
          </div>
        </div>
      </main>

      {/* Confirmation Modal before Submit */}
      {showSubmitModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm p-4 animate-in fade-in duration-200">
          <div className="w-full max-w-md glass-panel rounded-3xl p-6 sm:p-7 border border-white/10 shadow-2xl">
            <div className="flex items-center gap-3 text-amber-400 mb-4">
              <div className="w-10 h-10 rounded-2xl bg-amber-500/20 border border-amber-500/30 flex items-center justify-center">
                <AlertTriangle className="w-5 h-5 text-amber-400" />
              </div>
              <div>
                <h3 className="text-base font-bold text-white">Xác nhận nộp bài thi</h3>
                <p className="text-xs text-slate-400">Thời gian làm bài vẫn còn {minutes} phút {seconds} giây</p>
              </div>
            </div>

            <div className="bg-slate-950/60 rounded-2xl p-4 border border-white/5 mb-6 text-xs sm:text-sm text-slate-300 space-y-2">
              <div className="flex justify-between">
                <span>Số câu đã làm:</span>
                <span className="font-bold text-indigo-300">{answers.size}/{questions.length} câu</span>
              </div>
              <div className="flex justify-between">
                <span>Số câu chưa trả lời:</span>
                <span className={`font-bold ${questions.length - answers.size > 0 ? 'text-rose-400' : 'text-emerald-400'}`}>
                  {questions.length - answers.size} câu
                </span>
              </div>
            </div>

            <div className="flex items-center justify-end gap-3">
              <button
                onClick={() => setShowSubmitModal(false)}
                className="px-4 py-2.5 rounded-xl text-xs font-semibold bg-white/5 border border-white/10 text-slate-300 hover:bg-white/10 transition-colors"
              >
                Tiếp tục làm bài
              </button>
              <button
                onClick={handleSubmitExam}
                disabled={isSubmitting}
                className="px-5 py-2.5 rounded-xl text-xs font-bold bg-indigo-600 hover:bg-indigo-500 text-white shadow-lg shadow-indigo-600/30 transition-all cursor-pointer"
              >
                {isSubmitting ? 'Đang nộp bài…' : 'Chấm điểm & Xem kết quả'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Context-Aware AI Tutor Drawer */}
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

function ExamWrapper({ params }: { params: Promise<{ id: string }> }) {
  const unwrappedParams = use(params);
  return <VirtualExamRoom examIdParam={unwrappedParams.id} />;
}

export default function ExamDynamicPage(props: ExamPageProps) {
  return (
    <Suspense
      fallback={
        <div className="min-h-screen flex items-center justify-center bg-[#07090e] text-white">
          <Loader2 className="w-8 h-8 text-indigo-400 animate-spin" />
        </div>
      }
    >
      <ExamWrapper params={props.params} />
    </Suspense>
  );
}
