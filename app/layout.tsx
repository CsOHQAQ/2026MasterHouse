import type { Metadata } from "next";
import { Noto_Sans_SC, Space_Mono } from "next/font/google";
import "./globals.css";

const noto = Noto_Sans_SC({
  variable: "--font-noto",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
});

const mono = Space_Mono({
  variable: "--font-mono",
  subsets: ["latin"],
  weight: ["400", "700"],
});

export const metadata: Metadata = {
  title: "Sweet House · 局外系统 Demo",
  description: "访客经营叙事游戏的 House 局外 UI/UX 交互原型。",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="zh-CN"><body className={`${noto.variable} ${mono.variable}`}>{children}</body></html>;
}
