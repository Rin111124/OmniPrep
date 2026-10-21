// ============================================================
// OmniPrep API Client
// Kết nối với backend ASP.NET Core & Gemini SSE streaming
// Requests are served by the authenticated application API.
// ============================================================

import {
  DashboardStats,
  ExamGradingResult,
  ExamSubmission,
  IngestionPayload,
  IngestionStatusResponse,
  Question,
  QuestionGradingDetail,
  StudentAnswer,
  Subject,
  SubjectCode,
  ScoreHistoryItem,
  TopicMastery,
  TutorContext,
  ExamSource,
} from '@/types';

const API_BASE = '/api/backend';

interface BackendSubject {
  id: string;
  code: string;
  name: string;
  topics: string[];
  totalScore: number;
  activeQuestionsCount: number;
  isExamSupported: boolean;
  hasQuestionCapacity: boolean;
  questionAvailability: {
    part1: { available: number; required: number };
    part2: { available: number; required: number };
    part3: { available: number; required: number };
  };
  examMatrix?: { part1?: { count: number }; part2?: { count: number }; part3?: { count: number } };
}

interface BackendQuestion {
  id: string;
  content: string;
  subjectId?: string;
  attachedTable?: string | null;
  format: string;
  difficulty: string;
  topic: string;
  imageUrls?: string[];
  sourceTags?: string[];
  options?: Record<string, string> | null;
  statements?: Array<{ id: string; text: string }> | null;
  unit?: string;
}

interface BackendAnswerData {
  options?: Record<string, string>;
  correct?: string;
  correctOption?: string;
  statements?: Array<{ id: string; text: string; is_correct?: boolean; isCorrect?: boolean }>;
  numericValue?: number;
  acceptedTexts?: string[];
  tolerance?: number;
  unit?: string;
}

interface BackendGradeDetail {
  questionId: string;
  questionNumber?: number;
  status: string;
  pointsEarned: number;
  topic?: string;
  format?: string;
  content?: string;
  explanation?: string;
  distractorAnalysis?: Record<string, string>;
  answer?: Record<string, unknown> | null;
  correctAnswerData?: BackendAnswerData;
}

interface BackendGradingResult {
  attemptId: string;
  totalScore: number;
  maxScore: number;
  scorePart1: number;
  scorePart2: number;
  scorePart3: number;
  timeSpentSeconds?: number;
  details: BackendGradeDetail[];
}

interface BackendHistoryItem {
  id: string;
  subjectCode: string;
  subjectName: string;
  totalScore: number;
  isSubmitted: boolean;
  startedAt: string;
  submittedAt?: string;
  timeSpentMinutes?: number;
}

interface BackendMastery {
  topics: Array<{ topic: string; totalAttempts: number; totalCorrect: number; masteryRate: number }>;
}

async function backendFetch(path: string, init?: RequestInit): Promise<Response> {
  const response = await fetch(`${API_BASE}${path}`, { ...init, cache: 'no-store' });
  if (!response.ok) {
    const body = await response.text();
    let message = body || `API request failed (${response.status})`;
    try {
      const parsed = JSON.parse(body);
      message = parsed.message || parsed.title || message;
    } catch {
      // Keep the response text.
    }
    throw new Error(message);
  }
  return response;
}

const backendSubjectCodes: Record<SubjectCode, string> = {
  GEOGRAPHY: 'GEO',
  LAW_ECONOMICS: 'GDKTPL',
  MATHEMATICS: 'MATH',
  PHYSICS: 'PHYS',
  CHEMISTRY: 'CHEM',
  BIOLOGY: 'BIO',
  HISTORY: 'HIS',
  ENGLISH: 'ENG'
};

