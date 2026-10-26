# MU Smart Recommendation Engine V1

## 本次实现范围

引擎保留七张方案卡片、会员价格和权限，只扩展原“游戏专属推荐”。推荐计算不安装游戏组件；EnvironmentDetector 仅在 MU 备份卷创建自动删除的写入探针，不在游戏目录写入。安装仍调用原有 InstallAsync / NeuralRenderInstaller / CombinedRenderInstaller / X86Bridge / LegacyBridge / OptiInstaller、下载校验、备份和恢复。

主要代码：

- `src/Recommendation/`：带来源检测模型、版本化规则、候选硬检查、参数解析、100分评分、Wilson下界、定向回退与运行验证。
- `src/MainForm.Recommendation.cs`：Windows设备检测、权限、已签发计划、安装前复检、写入前复检、现有安装器适配、用户反馈。
- `desktop-ui/src/SmartRecommendationPanel.tsx`：目标、范围、真实API确认、推荐原因、配置明细、应用、运行验证、明确同意后上传。
- `server/Mu.Server/Services/NeuralRuleStore.cs`：复用现有文件、后台、历史审计、版本更新和回滚，没有新增平行数据库。
- `server/Mu.Server/Services/RecommendationEvidence.cs`：复用现有GameTelemetry表和认证上传API，按账户、匿名设备、日期去重，隔离游戏构建/驱动/后端/组件/预设版本。

## 检测可信度

`CONFIRMED`、`INFERRED`、`UNKNOWN`随字段返回，不将静态EXE导入、系统DirectX、文件存在、进程启动当成实际图形API或NR生效。PE位数、主EXE SHA256、dxdiag设备与Windows驱动版本、游戏版本/Steam构建、分辨率、组件哈希、备份状态可读取。架构由型号推断；多GPU时所选GPU及Device ID也标记推断。当前没有DXGI预算接口，VRAM预算保持UNKNOWN。

游戏实际API、超分输入、窗口模式可以由用户按游戏设置明确确认，来源注明“用户确认，非自动检测”。缺失关键条件时展示REQUIRE_ACTION。不会根据GPU系列单独决定后端；旧多后端自动模式也取消了“RX9000直接Mochizuki”的默认。

## 规则存储与更新

继续使用现有 `/admin/neural-rules` 及 `neural-render-rules.json`。旧 `rules` 字段保留原用途；新引擎读取可选 `smartRules` 和 `weights`。旧格式缺少构建/驱动/来源证据，不能直接升级为高可信自动推荐。

认证接口：`GET /api/v1/render-rules?format=2&gameId=steam:12345`，仍执行 `schemes.game.view` 高级会员权限。旧接口不传format，返回原四字段，避免旧客户端严格JSON解析失败。

规则包含：ID与版本、Game ID、游戏版本/构建哈希、GPU型号/PCI Device ID/架构、Windows驱动范围与黑名单、API、位数、方案与固定组件版本、代理、依赖、参数、验证等级/来源/时间、已知冲突/错误码、禁用/拒绝和修复目标。

- Schema拒绝未知字段、任意脚本、下载URL、路径及任意INI键；组件版本与已有安装器能力绑定。
- 参数或匹配条件变化必须更新Rule Version；禁用/拒绝和到期调整可以立即发布。发布修改文档revision。
- 后台提供已审计回滚。回滚生成新文档revision，保留历史验证时间，不伪造新实测。
- HTTPS认证服务来源检查；拒绝跨源/非HTTPS重定向。Windows规则缓存使用已有DPAPI保护，按账户/服务/Game ID隔离，并重新校验Schema与有效期。
- 有效缓存可用于断网计算；应用安装仍遵守既有会员联网门禁，且需要重新取得可验证的远程规则。401/403不会回退为缓存授权。
- 规则异常时显示缓存来源或不可用，不生成假兼容结果。已签发计划在环境、规则revision或配置变化后失效；下载后写入前再次复检。

### 示例结构（仅说明格式，不是实测记录，不能照抄启用）

