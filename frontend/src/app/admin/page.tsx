'use client';

import { FormEvent, useCallback, useEffect, useState } from 'react';
import Image from 'next/image';
import { Navbar } from '@/components/Navbar';
import { RoleGuard } from '@/components/RoleGuard';
import { useAuth } from '@/context/AuthContext';
import {
  getModerationStats,
  getPendingQuestions,
  ModerationStats,
  PendingQuestion,
  reviewQuestion,
  submitExamForIngestion,
  updatePendingQuestion
} from '@/lib/api';
import { IngestionStatusResponse, SubjectCode } from '@/types';
import {
  Clock,
  CheckCircle2,
  XCircle,
  Edit3,
  Save,
  X,
  FileText,
  RefreshCw
} from 'lucide-react';

const supportedSubjects: Array<{ code: SubjectCode; label: string }> = [
  { code: 'GEOGRAPHY', label: 'Địa lý' },
  { code: 'LAW_ECONOMICS', label: 'Giáo dục Kinh tế & Pháp luật' },
  { code: 'MATHEMATICS', label: 'Toán học' },
  { code: 'PHYSICS', label: 'Vật lý' },
  { code: 'CHEMISTRY', label: 'Hóa học' },
  { code: 'BIOLOGY', label: 'Sinh học' },
  { code: 'HISTORY', label: 'Lịch sử' },
  { code: 'ENGLISH', label: 'Tiếng Anh' }
];

