# 玩家方案 Windows 客户端测试包

日期：2026-10-13（Asia/Shanghai）。版本：`v2.2.0-react-preview.5`。

客户端源码提交：`c4536cd5aa505b857be2f1b3197c33b5b780df33`，包含主干 `d1d4921` 的完整玩家方案流程和本次测试版元数据。Windows 文件版本为 `2.2.0.5`，程序集版本保持 `2.2.0.0`。

## 产物

- ZIP：`artifacts/AMD-DLSS-MU-v2.2.0-react-preview.5-win-x64.zip`，200,368,927 字节。
- ZIP SHA-256：`7d13703b27b16916a0ad359e08b595ed437ab4a690aa42c107f887be80da3b1c`。
- EXE：`artifacts/v2.2.0-react-preview.5-win-x64/AMD-DLSS-MU.exe`，205,960,268 字节。
- EXE SHA-256：`413eb2797f8ff755585f2f157629b56b2289fa0f368f48c9538d4881960fcf19`。

ZIP 仅包含 EXE、`TESTING.md` 和 `SHA256SUMS`；不包含调试符号、用户凭据、数据库或部署配置。包文件被 Git 忽略，未替换官网正式版。

## 验证

- React 生产构建通过，13 项前端测试通过。
- 账户回归通过：72 项。
- 配置、恢复、下载和 Magpie 回归通过：200 项，包括社区参数过滤与应用检查。
- 更新器及最终 Windows PE/版本检查通过：22 项。
- 单文件 EXE 为 Windows x64 PE，包含 453 个 bundle 条目。
- 内置 DLSS DLL SHA-256 与锁定值 `8270b350cd82de5ce89806872cdd6b6a9249b80836b91bbeb3573470744cc206` 一致。
- 资源检查确认包含最新 `ReactUI/index.html`、`index-BamWej_z.js`、`index-D2SoJuCU.css`、品牌图片、二维码及原生插件。
- Bundle 包含 WebView2 Core/WinForms、x64 WebView2Loader、WinForms 和 CoreLib；运行时配置确认自包含 .NET 与 WindowsDesktop。未包含未使用的 WebView2 WPF 程序集。
- ZIP 完整性检查通过。

初次配置回归因 macOS 默认临时目录经 `/var` 符号链接触发防链接检查而失败；使用仓库内真实临时目录后全量通过，客户端防链接逻辑未修改。构建恢复了当前新增的 WebView2 依赖，避免复用旧依赖缓存。

玩家方案真实公网 API 已通过 23 项端到端检查，部署与备份记录见 [服务端交付记录](COMMUNITY-PRESETS-DELIVERY.md)。本包在 macOS 交叉构建，Windows WebView2 启动、真实游戏 INI 上传、一键应用和恢复由用户在 Windows 实机验收。具体步骤见 [测试说明](../RELEASE-v2.2.0-react-preview.5.md)。