```json
{
  "schemaVersion": 1,
  "revision": "维护者填写新版本",
  "expiresUtc": "填写未来30天内的UTC时间",
  "rules": [],
  "weights": { "environment": 40, "verification": 35, "freshness": 10, "goal": 10, "readiness": 5 },
  "smartRules": []
}
```

录入smartRules时：

- `id`、`version`、`gameId`为限定字符标识；不使用电脑路径。
- `buildHashes`填写实测主EXE SHA256；或`gameVersions`填写检测结果中的完整版本。
- `gpuModels`或`deviceIds`填写实测设备；`architectures`只筛候选，不独立取得自动推荐资格。
- `driverMin`/`driverMax`使用Windows数字驱动版本，不把Adrenalin营销版本混填。
- `componentVersion`只接受当前安装器固定版本：base 0.8.0；fps 1.10.3.1+10.6；author/compat/online 1.12.1；amd-magpie 0.6.8.5-amd-rdna3；legacy32 0.7.12/runtime0.6.0；legacy64 6.8.0/addon0.7.12/runtime0.6.0；standard 0.9.4。
- `parameters`仅允许passes、modelScale、temporal、historyStrength、applyModel、dynamicResolution、nrInline、earlyAnyModuleWrap、allowEnhancedBarriers。取值及后端/版本校验后转为实际INI键。
- `minimumVramMb`和`minimumBudgetMb`可声明有证据的显存要求。预算接口尚未接入时不能满足声明预算的规则。
- `upscalerInputs`允许dlss/xess/fsr；高帧率验证需明确`xeFgVerified=true`并满足实际输入/API条件。
- `validation`为verified/community/experimental/unverified；`source`填写真实HTTPS验证来源，`verifiedAt`填写实际验证时间。
- `protectedOnline`标记已知受保护环境。没有明确有效的`onlineModificationAllowed`验证规则时停止注入，不提供绕过。
- `disabled=true`停用；`deny=true`明确拒绝；`fixes`列出经验证可修复的错误代码。回退只使用有针对性验证的候选。
- `community`不能手填，由服务端真实监控记录计算。

## 评分与参数

100分为环境40、验证35、时间/版本10、目标10、就绪5，权重可由同一规则文档调整，总计必须为100。BLOCKED/REQUIRE_ACTION不评分；无完整验证最高69，不自动配置。会员不参与评分，先选真实方案，再展示权限和已验证免费替代。

社区高可信要求至少20个匿名设备、20个独立账户、30次有效测试，功能执行Wilson95%下界>=80%，无未解决严重问题。匿名标识是本地Windows设备标识的域隔离SHA256摘要，不上传原标识。该标识不构成硬件远程证明，不能保证抵御恶意客户端伪造。

Mochizuki默认1 Pass；有验证记录才采用2–3；4–8仅专属实验规则且需用户确认；9–30不生成推荐。手动高级实验设置保留，不能绕过硬性限制。XeFG仍固定2X，不自动提高倍率。INI键来自当前1.12.1源码`Config.cpp`/`ConfigKeys.h`和模板，使用现有section/key级补丁；不新增ModelMode等字段。

## 七个方案接入程度

| 方案 | 推荐执行适配 |
| --- | --- |
| 基础稳定版 | 官方NeuralHub0.8入口，明确外部管理，不报告MU已安装 |
| 高帧率版 | 原组合安装器，固定XeFG2X，检查输入/API及版本证据 |
| 作者调校版 | 原1.12.1 Mochizuki安装器，模型校验和显式参数 |
| 兼容版 | 同一多后端安装器，显式Daniel/lmxxf/Mochizuki选择 |
| 网游适配版 | Daniel0.6配套路径，受保护环境没有允许规则则阻止注入 |
| AMD大力喜鹊 | 原独立工具，固定0.5.0 Runtime和模型，不修改游戏目录 |
| 老游戏专用版 | 按PE位数/API映射原x86桥接或ReShade64 |

标准OptiScaler仍可作为不声称NR的基础候选。原有Daniel0.6、N卡Magpie、玩家分享及恢复能力保留。所有实际游戏文件安装路径增加硬检查；对未知API/资源提供行动提示，不强行猜测。

