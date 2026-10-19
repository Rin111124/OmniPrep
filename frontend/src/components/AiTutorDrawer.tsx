'use client';

import React, { useCallback, useEffect, useState, useRef } from 'react';
import { Question, StudentAnswer, TutorContext } from '@/types';
import { streamAiTutorExplanation } from '@/lib/api';
import { Sparkles, X, Send, Bot, User, RefreshCw, Lightbulb } from 'lucide-react';

interface AiTutorDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  question: Question;
  studentAnswer?: StudentAnswer;
}

export const AiTutorDrawer: React.FC<AiTutorDrawerProps> = ({
  isOpen,
  onClose,
  question,
  studentAnswer
}) => {
  const [messages, setMessages] = useState<Array<{ sender: 'ai' | 'user'; text: string }>>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [inputQuery, setInputQuery] = useState('');
  const abortRef = useRef<(() => void) | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);

  // Auto scroll to bottom of chat
  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages, isStreaming]);

  // Khởi động phân tích ngữ cảnh ngay khi mở Drawer
  const triggerTutorExplanation = useCallback((customQuery?: string) => {
    if (abortRef.current) abortRef.current();

    setIsStreaming(true);

    if (customQuery) {
      setMessages(prev => [...prev, { sender: 'user', text: customQuery }]);
    }

    // Placeholder message for streaming
    setMessages(prev => [...prev, { sender: 'ai', text: '' }]);

    const studentOpt = studentAnswer?.selectedOption;
    const distractorReason = studentOpt && question.distractorAnalysis 
      ? question.distractorAnalysis[studentOpt] 
      : undefined;

    let parsedTable = question.dataTable || null;
    if (!parsedTable && question.attachedTable) {
      try {
        parsedTable = JSON.parse(question.attachedTable);
      } catch {
        // ignore
      }
    }

    const studentWrong = studentOpt || studentAnswer?.textAnswer || (studentAnswer?.subAnswers ? JSON.stringify(studentAnswer.subAnswers) : undefined);

    const context: TutorContext = {
      questionId: question.id,
      questionContent: question.content,
      questionFormat: question.format,
      studentSelection: studentOpt || studentAnswer?.textAnswer,
      correctAnswer: question.answerData.correctOption || question.answerData.correctAnswer || (question.answerData.subAnswers ? JSON.stringify(question.answerData.subAnswers) : undefined),
      distractorAnalysis: distractorReason,
      explanation: question.explanation,
      userQuery: customQuery,
      // Hidden payload:
      dataTable: parsedTable,
      userWrongAnswer: studentWrong,
      userWeakTopic: question.topic
    };

    streamAiTutorExplanation(
      context,
      (chunk) => {
        setMessages(prev => {
          const next = [...prev];
          const lastIndex = next.length - 1;
          if (lastIndex >= 0 && next[lastIndex].sender === 'ai') {
            next[lastIndex] = {
              ...next[lastIndex],
              text: next[lastIndex].text + chunk
            };
          }
          return next;
        });
      },
      () => {
        setIsStreaming(false);
      },
      (err) => {
        console.error('Tutor stream error', err);
        setIsStreaming(false);
      }
    ).then(abortFn => {
      abortRef.current = abortFn;
    });
  }, [question, studentAnswer]);

  useEffect(() => {
    if (!isOpen) {
      abortRef.current?.();
      return;
    }

    const timer = window.setTimeout(() => {
      setMessages([]);
      triggerTutorExplanation();
    }, 0);

    return () => {
      window.clearTimeout(timer);
      abortRef.current?.();
    };
  }, [isOpen, question.id, triggerTutorExplanation]);
  const handleSendMessage = (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputQuery.trim() || isStreaming) return;
    const q = inputQuery.trim();
    setInputQuery('');
    triggerTutorExplanation(q);
  };

  const handleQuickPrompt = (promptText: string) => {
    if (isStreaming) return;
    triggerTutorExplanation(promptText);
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/60 backdrop-blur-sm transition-opacity">
      <div className="w-full max-w-xl h-full glass-panel border-l border-white/10 flex flex-col shadow-2xl animate-in slide-in-from-right duration-300">
        {/* Drawer Header */}
        <div className="p-4 sm:p-5 border-b border-white/10 flex items-center justify-between bg-gradient-to-r from-slate-900 via-indigo-950/60 to-violet-950/80">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-2xl bg-gradient-to-tr from-violet-600 via-indigo-600 to-cyan-400 flex items-center justify-center shadow-lg shadow-violet-600/30">
              <Sparkles className="w-5 h-5 text-white" />
            </div>
            <div>
              <h3 className="text-sm font-bold text-white flex items-center gap-2">
                Gia sư AI Ngữ cảnh (Context-Aware)
                <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-violet-500/20 text-violet-300 border border-violet-500/30">
                  AI Tutor
                </span>
              </h3>
              <p className="text-xs text-slate-400">
                Chuyên gia bóc tách bẫy đề thi THPT 2018 theo lỗi sai của em
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="w-8 h-8 rounded-lg bg-white/5 border border-white/10 flex items-center justify-center text-slate-400 hover:text-white hover:bg-white/10 transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Question Context Snippet */}
        <div className="px-5 py-3 bg-slate-950/60 border-b border-white/5 text-xs text-slate-300 flex items-center justify-between">
          <div className="truncate mr-2">
            <span className="font-bold text-violet-300">Chuyên đề: </span>
            <span className="text-slate-400 mr-2">{question.topic}</span>
            <span className="font-bold text-indigo-300">Nội dung: </span>
            <span className="text-slate-300 truncate">{question.content.slice(0, 60)}...</span>
          </div>
        </div>

        {/* Chat Stream History */}
        <div ref={scrollRef} className="flex-1 overflow-y-auto p-4 sm:p-6 space-y-4">
          {messages.map((msg, i) => (
            <div
              key={i}
              className={`flex gap-3 ${msg.sender === 'user' ? 'justify-end' : 'justify-start'}`}
            >
              {msg.sender === 'ai' && (
                <div className="flex-shrink-0 w-8 h-8 rounded-xl bg-violet-600/30 border border-violet-500/40 flex items-center justify-center mt-1 shadow-sm">
                  <Bot className="w-4 h-4 text-violet-300" />
                </div>
              )}

              <div
                className={`max-w-[85%] rounded-2xl p-4 text-sm leading-relaxed ${
                  msg.sender === 'user'
                    ? 'bg-indigo-600 text-white shadow-md'
                    : 'bg-slate-900/90 text-slate-200 border border-white/10 shadow-lg whitespace-pre-wrap'
                }`}
              >
                {msg.text || (
                  <div className="flex items-center gap-2 text-slate-400">
                    <span className="inline-block w-2 h-2 rounded-full bg-violet-400 animate-pulse" />
                    <span>Gia sư AI đang phân tích bài làm và bẫy đề thi của em...</span>
                  </div>
                )}
              </div>

              {msg.sender === 'user' && (
                <div className="flex-shrink-0 w-8 h-8 rounded-xl bg-indigo-600 flex items-center justify-center mt-1">
                  <User className="w-4 h-4 text-white" />
                </div>
              )}
            </div>
          ))}
        </div>

        {/* 3 Quick Prompts Bar */}
        <div className="px-4 py-2.5 bg-slate-950/80 border-t border-white/5 flex items-center gap-2 overflow-x-auto text-xs">
          <button
            onClick={() => handleQuickPrompt('Giải thích dễ hiểu hơn câu này')}
            disabled={isStreaming}
            className="flex-shrink-0 px-3 py-1.5 rounded-xl bg-violet-500/10 border border-violet-500/30 hover:bg-violet-500/20 text-violet-200 hover:text-white transition-all flex items-center gap-1.5 disabled:opacity-40"
          >
            <Lightbulb className="w-3.5 h-3.5 text-violet-400" />
            <span>Giải thích dễ hiểu hơn câu này</span>
          </button>
          <button
            onClick={() => handleQuickPrompt('Mẹo làm nhanh / Công thức rút gọn')}
            disabled={isStreaming}
            className="flex-shrink-0 px-3 py-1.5 rounded-xl bg-indigo-500/10 border border-indigo-500/30 hover:bg-indigo-500/20 text-indigo-200 hover:text-white transition-all flex items-center gap-1.5 disabled:opacity-40"
          >
            <Sparkles className="w-3.5 h-3.5 text-indigo-400" />
            <span>Mẹo làm nhanh / Công thức rút gọn</span>
          </button>
          <button
            onClick={() => handleQuickPrompt('Cho em 1 câu tương tự để làm lại')}
            disabled={isStreaming}
            className="flex-shrink-0 px-3 py-1.5 rounded-xl bg-cyan-500/10 border border-cyan-500/30 hover:bg-cyan-500/20 text-cyan-200 hover:text-white transition-all flex items-center gap-1.5 disabled:opacity-40"
          >
            <RefreshCw className="w-3.5 h-3.5 text-cyan-400" />
            <span>Cho em 1 câu tương tự để làm lại</span>
          </button>
        </div>

        {/* Input Bar */}
        <form onSubmit={handleSendMessage} className="p-4 border-t border-white/10 bg-slate-900/90">
          <div className="flex items-center gap-2">
            <input
              type="text"
              value={inputQuery}
              onChange={(e) => setInputQuery(e.target.value)}
              placeholder="Đặt câu hỏi thêm cho Gia sư AI..."
              className="flex-1 bg-slate-950/80 border border-white/10 focus:border-cyan-400 focus:ring-1 focus:ring-cyan-400/30 rounded-xl px-4 py-2.5 text-sm text-slate-100 outline-none transition-all"
            />
            <button
              type="submit"
              disabled={isStreaming || !inputQuery.trim()}
              className="w-10 h-10 rounded-xl bg-gradient-to-tr from-cyan-500 to-indigo-600 flex items-center justify-center text-white disabled:opacity-40 disabled:cursor-not-allowed hover:scale-105 transition-transform"
            >
              <Send className="w-4 h-4" />
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
