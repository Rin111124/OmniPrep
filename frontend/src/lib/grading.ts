// ============================================================
// Bộ thuật toán chấm điểm chuẩn 3 phần theo Quyết định Bộ GD&ĐT 2025
// Tương thích 100% với logic OmniPrep.Infrastructure/Services/GradingService.cs
// ============================================================

import { ExamGradingResult, ExamSubmission, Question, QuestionGradingDetail, StudentAnswer } from '@/types';

export function gradeExamLocally(
  examId: string,
  questions: Question[],
  submission: ExamSubmission
): ExamGradingResult {
  let scorePartI = 0;
  let scorePartII = 0;
  let scorePartIII = 0;
  let correctCount = 0;

  const answerMap = new Map<string, StudentAnswer>();
  for (const ans of submission.answers) {
    answerMap.set(ans.questionId, ans);
  }

  const details: QuestionGradingDetail[] = [];

  questions.forEach((q, index) => {
    const studentAns = answerMap.get(q.id) || { questionId: q.id };
    const qNum = index + 1;

    let isCorrect = false;
    let earned = 0;
    let max = 0.25;
    let partNum: 1 | 2 | 3 = 1;
    let correctSubCount = 0;

    if (q.format === 'SingleChoice') {
      partNum = 1;
      max = 0.25;
      const studentOpt = (studentAns.selectedOption || '').trim().toUpperCase();
      const correctOpt = (q.answerData.correctOption || '').trim().toUpperCase();

      if (studentOpt && studentOpt === correctOpt) {
        isCorrect = true;
        earned = 0.25;
        correctCount++;
      }
      scorePartI += earned;
    } 
    else if (q.format === 'TrueFalseGroup') {
      partNum = 2;
      max = 1.0;
      const correctSubs = q.answerData.subAnswers || {};
      const studentSubs = studentAns.subAnswers || {};

      for (const key of ['a', 'b', 'c', 'd']) {
        if (studentSubs[key] !== undefined && studentSubs[key] === correctSubs[key]) {
          correctSubCount++;
        }
      }

      // Thang điểm lũy tiến Bộ GD&ĐT:
      // 1 ý = 0.1đ | 2 ý = 0.25đ | 3 ý = 0.50đ | 4 ý = 1.00đ
      switch (correctSubCount) {
        case 1: earned = 0.10; break;
        case 2: earned = 0.25; break;
        case 3: earned = 0.50; break;
        case 4: earned = 1.00; isCorrect = true; correctCount++; break;
        default: earned = 0.0; break;
      }
      scorePartII += earned;
    } 
    else if (q.format === 'ShortAnswer') {
      partNum = 3;
      max = 0.25;
      const studentText = (studentAns.textAnswer || '').trim();
      const isMatch = checkShortAnswerMatch(
        studentText,
        q.answerData.correctAnswer || '',
        q.answerData.acceptedAnswers || [],
        q.answerData.numericTolerance ?? 0.05
      );

      if (isMatch) {
        isCorrect = true;
        earned = 0.25;
        correctCount++;
      }
      scorePartIII += earned;
    }

    const distractorReason = studentAns.selectedOption && q.distractorAnalysis 
      ? q.distractorAnalysis[studentAns.selectedOption] 
      : undefined;

    details.push({
      questionId: q.id,
      questionNumber: qNum,
      partNumber: partNum,
      format: q.format,
      content: q.content,
      topic: q.topic,
      isCorrect,
      scoreEarned: earned,
      maxScore: max,
      studentAnswer: studentAns,
      correctAnswerData: q.answerData,
      explanation: q.explanation,
      distractorReason,
      correctSubCount: q.format === 'TrueFalseGroup' ? correctSubCount : undefined
    });
  });

  const totalScore = Math.round((scorePartI + scorePartII + scorePartIII) * 100) / 100;

  return {
    attemptId: 'attempt-' + Date.now(),
    totalScore,
    maxScore: 10.0,
    scorePartI: Math.round(scorePartI * 100) / 100,
    scorePartII: Math.round(scorePartII * 100) / 100,
    scorePartIII: Math.round(scorePartIII * 100) / 100,
    correctCount,
    totalQuestions: questions.length,
    timeSpentSeconds: submission.timeSpentSeconds,
    details
  };
}

function checkShortAnswerMatch(
  studentInput: string,
  correctAnswer: string,
  acceptedList: string[],
  tolerance: number
): boolean {
  if (!studentInput) return false;

  const normalize = (val: string) => val.trim().toLowerCase().replace(',', '.').replace(/[%°C\sđ]/g, '');

  const normStudent = normalize(studentInput);
  const normCorrect = normalize(correctAnswer);

  if (normStudent === normCorrect) return true;

  for (const acc of acceptedList) {
    if (normStudent === normalize(acc)) return true;
  }

  // Thử parse sang số thực và kiểm tra dung sai số học
  const numStudent = parseFloat(normStudent);
  const numCorrect = parseFloat(normCorrect);

  if (!isNaN(numStudent) && !isNaN(numCorrect)) {
    return Math.abs(numStudent - numCorrect) <= tolerance + 1e-6;
  }

  return false;
}
