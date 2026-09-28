# 根据真实资料查询电脑配置

2026-09-28 的产品方向以公开查询为主：用户选择游戏并填写电脑配置，即可查看硬件档位估算、官方安装要求与已有的上游 Mod 适配记录。页面不要求用户先测试或上报；旧版文档中的征集测试流程不再作为官网查询入口。

## 来源与可复现性

- **游戏身份**：沿用 Steam 官方 2,001 款 Windows 游戏目录、稳定 AppID 与中英文名称；不新增虚构游戏、版本或玩家测试。
- **官方配置**：`scripts/catalog/collect-steam-requirements.mjs` 从 Steam 官方详情接口获取最低及推荐配置，保留原文、商店来源、抓取时间及响应 SHA-256。核对内层 `steam_appid`，不能仅凭返回对象的外层键关联游戏。缺失、未提供与尚未采集单独记录。容量存在逗号、空格分组或范围歧义时，不强行生成自动判定数字。
- **上游适配**：`scripts/catalog/collect-optiscaler.mjs` 使用 OptiScaler 官方 Wiki 提交 `67f573d5acd9a719cc799159c798fb978b702446`。707 行去重为 706 条，239 款可与本目录精确关联，其中 111 款具有归属于该游戏的上游测试环境。未匹配条目不冒充 Steam 游戏；共用 Luma 页面中的机器配置不传播为每款游戏的测试环境。每条均为 `muVerified: false`。
- **硬件性能**：`scripts/catalog/generate-hardware-reference.mjs` 提取 Tom's Hardware 当前及历史 CPU/GPU 游戏测试表中的型号和性能区间，保留各测试批次。当前包含 100 个桌面 GPU、84 个 CPU；显存使用对应表或 AMD、NVIDIA、Intel、XFX 官方产品页补充。型号的显存容量及 GDDR 版本不随意合并，纯理论浮点性能不替代游戏测试。
- **NVIDIA 功能**：307 款精确匹配条目展示官方 RTX 功能列表事实。按各列解释原生支持与 NVIDIA App 覆盖标记，并注明相关硬件条件；不把 DLSS、帧生成或光追标记转换为 AMD/MU 的支持结论。

原始响应、记录清单及内容哈希保存在工作区旁 `.tools/` 的来源归档目录；发布数据位于 `server/Mu.Server/Data/Catalog/` 和 `website/public/assets/hardware-reference.json`。重放前验证原始文件哈希，修改解析器后必须对完整原始快照重新生成，不混用新旧解析结果。

## 判断边界

浏览器使用 `hardware-check.js` 对比 CPU、GPU 综合游戏性能及内存，配置随分享链接保留，不作为游戏列表的 GPU 实测筛选条件。官方系统、DirectX、安装空间与其他限定条件另行展示。

同一测试批次仅在全部观测区间均有至少 10% 差距时判定更高或更低；相同型号单独识别。跨批次不直接比较分数，只在一个共享型号两侧的独立实测差距均超过 25%、方向一致时作明确标注的保守档位推断。缺少可比数据、接近门槛、型号或容量版本有歧义时保留未知。

已知硬件显存不能被手填容量覆盖。最低配置明确不达标优先于推荐配置判断。页面的“预计达到配置”不等同于具体画质/帧率、光追能力、MU 或 DLSS5 已通过实测；OptiScaler 版本、前置 Mod 与异常条件独立展示，并链接到固定提交的来源。

核心数、线程数、指令集、强制光追及 Shader 等额外条件尚未核对时，不输出整机通过。参考显卡自带的容量也不自动变成游戏明确声明的最低显存。旧型号没有对应基准或发行商只写泛化要求时，页面提供部分硬件对比与官方原文，不能声称所有目录游戏都已有自动整机结论。

无玩家报告时不展示空的成功率、成功次数或“请先测试”入口；已有真实报告继续保留，数据库不生成示例报告。

## 接口与发布

公共列表新增可选 `evidence` 及全目录 `coverage`；详情新增 `requirements` 与 `modCompatibility`。保留已有客户端字段。代理校验来源域名、Steam 身份、固定 Wiki 提交和 `muVerified: false`，不透传任意网址。SQLite schema 保持 3。

发布前冻结完整数据，串行运行服务/HTTP/管理端测试，运行 Node 数据解析、代理及硬件判断测试，重新构建嵌入资源。按 `server/METADATA-RELEASE.md` 在生产数据库备份副本中演练：全部表结构和行内容一致、完整性与外键通过，并启动候选服务实际查询元数据。

官网差异包只替换清单文件，校验旧文件及依赖哈希，保留下载包、视频、用户数据和 Magpie 镜像接线。候选部署与正式激活分开记录；实际发布的覆盖数量、哈希、备份和公网验收结果在完成后补充。

## 2026-09-28 冻结数据

