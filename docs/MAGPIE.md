# 大力喜鹊

入口：左侧「大力喜鹊」→「开启大力喜鹊」。首次联网下载约 467 MiB，后续复用本机组件。

1. MU 下载 SAOG0721/Magpie 的 `v0.6.8-experimental.1` 完整主包，核对固定大小和 SHA-256 后解压。
2. 创建独立便携配置，默认中文、Lanczos 等比适应屏幕缩放；不修改游戏或已有 Magpie 的配置，不设置开机启动，不运行 NGX OTA 脚本。
3. 程序打开后，将游戏切换为窗口模式并聚焦游戏，按 **Alt + Shift + A** 开启或停止效果。首次快捷键冲突可在 Magpie 内修改。
4. AI 效果、补帧与画面参数在 Magpie 内选择。默认配置不是 DLSS，也不是帧生成；具体支持由显卡、驱动和效果组件决定。不要叠加多套补帧。

若已有 Magpie 在后台，请先从托盘退出后再使用 MU 的独立版本。MU 不强制结束已有实例。

安装位置：`%LOCALAPPDATA%\AMD-NR-Assistant\tools\Magpie\v0.6.8-experimental.1`。
配置位置：该目录中 `Magpie.exe` 旁的 `config\v4e\config.json`。重复启动不覆盖用户修改。

主包在运行时从上游下载，不内置到 MU EXE。保留完整上游包中的许可证与说明。Magpie 是独立进程，不代表游戏内 DLSS 已启用。

参考：

- https://github.com/SAOG0721/Magpie
- https://github.com/SAOG0721/Magpie/releases/tag/v0.6.8-experimental.1
- 配置及快捷键：该 tag 的 `src/Magpie/AppSettings.cpp`、`ConfigLocations.h`。

验证边界：跨平台代码测试与 Windows 发布包检查不替代 Windows 实机启动、快捷键和捕获效果测试。
