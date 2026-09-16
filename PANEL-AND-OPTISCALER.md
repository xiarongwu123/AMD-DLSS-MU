# v1.3.0 测试版：面板、诊断与 OptiScaler

## F8 浮动面板

选中实际游戏 EXE，点击“F8 控制面板”。保持启动器运行，窗口化/无边框游戏使用 F8 显示隐藏；Escape 隐藏。F8 被其他程序占用时会显示提示。独占全屏可能遮挡桌面窗口。显示运行观察、原生菜单快捷键、配置编辑和诊断导出。配置编辑仅在游戏退出后保存，备份原文件；保存不等于实时生效。

## 实时滑块插件（实验性）

面板中的“安装实时滑块插件”安装本项目的原生 ReShade add-on。需要用户已有 **ReShade 6.8.0 Add-on 版**和指定 **RenoDX v4.7**：

`renodx-dlss5.addon64` SHA-256：`d5adf82eb44b065f4c590ac91fe824bab07afea0eb9f994bde936710c8593952`

游戏内 F8 打开、Escape 关闭。提供结构、全局色调、皮肤、整体/局部强度、运动缩放及样式等 15 项回调映射，未提供的控件禁用。参数从原运行时读取，修改交由原回调执行，随后读回；不把加载或连接当成推理成功。原插件会按自身行为保存参数。面板可拖动和调整大小。

此适配器不是公开 RenoDX API：复用 DLSS5-Swapper 的 MIT 许可、版本锁定适配器，在调用期间临时替换其私有 ImGui 表指针并恢复。它检查二进制哈希和内存代码特征，拒绝不匹配版本。**不能用于 AMD 模式一，不能承诺在 RX 7900 XT 上启用 RenoDX 神经渲染。** AMD 模式一继续用 End；OptiScaler 原生实时菜单为 Insert。安装不会额外下载 ReShade/RenoDX，也不会绕过反作弊。

原生插件纳入安装记录，可通过“恢复配置”移除。安装后 F8 留给原生插件，桌面窗口改为按钮打开。原生控件是 ImGui 实现，与 DLSS5-Swapper 的 Chromium 面板非像素级相同。

## OptiScaler 独立安装

模式二：**官方标准版 OptiScaler 0.9.4**（原模式三）。旧 AMD Pre-SR 本地包模式已从安装入口移除，旧安装记录仍可恢复。按游戏实际图形接口选择 DX11/DX12 或 Vulkan。自动下载固定版本、核对大小与 SHA-256，限定组件清单解压、校验 x64 DLL，建立恢复记录后安装。下载可取消，临时网络错误重试；已有校验缓存可复用。安装失败尝试恢复。标准版 OptiScaler 不等于 DLSS 5 神经渲染。

仅下载 https://github.com/optiscaler/OptiScaler 的已审查发行附件。保留包内许可证，不执行包内脚本。安装启用 Info 级文件日志和 Insert 菜单，限制目标进程名。**标准 OptiScaler 是超分/帧生成适配，不是 DLSS 5 神经渲染安装。** 需要游戏提供兼容输入，具体功能依赖硬件和游戏；部分游戏仍需专用设置/组件，本版不自动安装 OptiPatcher、修改注册表或添加未知插件。

## 增强诊断

记录安装阶段、网络异常细节、外部安装器退出码、产物检查结果；即使安装尚未建立记录也能保留错误。导出增加运行观察、Windows 信息、配置以及 dlssnr_on_amd / OptiScaler / ReShade / dlss5-neural / dlss5-feed 日志末尾片段。每份日志限制 64 KiB，并明确标出 missing/unreadable/truncated。导出前预览，默认不上传。脱敏处理常见路径、邮箱、凭据行，但不能保证过滤全部个人信息，分享前仍需检查。

## 来源与许可

- 原生适配器来自 rakanki911/DLSS5-Swapper，提交 `24bd2aca7a7451ce94e564366381e33cac9dcdba`；保留 `native/LICENSE-Swapper`（MIT）。
- ReShade SDK：`18deaa52de0c425a78b329e9cb3c497281cd00ec`，许可在 `native/vendor/LICENSE-ReShade.md`。
- Dear ImGui：`3912b3d9a9c1b3f17431aebafd86d2f40ee6e59c`，许可在 `native/vendor/LICENSE-ImGui.txt`。
- SharpCompress 0.48.1：MIT，用于 7z 解压。
- OptiScaler：GPL-3.0 项目；运行时从官方发行页下载，源码 https://github.com/optiscaler/OptiScaler/tree/v0.9.4 。

## 构建和验证

原生插件使用 Zig 0.14.1：Windows 可运行 `native/build.ps1 -Zig <zig.exe 路径>`。C# 构建前需生成 assets/AMD-DLSS-MU.addon64。

自动测试覆盖恢复、目录隔离、配置保留与并发修改、日志脱敏与大小限制，以及实际固定版本 OptiScaler 包校验和配置生成。原生面板已交叉编译为 Windows x64 DLL；尚未在 Windows 游戏中完成 F8、输入阻挡、实时回读和恢复的端到端验证。请先使用离线测试游戏验证，不作为已完成游戏兼容性实测的正式发布。
