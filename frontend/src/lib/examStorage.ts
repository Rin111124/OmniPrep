export type ExamStorageKind = 'active_exam' | 'timer' | 'answers' | 'result' | 'questions' | 'flagged' | 'mistakes';

/** Prevents multiple signed-in accounts on a shared browser profile from reusing each other's exam state. */
export function examStorageKey(userId: string, kind: ExamStorageKind, examId: string): string {
  if (!userId) throw new Error('An authenticated user is required to access exam storage.');
  return `omniprep_user_${userId}_${kind}_${examId}`;
}

export function readExamStorage(key: string): string | null {
  try { return localStorage.getItem(key); } catch { return null; }
}

export function writeExamStorage(key: string, value: string): boolean {
  try { localStorage.setItem(key, value); return true; } catch { return false; }
}

export function removeExamStorage(key: string): void {
  try { localStorage.removeItem(key); } catch { /* Storage can be disabled or full. */ }
}
