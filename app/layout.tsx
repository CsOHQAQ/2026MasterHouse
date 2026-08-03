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
  title: "Guesthouse of Meros · 局外系统 Demo",
  description: "访客经营叙事游戏 Guesthouse of Meros 的局外 UI/UX 交互原型。",
  metadataBase: new URL("https://sweet-house-meta-demo.fanncai888.chatgpt.site"),
  openGraph: {
    title: "Guesthouse of Meros · New Life, New Home",
    description: "手绘叙事经营游戏的访客到访与 Guesthouse 局外 UI/UX 原型。",
    images: [{ url: "/og-meros.png", width: 1680, height: 945, alt: "四位动物访客抵达 Guesthouse of Meros" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Guesthouse of Meros · New Life, New Home",
    description: "手绘叙事经营游戏的访客到访与 Guesthouse 局外 UI/UX 原型。",
    images: ["/og-meros.png"],
  },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="zh-CN"><body className={`${noto.variable} ${mono.variable}`}>{children}</body></html>;
}
