import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "OmniPrep (Prep12.vn) - Ôn thi Tốt nghiệp THPT Thông minh 2025",
  description: "Nền tảng ôn thi THPT chuẩn cấu trúc 3 phần Bộ GD&ĐT 2025, tự động cào đề, khử trùng pgvector và Gia sư AI ngữ cảnh Gemini 2.0 (0 VNĐ/tháng).",
};

import { AuthProvider } from "@/context/AuthContext";
export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-full flex flex-col">
        <AuthProvider>{children}</AuthProvider>
      </body>
    </html>
  );
}
