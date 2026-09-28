# 游戏兼容数据库 V1

2026-09-28 官网已按最新产品方向改为 [基于真实来源的电脑配置查询](COMPATIBILITY-HARDWARE.md)。本文记录先前的客户端报告接口与实现，不能据此恢复官网“先测试再查询”的流程；客户端代码尚不代表新版 EXE 已发布。

V1 在现有 WinForms 客户端首页提供游戏搜索、按 GPU 查看社区实测、自动采集本机环境、提交测试结果。官网另外提供公开只读查询，上报引导至支持该功能的客户端。两端共用现有账号服务和 SQLite。

## 使用流程

1. 登录后进入“游戏兼容性”，搜索名称或 Steam App ID。显卡筛选默认展示全部显卡，可以切换到本机任意一张显卡。
2. 选择游戏，查看成功、部分成功、失败次数、各 GPU 分布和最近 50 条环境快照。无记录显示“待验证”；混合反馈显示“结果不一致”。目录收录和本地已安装均不代表兼容。
3. 完成真实游戏体验后点击“提交测试结果”，选择已经扫描到的本地游戏，或定位实际游戏 EXE。目录未收录的游戏可以通过这次提交建立条目。GTA V Legacy 和 Enhanced 使用独立条目。
4. 自动读取显卡、Windows 驱动版本、显存、操作系统、系统 DirectX、EXE/Steam build、邻近 DLSS DLL 和工具版本。多 GPU 设备需要确认实际测试用卡。Windows 驱动版本不是 AMD Adrenalin 软件包版本。
5. 选择成功、部分成功或失败；有问题时选择原因。游戏 API、设置、其他 Mod 可按实际测试补充；无法识别的字段保持未知。点击提交后公开环境快照、匿名玩家编号、结果和服务端提交时间。

自动采集发生在报告表单读取游戏时，不监视游戏运行过程。DLL 文件版本只能证明本机存在该文件，系统 DirectX 版本也不代表游戏正在使用的 API。V1 保存的是玩家自述结果，没有将其标成开发者或自动化认证。

## 数据与接口

共享契约为 `src/CompatibilityModels.cs`。游戏目录、GPU 查询键和每次不可变环境快照保存在账号数据库中；游戏版本属于测试记录。GPU 架构无法可靠检测时为空或 `unknown`。

以下客户端接口需要 Bearer 登录；服务端分别检查 `compatibility.read` / `compatibility.submit` 功能权限，默认普通用户可用，可在现有后台功能管理中调整。官网使用独立公共只读接口和 `compatibility.public.read` 开关，见 [官网说明](COMPATIBILITY-WEBSITE.md)。

| 接口 | 行为 |
| --- | --- |
| `GET /api/v1/compatibility/games?q=...&gpu=...&page=1&pageSize=50` | 分页搜索目录，每页最多 50 款；返回匹配总数/全目录数量及 GPU 精确筛选后的实测计数 |
| `GET /api/v1/compatibility/games/{id}?gpu=...` | 游戏详情、全部 GPU 分布、筛选后的最近 50 条记录 |
| `POST /api/v1/compatibility/tests` | 创建游戏条目或关联已选条目，提交环境与结果 |

报告仅接受 `success`、`partial`、`failure`。`untested` 是零报告时的派生状态，不是可以提交的结果。服务端生成匿名玩家编号及提交时间，不返回邮箱或账号 ID。

官方游戏目录现有 2,001 款，包含来源网址、核对时间和部分官方列表收录引用。目录来源与 MU 实测分别展示，见 [目录数据说明](COMPATIBILITY-CATALOG.md)。

每个账号的 `SubmissionId` 幂等；相同 ID 和内容的重试返回原记录，改变内容返回 409。客户端遇到网络错误保留原 ID 和负载，可重试同一记录。同账号、同游戏、相同环境每 UTC 日只计一条；另有每小时 20 条、24 小时 100 条限制。以上用于降低重复计数，不能证明自述结果真实性。

只在点击提交时上传结构化字段，不上传完整 dxdiag、机器名或游戏绝对路径。不会写入游戏配置或启动游戏。玩家填写的设置和说明会公开，表单会提醒勿填写个人信息。

## 部署与验证

客户端和账号服务需要配套更新。新客户端连接未升级服务端时显示明确的服务未提供提示，不会填充示例测试记录。按 `server/Mu.Server/DATABASE.md` 先备份，再进行版本化迁移；服务端启动支持现有 schema v1/v2 升至 v3，保留账号和原有权限配置。

自动化验证入口：

```sh
dotnet run --project tests-compatibility/Compatibility.Tests.csproj
dotnet run --project tests-account/Account.Tests.csproj
dotnet build tests-ui/Ui.Compile.csproj -c Release
dotnet run --project server/Mu.Server.Tests/Mu.Server.Tests.csproj
dotnet run --project server/Mu.Server.HttpTests/Mu.Server.HttpTests.csproj
```

服务端测试应串行执行，避免同时写入共享编译输出。macOS 上可运行协议、采集解析和服务端测试，也可交叉编译 WinForms；不能替代 Windows 真机验收。

Windows 验收需覆盖：单 GPU/混合 GPU 的 dxdiag 识别、真实游戏 EXE/Steam build、100%/150%/200% DPI、键盘输入与滚动、未登录/功能关闭、成功与失败上报、断网重试同一条记录，以及提交后查询计数变化。生产部署、Windows EXE/游戏运行和真实社区记录积累是独立验收状态。

V1 未包含截图上传、排行榜、兼容率推荐、测试任务、自动安装/启动测试、网页上报或社区运营页面。先积累 100–500 条真实记录，再据实际查询与反馈扩展。
