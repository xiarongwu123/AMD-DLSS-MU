# 游戏运行监测 V1

## 使用方式

用户完成 MU 游戏配置后，监测自动开启，无需另开参与开关。MU 运行且账户已登录时，会识别游戏库中已配置的游戏进程。游戏退出后生成一条会话摘要，先保存在本机，再上传到当前账户对应的服务器数据库。在设置页选择官方 [PresentMon 控制台程序](https://github.com/GameTechDev/PresentMon/releases)后可采集 FPS；无需 PresentMon 也可记录游戏、硬件、MU 安装模式和运行时间，但 FPS 保持未知。

未配置的游戏不会采集。设置页“清除我的监测数据”会删除本机待上传记录，并请求服务器删除此账户的所有监测会话。已配置游戏以后运行时仍会生成新数据。若服务器不可达，界面会显示删除未完成，须联网后重试。

## V1 实际字段

| 类别 | 字段 | 来源与限制 |
| --- | --- | --- |
| 游戏 | 名称、Steam App ID、EXE 文件版本、运行起止时间 | 已识别的游戏库条目及 EXE；版本可能为空 |
| 硬件 | GPU 名称、显存、驱动版本、CPU 名称、物理内存、Windows 版本 | 系统查询；多 GPU 环境可能无法确认实际渲染 GPU |
| MU 配置 | 已安装的 MU 模式、版本 | MU 自身安装记录，不代表游戏当前图形设置 |
| 性能 | 平均 FPS、1% Low、帧数、FPS 来源、平均 CPU/GPU 每帧忙碌时间 | PresentMon CSV；优先采用画面实际变化间隔，缺失时采用 Present 间隔，并标记来源；至少 120 个有效帧才给出 FPS；忙碌时间单位为毫秒，不是利用率百分比 |
| 状态 | `captured`、`insufficient_frames`、`mixed_metric` 或 PresentMon 错误状态 | 仅说明数据采集结果，不代表游戏兼容性或稳定性 |

目前不会采集游戏画面、音频、完整 EXE 路径、系统用户名或原始逐帧 CSV。原始 CSV 用后删除。服务器记录一个会话的摘要 JSON，并按账户隔离。

图形 API 只能得到 PresentMon 的 PresentRuntime（例如 DXGI），不能据此区分 DX11 与 DX12。HDR、分辨率、超分/FG/RT 设置、GPU 温度与功耗、VRAM 占用、CPU/GPU 利用率、优化前后对照、崩溃和黑屏判断均尚未接入；这些值不能从现有数据推断或填为零。

## 数据与运维

- 本机队列位于 `%LOCALAPPDATA%\AMD-NR-Assistant\telemetry-queue\<账户 ID 哈希>\`。上传失败会重试；服务端尚未提供接口时每小时重试一次，限流时遵守重试等待。服务器拒绝或损坏的记录改名为 `.rejected`，供排查，清除数据会一并删除。
- 服务端接口为 `POST /api/v1/telemetry/sessions` 和 `DELETE /api/v1/telemetry/sessions`，均需客户端账户认证。上传以会话 ID 幂等，重复 ID 对应不同内容会返回 409。
- SQLite 账户库版本从 3 升到 4，启动时迁移创建 `GameTelemetry` 表。部署前按原有服务端流程备份账户数据库。已于 2026-10-07 部署到线上，见 [部署记录](TELEMETRY-DELIVERY.md)。
- 管理员完成登录和动态码验证后，在后台导航选择“游戏监控”，或访问 `/admin/telemetry`。页面支持时间范围和游戏名称筛选，展示会话数、参与账户、有无 FPS 的会话数、游戏 FPS/1% Low 汇总、显卡分布、MU 安装模式和最近 50 条会话。
- Windows 真实游戏与 PresentMon 联调仍需在 Windows 机器执行。Mac 上只完成跨平台编译、CSV 解析及服务端 HTTP/迁移测试。

## 验证命令

```sh
dotnet run --project tests-telemetry/Telemetry.Tests.csproj -c Release
dotnet build tests-ui/Ui.Compile.csproj -v quiet
dotnet run --project server/Mu.Server.Tests/Mu.Server.Tests.csproj -c Release
dotnet run --project server/Mu.Server.HttpTests/Mu.Server.HttpTests.csproj -c Release
```
