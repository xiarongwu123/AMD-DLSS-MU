# 启动器自动更新

启动后，MU 从官网 `https://amd-dlss-mu.claude-api.cn/release.json` 检查 Windows x64 正式版；也可点击「检查更新」。官网清单的版本不高于本机时不更新。网络失败不影响游戏管理。

用户确认后，MU 只从官网 `https://amd-dlss-mu.claude-api.cn/download/file` 下载完整 `AMD-DLSS-MU.exe`。下载支持进度、取消和断点续传。下载完毕校验清单中的大小和 SHA-256、x64 PE 架构，以及 EXE 内部版本与发布标签一致；校验失败不会安装。客户端没有 GitHub 下载回退源。

官网须先同步并发布新版本文件与 `release.json`，再分发使用官网更新源的客户端。清单需提供 `tag: vX.Y.Z`、相同的 `version: X.Y.Z`、`channel: stable`、`platform: Windows x64`、`file: AMD-DLSS-MU.exe`、文件大小与 SHA-256，且下载地址为官网 `/download/file`、`delivery: server`。官网后台如何获取发布文件属于服务端流程；客户端只访问官网。

下载后，程序将当前 EXE 复制为独立更新助手，退出后在原目录原子替换文件，并保留旧版备份。新版启动后须在 45 秒内完成主窗口健康确认，否则回退旧版。健康确认不验证游戏功能。目录不可写时保持当前版本并提示，不自动提权。游戏文件、安装记录和配置不参与启动器更新。缓存位于 `%LOCALAPPDATA%/AMD-NR-Assistant/updates`。

旧版客户端若仍使用 GitHub 更新源，需要先通过旧版更新器或官网下载一次使用官网更新源的版本。Windows 上仍需端到端验证成功替换、文件占用、权限不足和启动失败回退。
