import type { Metadata, Viewport } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";
import { AuthProvider } from "@/context/AuthContext";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  maximumScale: 5,
  userScalable: true,
  themeColor: "#07090e",
  colorScheme: "dark",
};

export const metadata: Metadata = {
  title: "OmniPrep (Prep12.vn) - Ôn thi Tốt nghiệp THPT Thông minh 2025",
  description: "Nền tảng ôn thi THPT chuẩn cấu trúc 3 phần Bộ GD&ĐT 2025, tự động cào đề, khử trùng pgvector và Gia sư AI ngữ cảnh Gemini 2.0 (0 VNĐ/tháng).",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="vi"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-dvh flex flex-col bg-[#07090e] text-[#f1f5f9] overflow-x-hidden selection:bg-indigo-500/30 selection:text-indigo-200">
        <AuthProvider>{children}</AuthProvider>
      </body>
    </html>
  );
}

