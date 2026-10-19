// ============================================================
// OmniPrep - Sổ tay Bẫy Đề Thi (Mistake Notebook Storage)
// Quản lý các câu hỏi làm sai, phân tích bẫy và chế độ luyện lại
// Hỗ trợ lưu trữ cục bộ (localStorage) và đồng bộ hai chiều với máy chủ
// ============================================================

import { ExamGradingResult, MistakeItem, Question, SubjectCode } from '@/types';
import { examStorageKey } from '@/lib/examStorage';

function storageKey(userId: string): string {
  return examStorageKey(userId, 'mistakes', 'v1');
}

export function getMistakes(userId: string): MistakeItem[] {
  if (typeof window === 'undefined' || !userId) return [];

  try {
    const raw = localStorage.getItem(storageKey(userId));
    if (!raw) return [];
    return JSON.parse(raw);
  } catch {
    return [];
  }
}

export function saveMistakes(userId: string, items: MistakeItem[]): void {
  if (typeof window === 'undefined' || !userId) return;
  try {
    localStorage.setItem(storageKey(userId), JSON.stringify(items));
  } catch (e) {
    console.error('Failed to save mistakes to localStorage', e);
  }
}

type ServerMistakeResponse = {
  id: string;
  questionId: string;
  subjectCode: SubjectCode;
  studentAnswer: Record<string, unknown> | null;
  mastered: boolean;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
  question: Question;
};

/**
 * Đồng bộ hai chiều với máy chủ: tải danh sách câu sai mới nhất từ server
 * và đẩy các câu sai chưa đồng bộ lên server.
 */
export async function syncMistakesWithServer(userId: string): Promise<MistakeItem[]> {
  const localMistakes = getMistakes(userId);
  if (typeof window === 'undefined' || !userId) return localMistakes;

  try {
    const res = await fetch('/api/backend/mistakes', {
      method: 'GET',
      headers: { Accept: 'application/json' }
    });

    if (!res.ok) return localMistakes;

    const serverItems: ServerMistakeResponse[] = await res.json();
    const serverMap = new Map<string, ServerMistakeResponse>(
      serverItems.map(item => [item.questionId, item])
    );

    // Tìm các câu ở local mà server chưa có để sync lên
    const unsyncedItems = localMistakes.filter(m => !serverMap.has(m.question.id));
    if (unsyncedItems.length > 0) {
      void fetch('/api/backend/mistakes/sync', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          items: unsyncedItems.map(m => ({
            questionId: m.question.id,
            studentAnswer: m.studentAnswer,
            mastered: m.mastered,
            notes: m.notes
          }))
        })
      }).catch(err => console.warn('Background sync to server failed:', err));
    }

    // Kết hợp: ưu tiên câu hỏi từ server (vì có metadata chuẩn từ DB)
    const mergedMap = new Map<string, MistakeItem>();

    // 1. Nạp items từ server
    for (const s of serverItems) {
      mergedMap.set(s.questionId, {
        id: s.id,
        question: s.question,
        studentAnswer: (s.studentAnswer as unknown as MistakeItem['studentAnswer']) || {},
        subjectCode: s.subjectCode,
        createdAt: s.createdAt,
        mastered: s.mastered,
        notes: s.notes || undefined
      });
    }

    // 2. Bổ sung items chỉ có ở local
    for (const l of localMistakes) {
      if (!mergedMap.has(l.question.id)) {
        mergedMap.set(l.question.id, l);
      }
    }

    const mergedList = Array.from(mergedMap.values()).sort(
      (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
    );

    saveMistakes(userId, mergedList);
    return mergedList;
  } catch (error) {
    console.warn('Sync with server failed, fallback to local storage:', error);
    return localMistakes;
  }
}

export function saveMistakesFromResult(
  result: ExamGradingResult,
  questions: Question[],
  userId: string,
  subjectCode: SubjectCode = 'GEOGRAPHY'
): number {
  const current = getMistakes(userId);
  const questionMap = new Map(questions.map(q => [q.id, q]));
  let newAddedCount = 0;
  const newItemsToSync: Array<{ questionId: string; studentAnswer: unknown; mastered: boolean; notes: string }> = [];

  for (const detail of result.details) {
    if (!detail.isCorrect) {
      const q = questionMap.get(detail.questionId);
      if (!q) continue;

      // Check if already in mistake notebook
      const existingIdx = current.findIndex(m => m.question.id === q.id);
      if (existingIdx >= 0) {
        // Update student answer and unmark mastered
        current[existingIdx].studentAnswer = detail.studentAnswer;
        current[existingIdx].mastered = false;
        current[existingIdx].createdAt = new Date().toISOString();
        newItemsToSync.push({
          questionId: q.id,
          studentAnswer: detail.studentAnswer,
          mastered: false,
          notes: detail.distractorReason || 'Làm sai trong đề thi thử'
        });
      } else {
        const item: MistakeItem = {
          id: `mistake-${q.id}-${Date.now()}`,
          question: q,
          studentAnswer: detail.studentAnswer,
          subjectCode,
          createdAt: new Date().toISOString(),
          mastered: false,
          notes: detail.distractorReason || 'Làm sai trong đề thi thử'
        };
        current.unshift(item);
        newAddedCount++;
        newItemsToSync.push({
          questionId: q.id,
          studentAnswer: detail.studentAnswer,
          mastered: false,
          notes: detail.distractorReason || 'Làm sai trong đề thi thử'
        });
      }
    }
  }

  saveMistakes(userId, current);

  // Sync to backend if authenticated
  if (userId && newItemsToSync.length > 0) {
    void fetch('/api/backend/mistakes/sync', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ items: newItemsToSync })
    }).catch(e => console.warn('Syncing new mistakes to server failed:', e));
  }

  return newAddedCount;
}

export function toggleMistakeMastered(userId: string, id: string): void {
  const current = getMistakes(userId);
  const updated = current.map(item =>
    item.id === id ? { ...item, mastered: !item.mastered } : item
  );
  saveMistakes(userId, updated);

  // Async sync to server
  if (userId && id && !id.startsWith('mistake-')) {
    void fetch(`/api/backend/mistakes/${id}/toggle-mastered`, {
      method: 'PATCH'
    }).catch(e => console.warn('Failed to toggle mastered on server:', e));
  }
}

export function deleteMistake(userId: string, id: string): void {
  const current = getMistakes(userId);
  const filtered = current.filter(item => item.id !== id);
  saveMistakes(userId, filtered);

  if (userId && id && !id.startsWith('mistake-')) {
    void fetch(`/api/backend/mistakes/${id}`, {
      method: 'DELETE'
    }).catch(e => console.warn('Failed to delete mistake on server:', e));
  }
}

export function clearAllMistakes(userId: string): void {
  saveMistakes(userId, []);

  if (userId) {
    void fetch('/api/backend/mistakes', {
      method: 'DELETE'
    }).catch(e => console.warn('Failed to clear mistakes on server:', e));
  }
}
