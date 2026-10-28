import type { Download } from "./types";

export function isDownloadComplete(state: string) {
  return state === "已完成 · 校验通过" || state === "已完成 · 已打开工具";
}

export function magpieDownload(downloads: Download[], command: "magpie" | "amdMagpie") {
  const title = command === "amdMagpie" ? "AMD 大力喜鹊" : "大力喜鹊 · Magpie";
  return downloads.find(d => d.independent && d.title === title &&
    !isDownloadComplete(d.state) && d.state !== "下载失败" && d.state !== "已取消");
}
