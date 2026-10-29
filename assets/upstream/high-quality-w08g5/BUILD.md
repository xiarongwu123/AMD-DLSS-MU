# 高质量（画质增强）方案构建资源

大型资源随 v2.2.8 的 GitHub Release 提供，Git 仓库仅保存固定版本、大小和 SHA256 清单。

在项目根目录执行 `python3 scripts/prepare-bundled-resources.py`，会下载并校验 `assets/high-quality-w08g5.zip`，从其中校验并提取 `assets/dlssnr.bin`。已有正确文件直接复用；失败不会替换现有文件。

随后按项目原有流程构建 desktop-ui 和 Windows 客户端。此脚本仅补齐新增加的资源；既有构建所需 DLL、前端依赖仍按原构建流程准备。

资源包包含两份基础模型，MU 原生安装系统使用它们，不执行 BAT 或 PowerShell 脚本。此目录的 install.ps1 仅用于修正手动安装原包和比对上游安装逻辑。
