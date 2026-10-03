# DLSS-NR on AMD v0.6.0 接入

官方发布：https://github.com/danielblnc/DLSS-NR-on-AMD/releases/tag/v0.6.0

- 官方安装器：`dlssnr_on_amd_setup.exe`
- 文件大小：`59841536` 字节
- SHA-256：`20636c9587e858e2b35b29702ef36bcb0018a985592e24d6ca95b3e1b41d2e71`
- 安装器为 Windows x64 GUI PE 文件；客户端不打包或再分发上游安装器。

上述大小和摘要已与 GitHub Release 元数据及实际下载的官方安装包分别核对。

上游新增 RX 6000（RDNA2）支持，要求安装 AMD HIP 7.2 运行时；所有支持的显卡获得画质改进，修复预缩放模式下的暗部、小灯光和细小物体闪烁，并修复自动曝光适应的轻微延迟。发布说明未给出统一性能提升百分比；MU 不保证具体帧率。

客户端仅下载并校验官方 Release；继续显示上游 GUI，不使用旧版控制台输入协议，也不猜测静默参数。安装器关闭后再次检查游戏目录。macOS 构建与测试不能替代 Windows + AMD 显卡实机验收。