export default function AdminPage() {
  const { role, isLoading: authLoading } = useAuth();
  const [subjectCode, setSubjectCode] = useState<SubjectCode>('GEOGRAPHY');
  const [sourceName, setSourceName] = useState('');
  const [examYear, setExamYear] = useState('');
  const [rawContent, setRawContent] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [result, setResult] = useState<IngestionStatusResponse | null>(null);
  const [pendingQuestions, setPendingQuestions] = useState<PendingQuestion[]>([]);
  const [hasMoreQuestions, setHasMoreQuestions] = useState(false);
  const [reviewNotes, setReviewNotes] = useState<Record<string, string>>({});
  const [reviewError, setReviewError] = useState('');
  const [isLoadingQuestions, setIsLoadingQuestions] = useState(true);
  const [reviewingId, setReviewingId] = useState<string | null>(null);

  // Moderation stats
  const [stats, setStats] = useState<ModerationStats | null>(null);

  // Question editing modal state
  const [editingQuestion, setEditingQuestion] = useState<PendingQuestion | null>(null);
  const [editForm, setEditForm] = useState<{
    content: string;
    attachedTable: string;
    topic: string;
    difficulty: string;
    explanation: string;
  }>({ content: '', attachedTable: '', topic: '', difficulty: '', explanation: '' });
  const [isSavingEdit, setIsSavingEdit] = useState(false);
  const [editError, setEditError] = useState('');

  const refreshStats = useCallback(async () => {
    try {
      const data = await getModerationStats();
      setStats(data);
    } catch {
      // Ignored
    }
  }, []);

  const refreshPending = useCallback(async (offset = 0) => {
    setIsLoadingQuestions(true);
    setReviewError('');
    try {
      const questions = await getPendingQuestions(offset);
      setPendingQuestions(current => offset === 0 ? questions : [...current, ...questions]);
      setHasMoreQuestions(questions.length === 100);
      void refreshStats();
    } catch (cause) {
      setReviewError(cause instanceof Error ? cause.message : 'Không thể tải câu hỏi chờ duyệt.');
    } finally {
      setIsLoadingQuestions(false);
    }
  }, [refreshStats]);

  useEffect(() => {
    if (authLoading || (role !== 'ADMIN' && role !== 'TEACHER')) return;
    let active = true;
    const frame = requestAnimationFrame(() => {
      if (!active) return;
      void getPendingQuestions(0).then(questions => {
        if (active) {
          setPendingQuestions(questions);
          setHasMoreQuestions(questions.length === 100);
        }
      }).catch(cause => {
        if (active) setReviewError(cause instanceof Error ? cause.message : 'Không thể tải câu hỏi chờ duyệt.');
      }).finally(() => {
        if (active) setIsLoadingQuestions(false);
      });
      void refreshStats();
    });

    return () => {
      active = false;
      cancelAnimationFrame(frame);
    };
  }, [authLoading, refreshStats, role]);

  async function handleReview(questionId: string, action: 'verify' | 'reject') {
    const note = reviewNotes[questionId]?.trim() || '';
    if (action === 'reject' && !note) {
      setReviewError('Nhập lý do loại câu hỏi trước khi từ chối.');
      return;
    }
    setReviewingId(questionId);
    setReviewError('');
    try {
      await reviewQuestion(questionId, action, note);
      setPendingQuestions(current => current.filter(question => question.id !== questionId));
      setReviewNotes(current => {
        const next = { ...current };
        delete next[questionId];
        return next;
      });
      void refreshStats();
    } catch (cause) {
      setReviewError(cause instanceof Error ? cause.message : 'Không thể lưu quyết định duyệt.');
    } finally {
      setReviewingId(null);
    }
  }

  function handleOpenEdit(question: PendingQuestion) {
    setEditingQuestion(question);
    setEditForm({
      content: question.content,
      attachedTable: question.attachedTable || '',
      topic: question.topic,
      difficulty: question.difficulty,
      explanation: question.explanation || ''
    });
    setEditError('');
  }

  async function handleSaveEdit() {
    if (!editingQuestion) return;
    setIsSavingEdit(true);
    setEditError('');
    try {
      const updated = await updatePendingQuestion(editingQuestion.id, {
        content: editForm.content,
        attachedTable: editForm.attachedTable,
        topic: editForm.topic,
        difficulty: editForm.difficulty,
        explanation: editForm.explanation
      });

      setPendingQuestions(current =>
        current.map(q => q.id === updated.id ? { ...q, ...updated } : q)
      );
      setEditingQuestion(null);
    } catch (cause) {
      setEditError(cause instanceof Error ? cause.message : 'Không thể lưu câu hỏi đã sửa.');
    } finally {
      setIsSavingEdit(false);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isSubmitting) return;
    setError('');
    setResult(null);
    setIsSubmitting(true);
    try {
      const response = await submitExamForIngestion({
        subjectCode,
        sourceName: sourceName.trim(),
        examYear: examYear ? Number(examYear) : undefined,
        rawContent: rawContent.trim()
      });
      setResult(response);
      setRawContent('');
      void refreshPending();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Không thể gửi tài liệu vào hàng đợi.');
    } finally {
      setIsSubmitting(false);
    }
  }

  async function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    if (!file.name.toLowerCase().endsWith('.txt')) {
      setError('Hiện chỉ hỗ trợ file .txt. OCR cho PDF và ảnh chưa được triển khai.');
      event.target.value = '';
      return;
    }
    if (file.size > 2 * 1024 * 1024) {
      setError('File vượt quá giới hạn 2 MB.');
      event.target.value = '';
      return;
    }
    setError('');
    const text = await file.text();
    setRawContent(text);
    if (!sourceName.trim()) {
      setSourceName(file.name.replace(/\.[^/.]+$/, ''));
    }
  }

  return (
    <div className="min-h-screen bg-[#07090e] text-white">
      <Navbar />
      <RoleGuard allowedRoles={['ADMIN', 'TEACHER']}>
        <main className="mx-auto max-w-5xl space-y-8 px-4 py-10 sm:px-6 lg:px-8">
          <header className="space-y-2">
            <h1 className="text-3xl font-black">Quản trị & Kiểm duyệt Ngân hàng đề</h1>
            <p className="text-sm text-slate-400">
              {role === 'ADMIN'
                ? 'Nạp đề thi dạng văn bản vào hàng đợi và kiểm duyệt nội dung câu hỏi.'
                : 'Xem xét và phê duyệt hoặc từ chối câu hỏi trước khi đưa vào ngân hàng đề.'}
            </p>
          </header>

          {/* Moderation Metrics Bar */}
          {stats && (
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div className="rounded-2xl border border-amber-500/30 bg-amber-950/20 p-4">
                <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-amber-400">
                  <Clock className="w-4 h-4" />
                  <span>Đang chờ duyệt</span>
                </div>
                <div className="mt-2 text-2xl font-black font-mono text-white">{stats.totalPending}</div>
                <p className="text-xs text-slate-400 mt-0.5">Câu hỏi cần giáo viên hiệu đính</p>
              </div>

              <div className="rounded-2xl border border-emerald-500/30 bg-emerald-950/20 p-4">
                <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-emerald-400">
                  <CheckCircle2 className="w-4 h-4" />
                  <span>Đã duyệt & xuất bản</span>
                </div>
                <div className="mt-2 text-2xl font-black font-mono text-white">{stats.totalVerified}</div>
                <p className="text-xs text-slate-400 mt-0.5">Sẵn sàng tạo đề thi thử</p>
              </div>

              <div className="rounded-2xl border border-rose-500/30 bg-rose-950/20 p-4">
                <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-rose-400">
                  <XCircle className="w-4 h-4" />
                  <span>Đã từ chối / loại</span>
                </div>
                <div className="mt-2 text-2xl font-black font-mono text-white">{stats.totalRejected}</div>
                <p className="text-xs text-slate-400 mt-0.5">Lưu lịch sử kèm lý do loại</p>
              </div>
            </div>
          )}

          {role === 'ADMIN' && (
            <form onSubmit={handleSubmit} className="space-y-4 rounded-3xl border border-white/10 bg-slate-950/80 p-6 shadow-xl">
              <div className="flex items-center gap-2 border-b border-white/10 pb-3">
                <FileText className="w-5 h-5 text-indigo-400" />
                <h2 className="text-lg font-bold">Nạp đề thi mới vào hàng đợi bóc tách</h2>
              </div>
              <div className="grid gap-4 sm:grid-cols-3">
                <label className="text-sm text-slate-300">
                  Môn học
                  <select value={subjectCode} onChange={event => setSubjectCode(event.target.value as SubjectCode)} className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2.5">
                    {supportedSubjects.map(subject => <option key={subject.code} value={subject.code}>{subject.label}</option>)}
                  </select>
                </label>
                <label className="text-sm text-slate-300">
                  Nguồn đề
                  <input required maxLength={200} value={sourceName} onChange={event => setSourceName(event.target.value)} className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2.5" />
                </label>
                <label className="text-sm text-slate-300">
                  Năm (không bắt buộc)
                  <input type="number" min="2000" max="2100" value={examYear} onChange={event => setExamYear(event.target.value)} className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2.5" />
                </label>
              </div>

              <label className="block text-sm text-slate-300">
                Chọn file văn bản .txt (tối đa 2 MB)
                <input type="file" accept=".txt,text/plain" onChange={handleFileChange} className="mt-2 block w-full text-sm text-slate-400 file:mr-3 file:rounded-lg file:border-0 file:bg-indigo-600 file:px-3 file:py-2 file:font-semibold file:text-white" />
              </label>

              <label className="block text-sm text-slate-300">
                Hoặc dán nội dung đề
                <textarea required value={rawContent} onChange={event => setRawContent(event.target.value)} rows={10} maxLength={2 * 1024 * 1024} className="mt-2 w-full rounded-xl border border-white/10 bg-slate-900 p-3 font-mono text-xs text-slate-100" />
              </label>

              {error && <p role="alert" className="rounded-xl border border-rose-500/30 bg-rose-950/30 p-3 text-sm text-rose-200">{error}</p>}
              {result && <p role="status" className="rounded-xl border border-emerald-500/30 bg-emerald-950/30 p-3 text-sm text-emerald-200">Đã nhận tài liệu. Mã hồ sơ: {result.paperId}; trạng thái: {result.status}.</p>}

              <button disabled={isSubmitting || !rawContent.trim() || !sourceName.trim()} className="rounded-xl bg-indigo-600 px-5 py-3 text-sm font-bold hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-50">
                {isSubmitting ? 'Đang gửi...' : 'Đưa vào hàng đợi'}
              </button>
            </form>
          )}

          <section className="space-y-4">
            <header className="flex flex-wrap items-end justify-between gap-3">
              <div>
                <h2 className="text-xl font-black">Hàng chờ duyệt câu hỏi ({pendingQuestions.length})</h2>
                <p className="mt-1 text-sm text-slate-400">Hiệu đính, kiểm tra đáp án và xuất bản vào ngân hàng đề thi.</p>
              </div>
              <button onClick={() => void refreshPending()} disabled={isLoadingQuestions} className="flex items-center gap-1.5 rounded-xl border border-white/10 px-4 py-2 text-sm disabled:opacity-50 hover:bg-white/5">
                <RefreshCw className={`w-3.5 h-3.5 ${isLoadingQuestions ? 'animate-spin' : ''}`} />
                <span>{isLoadingQuestions ? 'Đang tải…' : 'Tải lại'}</span>
              </button>
            </header>

            {reviewError && <p role="alert" className="rounded-xl border border-rose-500/30 bg-rose-950/30 p-3 text-sm text-rose-200">{reviewError}</p>}
            {isLoadingQuestions && pendingQuestions.length === 0 && <p className="text-sm text-slate-400">Đang tải hàng chờ duyệt…</p>}
            {!isLoadingQuestions && pendingQuestions.length === 0 && <p className="rounded-2xl border border-white/10 p-5 text-sm text-slate-400">Không có câu hỏi đang chờ duyệt.</p>}

            {pendingQuestions.map(question => (
              <article key={question.id} className="space-y-4 rounded-2xl border border-white/10 bg-slate-950/70 p-5 shadow-lg">
                <div className="flex flex-wrap items-center justify-between gap-2 border-b border-white/5 pb-2">
                  <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-slate-400">
                    <span className="font-bold text-indigo-300">{question.subjectName}</span>
                    <span>·</span>
                    <span>{question.topic}</span>
                    <span>·</span>
                    <span className="font-mono text-slate-300">{question.format}</span>
                    <span>·</span>
                    <span className="rounded bg-white/5 px-1.5 py-0.5 text-[10px] text-amber-300">{question.difficulty}</span>
                  </div>

                  <button
                    onClick={() => handleOpenEdit(question)}
                    className="flex items-center gap-1 rounded-lg border border-indigo-500/40 bg-indigo-500/10 px-2.5 py-1 text-xs font-semibold text-indigo-300 hover:bg-indigo-500/20"
                  >
                    <Edit3 className="w-3.5 h-3.5" />
                    <span>Sửa câu hỏi</span>
                  </button>
                </div>

                <div className="whitespace-pre-wrap text-sm text-white leading-relaxed">{question.content}</div>

                {question.attachedTable && (
                  <pre className="overflow-auto rounded-xl bg-black/40 p-3 font-mono text-xs text-slate-300 border border-white/5">{question.attachedTable}</pre>
                )}

                <details className="text-sm text-slate-300">
                  <summary className="cursor-pointer font-medium text-slate-400 hover:text-white">Xem chi tiết đáp án & lời giải</summary>
                  <pre className="mt-2 overflow-auto rounded-xl bg-black/40 p-3 text-xs border border-white/5">{JSON.stringify(question.answerData, null, 2)}</pre>
                  {question.explanation && <p className="mt-2 whitespace-pre-wrap text-xs text-slate-300 bg-slate-900/40 p-2.5 rounded-lg border border-white/5">{question.explanation}</p>}
                </details>

                {question.imageUrls?.map((url, index) => (
                  <Image key={`${question.id}-${index}`} src={url} alt={`Hình minh họa ${index + 1}`} width={1024} height={768} unoptimized className="max-h-80 w-auto rounded-xl object-contain" />
                ))}

                <p className="text-xs text-slate-500">Nguồn: {question.sourceTags?.join(', ') || 'Chưa có thông tin nguồn'}</p>

                <div className="flex flex-wrap items-center gap-2 pt-2 border-t border-white/5">
                  <button
                    disabled={reviewingId !== null}
                    onClick={() => void handleReview(question.id, 'verify')}
                    className="flex items-center gap-1.5 rounded-xl bg-emerald-600 hover:bg-emerald-500 px-4 py-2 text-xs font-bold text-white disabled:opacity-50 transition-colors"
                  >
                    <CheckCircle2 className="w-3.5 h-3.5" />
                    <span>{reviewingId === question.id ? 'Đang lưu…' : 'Duyệt & Xuất bản'}</span>
                  </button>

                  <input
                    value={reviewNotes[question.id] || ''}
                    onChange={event => setReviewNotes(current => ({ ...current, [question.id]: event.target.value }))}
                    maxLength={1000}
                    placeholder="Lý do từ chối (bắt buộc khi từ chối)"
                    className="min-w-56 flex-1 rounded-xl border border-white/10 bg-slate-900 px-3 py-2 text-xs"
                  />

                  <button
                    disabled={reviewingId !== null || !reviewNotes[question.id]?.trim()}
                    onClick={() => void handleReview(question.id, 'reject')}
                    className="flex items-center gap-1.5 rounded-xl border border-rose-500/40 px-3.5 py-2 text-xs font-bold text-rose-300 hover:bg-rose-500/10 disabled:opacity-50 transition-colors"
                  >
                    <XCircle className="w-3.5 h-3.5" />
                    <span>Từ chối</span>
                  </button>
                </div>
              </article>
            ))}

            {hasMoreQuestions && (
              <button
                onClick={() => void refreshPending(pendingQuestions.length)}
                disabled={isLoadingQuestions}
                className="w-full rounded-2xl border border-white/10 bg-slate-950/40 px-4 py-3 text-sm font-semibold text-slate-300 hover:bg-white/5 disabled:opacity-50"
              >
                {isLoadingQuestions ? 'Đang tải…' : 'Tải thêm câu hỏi'}
              </button>
            )}
          </section>

          {/* Edit Question Modal */}
          {editingQuestion && (
            <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-sm p-4 overflow-y-auto">
              <div className="relative w-full max-w-3xl rounded-3xl border border-white/20 bg-slate-950 p-6 shadow-2xl space-y-4 max-h-[90vh] overflow-y-auto">
                <div className="flex items-center justify-between border-b border-white/10 pb-3">
                  <div className="flex items-center gap-2">
                    <Edit3 className="w-5 h-5 text-indigo-400" />
                    <h3 className="text-lg font-bold">Hiệu đính câu hỏi</h3>
                  </div>
                  <button onClick={() => setEditingQuestion(null)} className="rounded-lg p-1 text-slate-400 hover:text-white">
                    <X className="w-5 h-5" />
                  </button>
                </div>

                {editError && <p role="alert" className="rounded-xl border border-rose-500/30 bg-rose-950/30 p-3 text-xs text-rose-200">{editError}</p>}

                <div className="space-y-3 text-xs">
                  <label className="block text-slate-300">
                    Nội dung câu hỏi (Markdown / LaTeX)
                    <textarea
                      rows={5}
                      value={editForm.content}
                      onChange={e => setEditForm(prev => ({ ...prev, content: e.target.value }))}
                      className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 p-3 text-slate-100 font-mono text-xs"
                    />
                  </label>

                  <div className="grid grid-cols-2 gap-3">
                    <label className="block text-slate-300">
                      Chuyên đề (Topic)
                      <input
                        value={editForm.topic}
                        onChange={e => setEditForm(prev => ({ ...prev, topic: e.target.value }))}
                        className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2 text-slate-100"
                      />
                    </label>

                    <label className="block text-slate-300">
                      Mức độ khó
                      <select
                        value={editForm.difficulty}
                        onChange={e => setEditForm(prev => ({ ...prev, difficulty: e.target.value }))}
                        className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2 text-slate-100"
                      >
                        <option value="Recognition">Nhận biết (Recognition)</option>
                        <option value="Comprehension">Thông hiểu (Comprehension)</option>
                        <option value="Application">Vận dụng (Application)</option>
                        <option value="HighApplication">Vận dụng cao (HighApplication)</option>
                      </select>
                    </label>
                  </div>

                  <label className="block text-slate-300">
                    Bảng số liệu đính kèm (Markdown Table)
                    <textarea
                      rows={3}
                      value={editForm.attachedTable}
                      onChange={e => setEditForm(prev => ({ ...prev, attachedTable: e.target.value }))}
                      placeholder="| Cột 1 | Cột 2 | ..."
                      className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 p-3 text-slate-100 font-mono text-xs"
                    />
                  </label>

                  <label className="block text-slate-300">
                    Lời giải chi tiết (Explanation)
                    <textarea
                      rows={4}
                      value={editForm.explanation}
                      onChange={e => setEditForm(prev => ({ ...prev, explanation: e.target.value }))}
                      className="mt-1 w-full rounded-xl border border-white/10 bg-slate-900 p-3 text-slate-100 text-xs"
                    />
                  </label>
                </div>

                <div className="flex justify-end gap-3 pt-3 border-t border-white/10">
                  <button
                    onClick={() => setEditingQuestion(null)}
                    className="rounded-xl border border-white/10 px-4 py-2 text-xs font-semibold text-slate-300 hover:bg-white/5"
                  >
                    Hủy
                  </button>
                  <button
                    onClick={() => void handleSaveEdit()}
                    disabled={isSavingEdit || !editForm.content.trim()}
                    className="flex items-center gap-1.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 px-4 py-2 text-xs font-bold text-white disabled:opacity-50"
                  >
                    <Save className="w-3.5 h-3.5" />
                    <span>{isSavingEdit ? 'Đang lưu…' : 'Lưu hiệu đính'}</span>
                  </button>
                </div>
              </div>
            </div>
          )}
        </main>
      </RoleGuard>
    </div>
  );
}