## 分阶段验证、失败与反馈

INSTALLED仅表示所有权清单和文件哈希正确；LOADED还需当前目标进程加载了与清单一致的后端Runtime。当前七条固定路线尚未接入可靠、版本化、可核验的NR/XeFG有效处理帧接口，因此ACTIVE和EXPERIENCE_CONFIRMED不会被自动伪造；服务器也拒绝当前客户端声称这两个状态以免虚增成功率。

仅阅读本次安装记录之后新增的有界日志片段，归类代理加载、初始化、模型编译、设备异常等线索；旧日志不计入执行证据。崩溃/画面异常可由用户明确上报，并保留为用户反馈。无可靠证据的进程退出不推断为崩溃。

失败后只展示有对应fixes、有效版本和充分验证的替代；先运行原恢复，再重新检测，用户第二次确认才应用。NR模型失败不把XeFG当成等价替代；反作弊失败停止注入，绝不自动绕过。下载重试和安装回滚复用现有有限机制，不无限轮换代理。

“上传本次验证”默认不勾选同意、不自动提交。上传前说明匿名标识、构建摘要、设备/驱动、方案版本、阶段结果；不上传用户名、完整路径或原始日志。上传复用现有telemetry/sessions接口和表，可使用现有删除运行数据接口撤回。V1不依赖FPS，也不制造FPS增益。

## 已知限制与运行要求

1. 本次未发布客户端、未部署服务器、未修改线上规则。新服务端和Windows客户端需同步部署。
2. 未录入任何虚假的实测规则。没有带实际构建/设备/驱动证据的smartRules时，真实界面显示“当前游戏暂无已验证的自动配置”。引擎与安装链路已经接通，游戏覆盖需维护者实测填充。
3. 本机为macOS，无法执行Windows dxdiag、DPAPI、AMD GPU推理和真实游戏注入。构建/规则/文件事务/HTTP测试通过不等于游戏效果实测通过。
4. VRAM预算、实际图形API自动采集、受保护在线模式完整识别和各Runtime有效处理帧接口存在底层缺口，保持UNKNOWN/INFERRED。反作弊识别复用现有有限检测，不宣称覆盖所有反作弊。
5. NeuralHub与外置工具不由MU管理具体游戏渲染状态；不得把打开工具当成安装或功能生效。
6. 当前真实上传只能积累安装、加载和用户反馈记录；尚不能凭这些形成“功能生效”的高可信社区自动推荐。需要后续接入并验证运行时执行证据接口。

## 本次构建与回归结果（2026-10-18）

- Windows客户端：`dotnet build src/AmdNrAssistant.csproj --no-restore`通过，0警告、0错误；未打包EXE。
- React：生产构建通过，13个测试文件、46项测试通过；现有工牌依赖仍有大分块体积提示。
- 推荐引擎：40项断言通过，覆盖规则、硬限制、评分、缓存异常和真实文件恢复。
- 既有管理/安装集成：主流程累计213项断言通过；另有神经渲染策略25项、方案策略14项断言通过。
- 客户端账户72项、兼容性API与检测fixture54项断言通过。
- 服务端账户193项、兼容性服务86项、元数据29项断言通过。
- 新规则HTTP集成：21项断言通过，使用真实TestServer/SQLite，覆盖旧客户端响应、鉴权、去重、隐私字段、禁用和审计回滚。
- 浏览器检查保留7张卡片、付费入口、未知状态提示；1024/1280宽度无横向溢出，无页面运行错误。浏览器预览不模拟Windows推荐结果。

macOS主机运行net8测试使用`DOTNET_ROLL_FORWARD=Major`；文件事务测试的TMPDIR使用工作区真实目录，避免`/var`符号链接触发现有防链接检查。上述结果不包含Windows硬件或游戏效果实测。

回归时补齐现有测试工程引用但缺失的`AccountApiClient.Compatibility.cs`薄封装，并修正兼容性业务请求429导致账户被错误标为离线的问题；心跳/刷新令牌限流仍执行原有短期离线状态策略。
