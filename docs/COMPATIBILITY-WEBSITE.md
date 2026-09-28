# 官网游戏兼容性查询

2026-09-28 最新方向与实现见 [电脑配置查询](COMPATIBILITY-HARDWARE.md)：官网根据现有官方要求和专业资料供用户直接查询，不要求先完成游戏测试。下方保留原方案及其历史发布记录。

## 范围

2026-09-27 后续更新已将目录扩充到 2,001 款游戏，增加分页、官方中英文名称和可追溯来源。当前数据和部署记录见 [真实游戏目录扩充](COMPATIBILITY-CATALOG.md)；下方首版发布记录保留当时 4 款游戏的历史状态。

官网 `/compatibility` 公开展示游戏目录、GPU 筛选、三种实测结果的计数和最近 50 条环境记录。分享地址保留游戏、搜索词和显卡筛选。上报在客户端完成，浏览器不伪装自动读取完整 GPU/驱动。

目录收录不代表支持。没有报告显示“待验证”，搜索无匹配、接口错误、尚未测试是不同状态。记录时间表示服务端收到提交的时间，不宣称游戏运行经过自动认证。

官网当前 v2.0 下载包未包含本次新增客户端上报入口；页面明确告知新版客户端尚待发布。本次官网更新不替换 EXE、不修改下载版本。正式客户端发布后应同步更新页面的上报引导文案。

## 源码与接口

`website/` 从 2026-09-27 当时正在运行的 `/home/xrw/amd-dlss-mu-site` 导入，包含已发布的 v2.0 公告、下载、使用说明、反馈和调研功能。没有导入生产 `.env`、数据库、视频或 EXE；部署时保留这些运行资源。

浏览器仅请求官网同源 `/api/compatibility/`，官网服务器再请求固定上游 `https://mu-api.claude-api.cn/api/v1/compatibility/public/`。CSP 的 `connect-src 'self'` 保持有效，不传递访客 Cookie、账户令牌或任意目标 URL。

| 官网接口 | 账号服务公共接口 |
| --- | --- |
| `GET /api/compatibility/games?q=&gpu=` | `/api/v1/compatibility/public/games?q=&gpu=` |
| `GET /api/compatibility/games/{id}?gpu=` | `/api/v1/compatibility/public/games/{id}?gpu=` |
| `GET /api/compatibility/gpus` | `/api/v1/compatibility/public/gpus` |

公共开关为 `compatibility.public.read`，后台可选择公开或关闭；它不改变客户端的 `compatibility.read` / `compatibility.submit` 权限。公共 API 不提供匿名写入。显卡列表只包含真实报告中的型号。

代理的配置、缓存、超时及测试详见 `website/` 内的代理说明。生产 Compose 仅绑定 `127.0.0.1:8088`，经既有 Cloudflare Tunnel 提供访问，因此开启 `COMPATIBILITY_TRUST_CF_IP=1` 使用 Cloudflare 覆写的访客 IP 进行限流。直接暴露服务端口的部署必须关闭此选项，或先建立可信代理边界。

## 验证边界

Node 代理测试使用隔离的上游服务；浏览器中的有记录/异常环境检查使用本地测试数据，不向生产数据库注入示例实测。线上空库应保持零报告。Windows 客户端运行和游戏实测仍是独立验收项目。

## 2026-09-27 部署记录

账号服务已切换到 `releases/20260927-compatibility`，生产数据库由 schema 2 升为 3。先在生产备份的副本上演练，再停止账号服务生成最终快照，在维护模式下迁移并核对数据，最后恢复正常服务。

- 最终升级前备份：`/home/xrw/amd-dlss-mu-account/backups/accounts-before-compatibility-20260927T093627Z.sqlite`。
- 原有业务及 Identity 表完整行一致，原功能权限配置保留；SQLite 完整性检查通过，外键违规为 0。
- 正式公开 API 返回 4 款游戏、0 条实测，均为 `untested`；匿名账号查询和匿名实测写入仍为 401。
- 副本演练及激活脚本位于 `website/deploy/`。恢复账号写入后不允许自动还原旧数据库，以免丢失新数据。

浏览器检查覆盖 1440px 和 320px、首页搜索跳转、GTA V 两个独立版本、显卡筛选、记录展开及查询链接复制。本地响应替身另外验证三种结果、环境字段、用户文本的 HTML 转义，以及“无匹配”和上游 503 的独立状态；这些测试没有写入生产数据库。

`node --test website/tests/*.test.mjs`：16/16 通过，包含 14 组兼容代理测试及 2 组既有统计测试。原始官网归档的统计测试仍断言旧 GitHub 跳转行为，本次仅更新其隔离下载 fixture 和断言以验证当前直下载、HEAD 和 Range 统计语义，下载实现保持不变。

官网已于 2026-09-27 17:47 左右（Asia/Shanghai）完成发布：

- 页面：`https://amd-dlss-mu.claude-api.cn/compatibility`；首页及下载、说明、反馈、调研页均包含兼容查询入口。
- 仅更新 12 个服务/页面文件；差异包 SHA-256：`2626b6bf3aab78282c824631bc761fba999a1ecc0e5ea42eed7218d372743924`。
- 旧源文件及镜像备份：`/home/xrw/amd-dlss-mu-site/backups/website-compatibility-20260927T094654Z-440490`。
- 激活脚本：`/home/xrw/amd-dlss-mu-site-staging/website-compatibility-patch-20260927T094607Z/activate-website-compatibility.sh`。需要网站回滚时传入站点根目录、`--rollback` 和上面的备份路径；脚本会拒绝覆盖此后发生的源码或镜像变化。
- 公网目录、GTA V 搜索、单游戏详情和 GPU 列表均返回预期结果；官网匿名写接口返回 405，原后台接口保持 401，调研定义接口保持 200。
- 公网 Chromium 实际加载 4 款待验证游戏，点击 Cyberpunk 2077 显示全部计数为 0；1440px 和 320px 无横向溢出。截图保存在本地忽略目录 `output/playwright/compatibility-live-*.png`。
- 下载保持正式版 v2.0，203287330 字节。服务器实际文件 SHA-256 为 `4a666476eed79bfa09c7ce973ccaf833e8078d6b173e2f18787878b57fda2a78`；公网 HEAD 为 200、Range 为 206、文件头为 `MZ`，未替换客户端包。

公网页面原有 Cloudflare 注入的统计 beacon 仍受 `script-src 'self'` 阻止；本次没有放宽 CSP。兼容页面自身脚本和同源数据请求正常。