快照时间为 `2026-09-27T17:24:38.248Z`（北京时间 9 月 28 日 01:24）。2,001 款全部采集完成：2,000 款有官方配置，1,997 款有最低配置，1,795 款有推荐配置；获取失败和待处理均为 0。Battlefield REDSEC（AppID 3028330）的官方响应未提供配置，保留 `not_provided`。

全部原始响应经 SHA-256 与内层 AppID 校验，使用最终解析器离线重放；第二份独立输出逐字节一致。原始响应归档位于 `/Volumes/SamsungPSS/mu/.tools/steam-requirements-source/2026-09-27T16-31-09-256Z`。

| 数据 | SHA-256 |
| --- | --- |
| `steam-requirements.json` | `f12c7fdd1529f4717d901a26b9bac22aeaff933c34dd59aa139a280eaa671ff9` |
| `steam-requirements.provenance.json` | `bd45791922f45bdb74cc1e2a382d4fd3d89f35be021b7b1b6bdc246feb640b8d` |
| `optiscaler-compatibility.json` | `1d3d2cecb19ea7eabcb24dccb177c36c95f9f900e66c2a5216c35ca8dbe9c023` |
| `technology-references.json` | `75ac584bd77dccbe94b244e5e1ff4d6dc6197fa52b5d5fae1dddddeb2e8441e0` |
| `hardware-reference.json` | `106a02dc8f1727d194424bcfcdf62bbee6c916643f7e7679c4878646977c92ee` |
| `hardware-check.js` | `b8c3193aa8a47fd1d66c0a1aae306228f845b62fb72ce60a480044da868c8ca3` |

最终 Node 回归 75/75 通过，覆盖采集重放、解析、数据映射、代理、硬件判断与前端交互。发布校验期间发现独立的 v2.0.1 官网更新，已合并并保留其公告、公共脚本与下载信息；本次不替换 EXE。当前下载为 201,095,999 字节，SHA-256 `f30315f9bdef2701c4a6d91c96e0dc43a6d8ad26152a01441e24ac0e9e26fd95`。

## 2026-09-28 生产发布与验收

服务端 Release 测试共 581 项通过（Service 156、HTTP 333、Admin 92），四份嵌入资源哈希与冻结源一致。Linux 发布包 SHA-256 为 `bca70900b759eb96c1c84d3ba218b393f135056797b4d8af10d046fa7c3290c3`，运行中的 `Mu.Server.dll` 为 `761723fb4c70b8b43d22805a1a9f1d681e14da333169ec15bcafff781d4003c0`。

候选版本在生产备份副本中完成启动、公开元数据及维护模式检查，18 张表的结构与全部行内容保持一致，SQLite schema 仍为 3。演练备份为 `backups/accounts-20260927T173225Z.sqlite`；正式激活前再次备份为 `backups/accounts-20260927T173307Z.sqlite`。账号服务已激活 `releases/20260928-hardware-data`，旧版本与备份保留。

官网差异包 SHA-256 为 `e47828bc74ac03b3343d1fdab7a4bbe4e79e373c893d2eccddd51a15859984be`。已激活镜像 `sha256:279f0f06bda7a209090519b02f4e63690ad46d0c7ee9af67a9799cda9fdc8cb5`，源码及旧镜像备份位于 `/home/xrw/amd-dlss-mu-site/backups/website-compatibility-20260927T173329Z-845628`。

公网验收于 `2026-09-27T17:35:34.337Z` 完成，`website/deploy/verify-hardware-release.mjs` 的 27 组检查全部通过：目录分页、中英文/AppID 查询、固定游戏身份与原文哈希、未提供配置路径、五份前端资源哈希、公开查询与受保护接口、v2.0.1 公告与下载信息、EXE Range、Magpie 镜像。报告保存在 `/Volumes/SamsungPSS/mu/.tools/hardware-live-verify-20260928.json`。浏览器实际显示 2,001 / 2,000 / 239 的覆盖数及黑神话官方要求、上游版本与环境，未出现旧版要求用户自行测试的入口。

另外在服务器对实际 EXE 与 Magpie ZIP 重新计算完整 SHA-256，分别为 `f30315f9bdef2701c4a6d91c96e0dc43a6d8ad26152a01441e24ac0e9e26fd95` 和 `efb41e5177a628c0742566a887660a5a50e159f824cbfbf9f0620b2cc3b6803c`，均与更新前一致。代码未提交或推送 Git。

公网浏览器另外完成交互验收：搜索“赛博朋克”，输入示例 `7900XT / 7800X3D / 32 GB`，选择结果后显示“核心硬件预计达到推荐配置”，同时展示各硬件比较依据和官方要求；浏览器错误/警告日志为空。验收后已清除示例参数，恢复用户原先打开的黑神话页面。本次仅发布官网与查询服务，没有发布新的 Windows 客户端。