function mapBackendQuestion(raw: BackendQuestion): Question {
  const format: Question['format'] = raw.format === 'MultipleChoice' ? 'SingleChoice'
    : raw.format === 'TrueFalse' ? 'TrueFalseGroup'
      : 'ShortAnswer';
  const difficulty: Question['difficulty'] = raw.difficulty === 'NhanBiet' ? 'Recognition'
    : raw.difficulty === 'ThongHieu' ? 'Comprehension'
      : raw.difficulty === 'VanDungCao' ? 'HighApplication'
        : 'Application';
  const statements = Array.isArray(raw.statements) ? raw.statements : [];
  return {
    id: raw.id,
    subjectId: raw.subjectId || '',
    content: raw.content,
    attachedTable: raw.attachedTable,
    format,
    difficulty,
    topic: raw.topic,
    answerData: {
      options: raw.options || undefined,
      subQuestions: statements.length
        ? Object.fromEntries(statements.map(item => [item.id, item.text]))
        : undefined,
      unit: raw.unit
    },
    imageUrls: raw.imageUrls || [],
    sourceTags: raw.sourceTags || []
  };
}

function mapAnswerData(raw: BackendAnswerData = {}) {
  const statements = Array.isArray(raw?.statements) ? raw.statements : [];
  return {
    options: raw?.options,
    correctOption: raw?.correctOption ?? raw?.correct,
    subQuestions: statements.length
      ? Object.fromEntries(statements.map(item => [item.id, item.text]))
      : undefined,
    subAnswers: statements.length
      ? Object.fromEntries(statements.flatMap(item => {
          const answer = item.is_correct ?? item.isCorrect;
          return typeof answer === 'boolean' ? [[item.id, answer]] : [];
        }))
      : undefined,
    correctAnswer: raw?.numericValue != null ? String(raw.numericValue) : undefined,
    acceptedAnswers: raw?.acceptedTexts,
    numericTolerance: raw?.tolerance,
    unit: raw?.unit
  };
}

export async function getSubjects(): Promise<Subject[]> {
  const res = await backendFetch('/subjects');
  const data: BackendSubject[] = await res.json();
  const frontendCodes = Object.fromEntries(Object.entries(backendSubjectCodes).map(([key, value]) => [value, key])) as Record<string, SubjectCode>;
  return (data || []).flatMap(item => frontendCodes[item.code] ? [{
    ...item,
    code: frontendCodes[item.code],
    activeQuestionsCount: item.activeQuestionsCount,
    isExamSupported: item.isExamSupported,
    isExamReady: item.isExamSupported && item.hasQuestionCapacity,
    questionAvailability: item.questionAvailability,
    examMatrix: item.examMatrix ? {
      partI_SingleChoice_Count: item.examMatrix.part1?.count || 0,
      partII_TrueFalseGroup_Count: item.examMatrix.part2?.count || 0,
      partIII_ShortAnswer_Count: item.examMatrix.part3?.count || 0
    } : undefined
  }] : []);
}

export async function generateExam(
  subjectCode: SubjectCode,
  sourceName?: string
): Promise<{ examId: string; attemptId: string; questions: Question[]; expiresAt?: string; durationMinutes?: number }> {
  const payload: { subjectCode: string; sourceName?: string } = {
    subjectCode: backendSubjectCodes[subjectCode]
  };
  if (sourceName && sourceName.trim() && sourceName !== 'RANDOM' && sourceName !== 'ALL') {
    payload.sourceName = sourceName.trim();
  }
  const response = await backendFetch('/exams/generate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  });
  const data: { examId: string; questions: BackendQuestion[]; expiresAt?: string; durationMinutes?: number } = await response.json();
  const startResponse = await backendFetch('/attempts/start', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ examId: data.examId })
  });
  const attempt: { attemptId: string } = await startResponse.json();
  const questions = (data.questions || []).filter(Boolean).map(mapBackendQuestion);
  if (questions.length === 0) throw new Error('Đề thi không có câu hỏi đã duyệt.');
  return {
    examId: data.examId,
    attemptId: attempt.attemptId,
    questions,
    expiresAt: data.expiresAt,
    durationMinutes: data.durationMinutes
  };
}

export async function getExamSources(params?: { type?: string; province?: string; search?: string }): Promise<ExamSource[]> {
  const query = new URLSearchParams();
  if (params?.type) query.set('type', params.type);
  if (params?.province) query.set('province', params.province);
  if (params?.search) query.set('search', params.search);
  const qs = query.toString();
  const response = await backendFetch(`/exam-sources${qs ? `?${qs}` : ''}`);
  return response.json();
}

