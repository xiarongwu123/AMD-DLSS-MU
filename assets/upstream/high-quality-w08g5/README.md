# 高质量方案原安装脚本修正

使用用户于 2026-10-29 提供的 install.ps1。原包脚本有两个连续 UTF-8 BOM；新版仅有一个 BOM，去掉 BOM 后内容完全相同。

原 install.bat 通过 PowerShell 调用同目录 install.ps1，因此替换的是 install.ps1，无需把 PowerShell 内容写进 BAT。

此文件用于原始离线包的修正与安装逻辑比对。MU 的 HighQualityInstaller 直接校验和部署文件，不执行此脚本；高质量内置资源包不含 BAT/PowerShell 安装器。
