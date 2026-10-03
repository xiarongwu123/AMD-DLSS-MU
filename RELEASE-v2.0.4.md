# AMD DLSS MU v2.0.4

本版将 DLSS-NR on AMD 上游安装器更新至 **v0.6.0**，保留 v2.0.3 的游戏库、兼容性反馈、账户和大力喜鹊功能。

- 新增 RX 6000（RDNA2）系列支持；使用前须安装 AMD HIP 7.2 运行时。MU 不再把该系列误判为不受支持。
- 上游改善了画面质量，修复预缩放模式下暗部、细小灯光与物体闪烁，以及自动曝光适应的轻微延迟。
- 安装器继续从官方 Release 按需下载，并校验固定的文件大小与 SHA-256；未把上游安装器或 Magpie 完整包塞入 MU EXE。

上游未公布本次统一的帧率提升百分比；具体效果取决于显卡、驱动、游戏和设置。RX 6000 用户请在上游安装窗口确认 HIP 7.2 依赖已满足，不要跳过缺失警告。切换安装方案前先退出游戏并恢复原配置；带反作弊的联网游戏请遵守游戏规则。

发布包是 Windows x64 单文件 EXE。本次在 macOS 完成构建与自动化测试，**尚未在 Windows + AMD 显卡上完成实机验收**。

`AMD-DLSS-MU.exe` SHA-256：`9a06f67be74ec8377554c57529c2bbdcbfc3e28f7e78729f7b43ff997eacc854`

上游发布页：https://github.com/danielblnc/DLSS-NR-on-AMD/releases/tag/v0.6.0