interface BackendAttemptState {
  id: string;
  examId: string;
  startedAt: string;
  durationMinutes: number;
  expiresAt: string;
  questions: BackendQuestion[];
  answers: Array<{ questionId: string; answer: Record<string, unknown> | null; timeSpentSeconds?: number | null }>;
}

export async function getAttempt(attemptId: string): Promise<{
  examId: string;
  attemptId: string;
  startedAt: string;
  durationMinutes: number;
  expiresAt: string;
  questions: Question[];
  answers: StudentAnswer[];
}> {
  const response = await backendFetch(`/attempts/${encodeURIComponent(attemptId)}`);
  const raw: BackendAttemptState = await response.json();
  return {
    examId: raw.examId,
    attemptId: raw.id,
    startedAt: raw.startedAt,
    durationMinutes: raw.durationMinutes,
    expiresAt: raw.expiresAt,
    questions: raw.questions.map(mapBackendQuestion),
    answers: raw.answers.flatMap(item => {
      const answer = item.answer;
      if (!answer) return [];
      const restored: StudentAnswer = { questionId: item.questionId };
      if (typeof answer.selected === 'string') restored.selectedOption = answer.selected;
      if (answer.answers && typeof answer.answers === 'object') {
        restored.subAnswers = Object.fromEntries(Object.entries(answer.answers).map(([key, value]) => [key, value === true || value === 'true']));
      }
      if (answer.value != null) restored.textAnswer = String(answer.value);
      return [restored];
    })
  };
}

export async function saveAttemptAnswers(
  attemptId: string,
  answers: StudentAnswer[],
  timeSpentSeconds: number
): Promise<void> {
  const response = await backendFetch(`/attempts/${encodeURIComponent(attemptId)}/answers`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      timeSpentSeconds,
      answers: answers.map(answer => ({
        questionId: answer.questionId,
        answer: answer.selectedOption ? { selected: answer.selectedOption }
          : answer.subAnswers ? { answers: answer.subAnswers }
            : answer.textAnswer != null ? { value: answer.textAnswer }
              : null
      }))
    })
  });
  await response.body?.cancel();
}

