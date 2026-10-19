'use client';

import React from 'react';
import { TopicMastery } from '@/types';
import { Flame, ArrowUpRight } from 'lucide-react';
import Link from 'next/link';

interface WeaknessHeatmapProps {
  topicMasteries: TopicMastery[];
}

export const WeaknessHeatmap: React.FC<WeaknessHeatmapProps> = ({
  topicMasteries
}) => {
  const getStatusColor = (percent: number) => {
    if (percent >= 80) return 'text-emerald-400 bg-emerald-500/10 border-emerald-500/30';
    if (percent >= 50) return 'text-amber-400 bg-amber-500/10 border-amber-500/30';
    return 'text-rose-400 bg-rose-500/10 border-rose-500/30';
  };

  const getProgressColor = (percent: number) => {
    if (percent >= 80) return 'from-emerald-500 to-teal-400';
    if (percent >= 50) return 'from-amber-500 to-yellow-400';
    return 'from-rose-500 to-red-400';
  };

  return (
    <div className="glass-panel rounded-3xl p-6 sm:p-8 border border-white/10">
      <div className="flex flex-wrap items-center justify-between gap-4 mb-6">
        <div>
          <div className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-xl bg-indigo-500/20 border border-indigo-500/30 flex items-center justify-center">
              <Flame className="w-4 h-4 text-indigo-400" />
            </div>
            <h2 className="text-lg font-bold text-white">
              Bản đồ Nhiệt Năng lực & Lỗ hổng (Mastery Heatmap)
            </h2>
          </div>
          <p className="text-xs text-slate-400 mt-1">
            Phân tích tỷ lệ đúng theo từng chuyên đề để ôn tập tập trung, không lan man
          </p>
        </div>

        {/* Legend */}
        <div className="flex items-center gap-3 text-xs">
          <span className="flex items-center gap-1.5 text-slate-400">
            <span className="w-2.5 h-2.5 rounded-full bg-emerald-500" /> 🟢 Đã vững (&ge;80%)
          </span>
          <span className="flex items-center gap-1.5 text-slate-400">
            <span className="w-2.5 h-2.5 rounded-full bg-amber-500" /> 🟡 Cần ôn thêm (50-79%)
          </span>
          <span className="flex items-center gap-1.5 text-slate-400">
            <span className="w-2.5 h-2.5 rounded-full bg-rose-500" /> 🔴 Lỗ hổng (&lt;50%)
          </span>
        </div>
      </div>

      {/* Grid of Topics */}
      <div className="space-y-4">
        {topicMasteries.map((item, idx) => {
          const statusClass = getStatusColor(item.accuracyPercent);
          const progressGradient = getProgressColor(item.accuracyPercent);

          return (
            <div
              key={idx}
              className="p-4 sm:p-5 rounded-2xl bg-slate-900/60 border border-white/5 hover:border-white/15 transition-all group"
            >
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-3">
                <div className="flex items-center gap-3">
                  <span className="w-6 h-6 rounded-lg bg-white/5 font-mono text-xs text-slate-400 flex items-center justify-center font-bold">
                    {idx + 1}
                  </span>
                  <span className="text-sm sm:text-base font-semibold text-slate-200">
                    {item.topic}
                  </span>
                </div>

                <div className="flex items-center gap-3 self-end sm:self-center">
                  <span
                    className={`px-3 py-1 rounded-xl text-xs font-bold border ${statusClass}`}
                  >
                    Đúng {item.correctQuestions}/{item.totalQuestions} ({item.accuracyPercent}%)
                  </span>

                  {item.accuracyPercent < 50 ? (
                    <Link
                      href={`/exam?mode=drill&topic=${encodeURIComponent(item.topic)}`}
                      className="flex items-center gap-1.5 px-3 py-1 rounded-xl text-xs font-bold bg-rose-600 text-white hover:bg-rose-500 shadow-md shadow-rose-600/30 transition-all hover:scale-105"
                    >
                      <span>Luyện ngay</span>
                      <ArrowUpRight className="w-3.5 h-3.5" />
                    </Link>
                  ) : (
                    <Link
                      href={`/exam?mode=drill&topic=${encodeURIComponent(item.topic)}`}
                      className="flex items-center gap-1 text-xs font-semibold text-indigo-400 hover:text-indigo-300 transition-colors"
                    >
                      <span>Ôn luyện</span>
                      <ArrowUpRight className="w-3.5 h-3.5" />
                    </Link>
                  )}
                </div>
              </div>

              {/* Progress Bar */}
              <div className="w-full bg-slate-950/80 rounded-full h-2.5 overflow-hidden border border-white/5">
                <div
                  className={`bg-gradient-to-r ${progressGradient} h-2.5 rounded-full transition-all duration-500`}
                  style={{ width: `${item.accuracyPercent}%` }}
                />
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
