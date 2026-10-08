# MU 宣传片

30 秒游戏科技风宣传片，Remotion 4.0.533 / React 19。横竖版分别构图，60 fps；源文件与最终视频均位于本目录。

## 成片

- `out/mu-promo-16x9.mp4`：1920 × 1080，官网 / B 站。
- `out/mu-promo-9x16.mp4`：1080 × 1920，抖音 / 视频号。
- `preview.html`：双版本本地播放页，通过下面的 HTTP 命令打开。
- `out/verification.json`：导出后生成的媒体校验记录。

## 分镜

| 时间 | 画面 | 文案 |
| --- | --- | --- |
| 0–3.75 s | MU 芯片品牌图，镜头推进 | 下一局，由你掌控 |
| 3.75–7.5 s | 原创程序化霓虹城市穿行 | 把注意力留给游戏 |
| 7.5–13.12 s | 按 MU 当前视觉重绘游戏库，选择游戏 | 你的游戏，一个主场 |
| 13.12–18.75 s | 三种方案卡片依次点亮 | 三种方案，一个入口 |
| 18.75–22.5 s | 原始文件记录、配置、恢复状态 | 放心配置，有路可回 |
| 22.5–26.25 s | MU 标志与光束 | 游戏配置，少点折腾 |
| 26.25–30 s | MU 品牌主图和官网地址 | 少点折腾，即刻开局 |

## 运行

使用 Node.js 22+，在本目录执行：

```sh
npm ci
npm run assets
npm run audio
npm run check
npm run studio
```

导出与验证：

```sh
npm run render
node scripts/verify.mjs
```

关键帧：`node scripts/render.mjs stills`。

低分辨率预览：`node scripts/render.mjs preview`，保持原始时间轴与帧率。

单独导出：`node scripts/render.mjs MU-Landscape` 或 `node scripts/render.mjs MU-Portrait`。

双版本播放页：`node scripts/preview-server.mjs`，访问 `http://127.0.0.1:4189`。

验证脚本需 PATH 中存在 `ffmpeg` / `ffprobe`。Remotion 首次运行自动获取其 Chrome Headless Shell。渲染并发设为 4；源文件动画完全由帧数驱动，字体加载完成后再截图。

## 素材与参考

- 用户参考：[一只瓜带你玩游戏丿的 RTX 5060 系列宣传片](https://www.douyin.com/video/7493676175248657679)，经用户分享短链解析到该视频；实际读取 30.063 秒、1024 × 576、30 fps、立体声 AAC。镜头参考是芯片开场、游戏场景、绿色技术信息、产品定版和标志收尾。
- 参考视频保存在 `reference/reference.mp4`，仅供制作对照，未编入成片；不复用参考片中的 NVIDIA 标志、帧率数值或原音轨。
- 品牌素材来自本仓库 `assets/`，通过 `scripts/prepare-assets.mjs` 同步到 `public/art/`。主图、芯片图、手柄图、MU 标志均是项目现有资产。
- 配色依据 `src/MainForm.Theme.cs` 和 `website/public/assets/app.css`：`#05080D`、`#0B111A`、`#233041`、`#ADFF18`。
- 游戏穿行场景为原创 SVG 透视几何；UI 为当前 MU 视觉的动画演示，三款游戏名称是示例。成片没有游戏实测或性能对比素材。
- 音轨由 `scripts/soundtrack.mjs` 原创合成：30 秒 / 128 BPM / 64 拍 / 48 kHz 立体声，包含底鼓、军鼓、低音、琶音、氛围音和切镜音效。无旁白。
- 字体 Noto Sans SC / Rajdhani 来自 Google Fonts，OFL 许可文本随字体存放在 `public/fonts/`；渲染时本地加载。
- [Remotion 渲染文档](https://www.remotion.dev/docs/renderer/render-media)。

功能文案依据本地当前源码与已有项目说明。下载、安装、恢复仍按产品支持条件执行；成片末尾保留第三方项目属性及效果依赖条件。未将 Pro 占位设计、云同步或固定性能收益写入宣传片。