export async function submitExam(
  attemptId: string,
  questions: Question[],
  submission: ExamSubmission
): Promise<ExamGradingResult> {
  const questionById = new Map(questions.map(question => [question.id, question]));
  let raw: BackendGradingResult;
  try {
    const response = await backendFetch(`/attempts/${encodeURIComponent(attemptId)}/submit`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        timeSpentSeconds: submission.timeSpentSeconds,
        answers: submission.answers.map(answer => {
          const question = questionById.get(answer.questionId);
          const format = question?.format === 'SingleChoice' ? 'MultipleChoice'
            : question?.format === 'TrueFalseGroup' ? 'TrueFalse'
              : 'ShortAnswer';
          const answerData = answer.selectedOption ? { selected: answer.selectedOption }
            : answer.subAnswers ? { answers: answer.subAnswers }
              : answer.textAnswer != null ? { value: answer.textAnswer }
                : null;
          return { questionId: answer.questionId, format, answer: answerData };
        })
      })
    });
    raw = await response.json();
  } catch (submitError) {
    // A network error can arrive after the server committed the grade. Recover the persisted result.
    try {
      const resultResponse = await backendFetch(`/attempts/${encodeURIComponent(attemptId)}/result`);
      raw = await resultResponse.json();
    } catch {
      throw submitError;
    }
  }
  const submittedAnswers = new Map(submission.answers.map(answer => [answer.questionId, answer]));
  const questionByIdAgain = new Map(questions.map(question => [question.id, question]));
  const details: QuestionGradingDetail[] = (raw.details || []).map((detail, index) => {
    const question = questionByIdAgain.get(detail.questionId);
    const format = question?.format || 'SingleChoice';
    const storedAnswer = detail.answer;
    const restoredAnswer: StudentAnswer = {
      questionId: detail.questionId,
      selectedOption: typeof storedAnswer?.selected === 'string' ? storedAnswer.selected : undefined,
      subAnswers: storedAnswer?.answers && typeof storedAnswer.answers === 'object'
        ? Object.fromEntries(Object.entries(storedAnswer.answers).map(([key, value]) => [key, value === true || value === 'true']))
        : undefined,
      textAnswer: storedAnswer?.value != null ? String(storedAnswer.value) : undefined
    };
    const studentAnswer: StudentAnswer = submittedAnswers.get(detail.questionId) || restoredAnswer;
    return {
      questionId: detail.questionId,
      questionNumber: detail.questionNumber || index + 1,
      partNumber: format === 'SingleChoice' ? 1 : format === 'TrueFalseGroup' ? 2 : 3,
      format,
      content: detail.content || question?.content || '',
      topic: detail.topic || question?.topic || '',
      isCorrect: detail.status === 'Correct',
      scoreEarned: detail.pointsEarned,
      maxScore: format === 'TrueFalseGroup' ? 1 : 0.25,
      studentAnswer,
      correctAnswerData: mapAnswerData(detail.correctAnswerData),
      explanation: detail.explanation,
      distractorReason: studentAnswer.selectedOption
        ? detail.distractorAnalysis?.[studentAnswer.selectedOption]
        : undefined
    };
  });
  return {
    attemptId: raw.attemptId,
    totalScore: raw.totalScore,
    maxScore: raw.maxScore,
    scorePartI: raw.scorePart1,
    scorePartII: raw.scorePart2,
    scorePartIII: raw.scorePart3,
    correctCount: details.filter(item => item.isCorrect).length,
    totalQuestions: details.length,
    timeSpentSeconds: raw.timeSpentSeconds ?? submission.timeSpentSeconds,
    details
  };
}

/**
 * Hỏi Gia sư AI (Context-Aware AI Tutor) với SSE Streaming.
 * Nhận context câu hỏi + lỗi sai của học sinh và stream câu trả lời theo thời gian thực.
 */
export async function streamAiTutorExplanation(
  context: TutorContext,
  onChunk: (chunk: string) => void,
  onDone: () => void,
  onError: (err: Error) => void
): Promise<() => void> {
  const controller = new AbortController();

  // The same-origin BFF forwards the session to the .NET API, which owns the Gemini call and rate limit.
  try {
    const res = await fetch('/api/tutor', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(context),
      signal: controller.signal
    });

    if (!res.ok) {
      const responseBody = await res.text();
      let message = responseBody || `AI tutor request failed (${res.status}).`;
      try {
        const parsed = JSON.parse(responseBody) as { message?: string; error?: string };
        message = parsed.message || parsed.error || message;
      } catch {
        // Keep the response text when the server returned plain text.
      }
      onError(new Error(message));
      return () => controller.abort();
    }
    if (!res.body) {
      onError(new Error('AI tutor response stream is unavailable.'));
      return () => controller.abort();
    }

    {
      const reader = res.body.getReader();
      const decoder = new TextDecoder();

      (async () => {
        let buffer = '';
        let eventType = 'message';
        let ended = false;
        const handleLine = (rawLine: string) => {
          const line = rawLine.replace(/\r$/, '');
          if (!line) {
            eventType = 'message';
            return;
          }
          if (line.startsWith('event:')) {
            eventType = line.slice(6).trim();
            return;
          }
          if (!line.startsWith('data:')) return;
          const chunk = line.slice(5).replace(/^ /, '');
          if (chunk === '[DONE]') {
            ended = true;
            onDone();
          } else if (eventType === 'error') {
            ended = true;
            onError(new Error(chunk || 'AI tutor stream failed.'));
          } else {
            onChunk(chunk);
          }
        };
        try {
          while (true) {
            const { done, value } = await reader.read();
            if (done) {
              buffer += decoder.decode();
              if (buffer) handleLine(buffer);
              break;
            }
            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split('\n');
            buffer = lines.pop() || '';
            for (const line of lines) {
              handleLine(line);
              if (ended) break;
            }
            if (ended) {
              await reader.cancel();
              return;
            }
          }
          if (!ended) onDone();
        } catch (cause) {
          if (cause instanceof Error && cause.name !== 'AbortError') onError(cause);
        }
      })();

      return () => controller.abort();
    }
  } catch {
    if (controller.signal.aborted) return () => controller.abort();
    onError(new Error('AI tutor is temporarily unavailable.'));
    return () => controller.abort();
  }
  return () => controller.abort();
}

