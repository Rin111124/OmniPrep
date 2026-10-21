// ============================================================
// OmniPrep TypeScript Types & Domain Interfaces
// Đồng bộ với .NET Domain & Supabase Database
// ============================================================

export type SubjectCode = 
  | 'GEOGRAPHY' 
  | 'LAW_ECONOMICS' 
  | 'MATHEMATICS' 
  | 'PHYSICS' 
  | 'CHEMISTRY' 
  | 'BIOLOGY' 
  | 'HISTORY' 
  | 'ENGLISH';

export type QuestionFormat = 'SingleChoice' | 'TrueFalseGroup' | 'ShortAnswer';
export type DifficultyLevel = 'Recognition' | 'Comprehension' | 'Application' | 'HighApplication';

export interface Subject {
  id: string;
  code: SubjectCode;
  name: string;
  description?: string;
  totalScore: number;
  topics: string[];
  examMatrix?: ExamMatrix;
  activeQuestionsCount?: number;
  isExamSupported?: boolean;
  isExamReady?: boolean;
  questionAvailability?: {
    part1: { available: number; required: number };
    part2: { available: number; required: number };
    part3: { available: number; required: number };
  };
}

export interface ExamMatrix {
  partI_SingleChoice_Count: number;
  partII_TrueFalseGroup_Count: number;
  partIII_ShortAnswer_Count: number;
  topicsDistribution?: Record<string, number>;
}

// BGD Form 2025 Question Data
export interface AnswerData {
  // Part I: Single choice
  options?: Record<string, string>; // { A: "...", B: "...", C: "...", D: "..." }
  correctOption?: string; // "A" | "B" | "C" | "D"

  // Part II: True/False group (a, b, c, d)
  subQuestions?: Record<string, string>; // { a: "Ý a...", b: "Ý b..." }
  subAnswers?: Record<string, boolean>; // { a: true, b: false, ... }

  // Part III: Short answer
  correctAnswer?: string; // e.g. "25.4"
  acceptedAnswers?: string[]; // ["25.4", "25,4"]
  numericTolerance?: number; // 0.1
  unit?: string; // "%", "triệu người", "tỉ USD"
}

export interface DataTable {
  title?: string;
  unit?: string;
  headers: string[];
  rows: string[][];
}

export interface Question {
  id: string;
  subjectId: string;
  content: string;
  attachedTable?: string | null;
  dataTable?: DataTable | null;
  format: QuestionFormat;
  difficulty: DifficultyLevel;
  topic: string;
  answerData: AnswerData;
  explanation?: string;
  distractorAnalysis?: Record<string, string>;
  shortAnswerWrongTraps?: Record<string, string>;
  sourceTags: string[];
  firstSeenYear?: number;
  imageUrls?: string[];
}

export interface ExamQuestionView extends Question {
  questionNumber: number;
  partNumber: 1 | 2 | 3;
}

// User Roles & RBAC
export type UserRole = 'STUDENT' | 'TEACHER' | 'ADMIN';

// User Profile & Gamification
export interface UserProfile {
  id: string;
  name: string;
  email: string;
  avatarUrl: string;
  role: UserRole;
  streakDays: number;
  targetScore: number;
  totalXp: number;
  isAuthenticated: boolean;
}

export interface ScoreHistoryItem {
  id: string;
  examTitle: string;
  score: number;
  maxScore: number;
  date: string;
  subjectCode: SubjectCode;
  durationMinutes: number;
}

// Student Submissions
export interface StudentAnswer {
  questionId: string;
  selectedOption?: string; // Part I: "A" | "B" | "C" | "D"
  subAnswers?: Record<string, boolean>; // Part II: { a: true, b: false }
  textAnswer?: string; // Part III: "25.4"
}

export interface ExamSubmission {
  examId: string;
  timeSpentSeconds: number;
  answers: StudentAnswer[];
}

// Grading Results
export interface QuestionGradingDetail {
  questionId: string;
  questionNumber: number;
  partNumber: 1 | 2 | 3;
  format: QuestionFormat;
  content: string;
  topic: string;
  isCorrect: boolean;
  scoreEarned: number;
  maxScore: number;
  studentAnswer: StudentAnswer;
  correctAnswerData: AnswerData;
  explanation?: string;
  distractorReason?: string;
  correctSubCount?: number; // For Part II
}

export interface ExamGradingResult {
  attemptId: string;
  totalScore: number;
  maxScore: number;
  scorePartI: number;
  scorePartII: number;
  scorePartIII: number;
  correctCount: number;
  totalQuestions: number;
  timeSpentSeconds: number;
  details: QuestionGradingDetail[];
}

// AI Tutor SSE Stream
export interface TutorContext {
  questionId: string;
  questionContent: string;
  questionFormat: QuestionFormat;
  studentSelection?: string;
  correctAnswer?: string;
  distractorAnalysis?: string;
  explanation?: string;
  userQuery?: string;
  // Context-Aware Hidden Payload:
  dataTable?: DataTable | null;
  userWrongAnswer?: string;
  userWeakTopic?: string;
}

// Heatmap / Competency Analytics
export interface TopicMastery {
  topic: string;
  totalQuestions: number;
  correctQuestions: number;
  accuracyPercent: number;
  status: 'Mastered' | 'Average' | 'Weak';
}

export interface DashboardStats {
  totalAttempts: number;
  averageScore: number;
  highestScore: number;
  weakestTopics: string[];
  topicMasteries: TopicMastery[];
}

// Ingestion
export interface IngestionPayload {
  subjectCode: SubjectCode;
  sourceName: string;
  examYear?: number;
  rawContent: string;
  file?: File;
}

export interface IngestionStatusResponse {
  paperId: string;
  status: 'Pending' | 'Processing' | 'Completed' | 'Failed';
  totalExtracted: number;
  totalNew: number;
  totalMerged: number;
  totalFailed: number;
  part1Count?: number;
  part2Count?: number;
  part3Count?: number;
  errors?: string[];
  message?: string;
}

// Mistake Notebook & Drill
export interface MistakeItem {
  id: string;
  question: Question;
  studentAnswer: StudentAnswer;
  subjectCode: SubjectCode;
  createdAt: string;
  mastered: boolean;
  notes?: string;
}

export interface DrillConfig {
  subjectCode: SubjectCode;
  topic?: string;
  count: number;
  format?: QuestionFormat;
}

export interface ExamSource {
  id: string;
  name: string;
  province?: string | null;
  sourceType: string;
  websiteUrl?: string | null;
  isActive: boolean;
}

export interface ScannedSourceItem {
  name: string;
  province?: string | null;
  sourceType: string;
  totalQuestions: number;
  part1Count: number;
  part2Count: number;
  part3Count: number;
  isFullMatrix: boolean;
  coverageScore: number;
}

export interface SubjectSourceScanResult {
  subjectCode: string;
  subjectName: string;
  totalAvailableQuestions: number;
  requiredMatrix: {
    part1: number;
    part2: number;
    part3: number;
    total: number;
  };
  recommendedSource: string;
  sources: ScannedSourceItem[];
}