export async function getDashboardData(subjectCode: SubjectCode = 'GEOGRAPHY'): Promise<{ stats: DashboardStats; history: ScoreHistoryItem[] }> {
  const [masteryResponse, historyResponse] = await Promise.all([
    backendFetch(`/dashboard/mastery/${backendSubjectCodes[subjectCode]}`),
    backendFetch('/dashboard/history')
  ]);
  const [mastery, allHistory] = await Promise.all([
    masteryResponse.json() as Promise<BackendMastery>,
    historyResponse.json() as Promise<BackendHistoryItem[]>
  ]);
  const relevantHistory = (allHistory || []).filter(item => item.isSubmitted && item.subjectCode === backendSubjectCodes[subjectCode]);
  const topicMasteries: TopicMastery[] = (mastery.topics || []).map(topic => ({
    topic: topic.topic,
    totalQuestions: topic.totalAttempts,
    correctQuestions: topic.totalCorrect,
    accuracyPercent: Math.round(topic.masteryRate * 1000) / 10,
    status: (topic.masteryRate >= 0.8 ? 'Mastered' : topic.masteryRate >= 0.5 ? 'Average' : 'Weak') as TopicMastery['status']
  }));
  const scores = relevantHistory.map(item => Number(item.totalScore) || 0);
  const stats: DashboardStats = {
    totalAttempts: relevantHistory.length,
    averageScore: scores.length ? scores.reduce((sum: number, score: number) => sum + score, 0) / scores.length : 0,
    highestScore: scores.length ? Math.max(...scores) : 0,
    weakestTopics: topicMasteries.filter(topic => topic.totalQuestions > 0).sort((a, b) => a.accuracyPercent - b.accuracyPercent).slice(0, 3).map(topic => topic.topic),
    topicMasteries
  };
  const frontendCode = Object.fromEntries(Object.entries(backendSubjectCodes).map(([key, value]) => [value, key])) as Record<string, SubjectCode>;
  const history: ScoreHistoryItem[] = relevantHistory.map(item => ({
    id: item.id,
    examTitle: `${item.subjectName || 'Bài ôn tập'} · ${new Date(item.startedAt).toLocaleDateString()}`,
    score: Number(item.totalScore) || 0,
    maxScore: 10,
    date: item.submittedAt || item.startedAt,
    subjectCode: frontendCode[item.subjectCode] || subjectCode,
    durationMinutes: item.timeSpentMinutes || 0
  })).reverse();
  return { stats, history };
}

export async function getDashboardStats(subjectCode: SubjectCode = 'GEOGRAPHY'): Promise<DashboardStats> {
  return (await getDashboardData(subjectCode)).stats;
}
export async function submitExamForIngestion(payload: IngestionPayload): Promise<IngestionStatusResponse> {
  const formData = new FormData();
  formData.append('SubjectCode', backendSubjectCodes[payload.subjectCode]);
  formData.append('SourceName', payload.sourceName);
  if (payload.examYear) formData.append('ExamYear', payload.examYear.toString());
  if (payload.file) {
    formData.append('file', payload.file, payload.file.name);
  } else if (payload.rawContent) {
    formData.append('rawContent', payload.rawContent);
  }
  const res = await backendFetch('/admin/ingest', { method: 'POST', body: formData });
  return await res.json();
}

export interface PendingQuestion {
  id: string;
  content: string;
  attachedTable?: string | null;
  format: string;
  difficulty: string;
  topic: string;
  answerData: Record<string, unknown>;
  explanation?: string | null;
  distractorAnalysis: Record<string, string>;
  imageUrls: string[];
  sourceTags: string[];
  createdAt: string;
  subjectCode: string;
  subjectName: string;
}

export async function getPendingQuestions(offset = 0): Promise<PendingQuestion[]> {
  const response = await backendFetch(`/admin/questions/pending?limit=100&offset=${Math.max(0, Math.trunc(offset))}`);
  return response.json();
}

export async function reviewQuestion(questionId: string, action: 'verify' | 'reject', note?: string): Promise<void> {
  await backendFetch(`/admin/questions/${encodeURIComponent(questionId)}/${action}`, {
    method: 'POST',
    headers: action === 'reject' ? { 'Content-Type': 'application/json' } : undefined,
    body: action === 'reject' ? JSON.stringify({ note }) : undefined
  });
}

export async function verifyAllQuestions(questionIds?: string[], subjectCode?: string): Promise<{ updatedCount: number; message: string }> {
  const response = await backendFetch('/admin/questions/verify-all', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ questionIds, subjectCode })
  });
  return response.json();
}

export interface DashboardSummary {
  streakDays: number;
  targetScore: number;
  totalExamsTaken: number;
  totalStudyTimeMinutes: number;
  averageScore: number;
  highestScore: number;
}

export async function getDashboardSummary(): Promise<DashboardSummary> {
  const response = await backendFetch('/dashboard/summary');
  return response.json();
}

export interface ModerationStats {
  totalPending: number;
  totalVerified: number;
  totalRejected: number;
  bySubject: Array<{
    subjectCode: string;
    subjectName: string;
    pending: number;
    verified: number;
    rejected: number;
  }>;
}

export async function getModerationStats(): Promise<ModerationStats> {
  const response = await backendFetch('/admin/questions/stats');
  return response.json();
}

export async function updatePendingQuestion(
  questionId: string,
  payload: {
    content?: string;
    attachedTable?: string | null;
    topic?: string;
    difficulty?: string;
    explanation?: string | null;
    answerData?: Record<string, unknown>;
    distractorAnalysis?: Record<string, string>;
  }
): Promise<PendingQuestion> {
  const response = await backendFetch(`/admin/questions/${encodeURIComponent(questionId)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  });
  return response.json();
}

export interface UserDataExport {
  exportedAt: string;
  userId: string;
  profile: {
    fullName: string;
    grade: number;
    schoolName: string;
    scoreGoals: Record<string, number>;
    role: string;
    createdAt: string;
  } | null;
  attempts: Array<{
    id: string;
    subjectId: string;
    subjectCode: string;
    totalScore: number;
    scorePart1: number;
    scorePart2: number;
    scorePart3: number;
    isSubmitted: boolean;
    startedAt: string;
    submittedAt: string | null;
    timeSpentSeconds: number;
  }>;
  topicMasteries: Array<{
    topic: string;
    totalAttempts: number;
    totalCorrect: number;
    masteryRate: number;
    masteryScore: number;
    lastPracticed: string;
  }>;
  mistakes: Array<{
    questionId: string;
    subjectCode: string;
    studentAnswer: unknown;
    mastered: boolean;
    notes: string | null;
    createdAt: string;
  }>;
}

export async function exportUserData(): Promise<UserDataExport> {
  const response = await backendFetch('/users/me/export');
  return response.json();
}

export async function deleteUserData(): Promise<{ message: string }> {
  const response = await backendFetch('/users/me', {
    method: 'DELETE'
  });
  return response.json();
}

export interface IngestedPaper {
  id: string;
  title: string;
  status: string;
  submittedAt: string;
  subjectCode: string;
  subjectName: string;
  sourceName: string;
  examYear?: number | null;
  rawLength: number;
  rawPreview: string;
  rawContent?: string;
}

export async function getIngestedPapers(): Promise<IngestedPaper[]> {
  const response = await backendFetch('/admin/ingest/papers');
  return response.json();
}

export async function getIngestedPaperDetails(paperId: string): Promise<IngestedPaper> {
  const response = await backendFetch(`/admin/ingest/papers/${encodeURIComponent(paperId)}`);
  return response.json();
}




