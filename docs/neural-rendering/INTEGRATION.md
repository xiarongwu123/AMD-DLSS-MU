# MU 原生神经渲染方案

## 当前接入

64 位新方案使用存储 ID **6**，名称“DLSS 5 神经渲染”。旧 ID 0 的安装、恢复和玩家分享保持兼容；32 位仍走 ID 5 的实验桥接，不能加载这里的 64 位 Runtime。原画质目标、帧率目标、默认选择和原有方案均保持不变。新方案以“DLSS 5 神经渲染（多后端）”追加到自定义清单，只有用户主动选择时使用；普通画质/帧率目标不会自动改用新方案。

上游固定为 [TheAutomatic v1.12.1](https://github.com/TheAutomatic/dlss-5-amd-project/releases/tag/v1.12.1)，源码提交 `05f3c20b7bd32c239a9d4609890b88300e971efd`。
安装包 SHA256：`790b862c87898ebb43ba23dfa0e41c7733f919752661f05a43e835abd9a601bf`。
`assets/neural-rendering-1.12.1.json` 保存原始 ZIP 和各文件的版本、大小、摘要。更新上游必须重新审核 ABI、依赖闭包、参数、许可与摘要；不能只改下载 URL。

MU 自己下载、校验、解压、选择后端、备份、部署、逐键写配置、校验、回滚。不运行 Setup.bat、Setup.ps1 或上游卸载器。仅在用户提供相应源 DLL 且缺少 Mochizuki 模型时，调用包内经过固定哈希校验的 Python 数据转换工具；不加载或执行该 NVIDIA DLL。需要安装带 `py` 启动器的 Python 3.10+；也可直接导入预先提取的模型，无需 Python。

## 后端和推荐边界

| 设备 / 规则 | 默认选择 | 默认标记 |
| --- | --- | --- |
| 匹配当前游戏 ID、显卡系列和接口的有效远程规则 | 遵循规则 | 遵循验证范围，手动改参数后不沿用已验证结论 |
| RX 9000，无匹配规则 | Mochizuki，1 Pass | 实验性 |
| RX 6000 / 7000，无匹配规则 | Daniel，1 Pass | 未验证 |
| 未能确认 AMD RX 系列或图形接口 | 提示具体缺少的信息 | 不猜测接口、不部署错误位数的组件 |

Mochizuki 不检查 HIP；Daniel/lmxxf 检查 HIP，保留 RX 6000 的 HIP 7.2 安装流程。lmxxf 的固定包包含 gfx1200/gfx1201 模块；其他 GPU 手动尝试必须显示未验证，不能标为稳定支持。EXE 导入表可识别 DX11 / DX12 / Vulkan；同时导入多个接口或动态加载时要求用户在高级设置确认实际接口。检测到接口、文件校验通过、能够启动，均不等于已验证运行效果。

Mochizuki 1/2/3 Pass 分别标为标准/增强/高质量；4–8 为实验，9–30 为实验/压力测试。超过 3 时设置 `MochizukiUnlockPasses=true`，实际数量写入 `MochizukiPasses`，`MochizukiMaxPasses=0`。不通过旧 `MochizukiLayerMode` 控制数量。保留模型比例、时序、历史强度、应用模型、DRS 高级选项。

## 资源来源与许可边界

| 类别 | 本次行为 |
| --- | --- |
| MU 自有实现、资源版本/摘要元数据 | 随客户端分发 |
| OptiScaler NR Host、匹配的 lmxxf/Mochizuki Runtime、计算模块、shader、开源依赖 | 从上游固定的官方 Release 下载；随游戏部署 LICENSE、NOTICE 和 Licenses，源码链接指向固定提交 |
| Daniel Runtime、NVIDIA DLL、NR 模型/权重 | 用户合法取得后通过本机资源入口导入；本方案不把它们重新托管或嵌入客户端 |

此方案不会使用旧方案内置的 NVIDIA DLL 自动填补资源，也不会执行 Daniel 的安装器。Daniel 只接受上游白名单的 0.6.0 PE image；附加设置 overlay 不计入 PE image 身份，但复制完整文件，并记录完整文件 SHA256。权重按上游的大小边界检查；没有固定权重认证资料，不能称为完整模型认证。

Mochizuki 模型要求 147,756,560 字节，SHA256 `2b41c888cf4155b8958c665ba64018ab0bd25c85fc71a2b6db86d0d04d1f7fbd`；用于提取的 310.8.0 源 DLL 要求 SHA256 `e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e`。这与旧方案内置的另一份 310.8 DLL 摘要不同，不能混用作 Mochizuki 提取源。

lmxxf 用户模型按上游 block0 标记检查并对逐文件复制做 SHA256 一致性校验；尚无完整内容认证数据，因此界面不声称完整模型已经通过上游认证。没有模型则停止安装，不显示“已启用”。

未默认部署可选 person-worker/YOLO 模型或 OptiPatcher；未为它们添加许可推断、重分发或任意程序执行入口。

## INI、manifest 与恢复

新方案在既有 `GameManagement` 安装记录的 `Neural` 字段保存 manifest：scheme、backend、passes、proxy、packageVersion、graphicsApi、verification、ruleRevision、installedFiles、modifiedConfigKeys、backupId、options。每项文件保存完整哈希和来源；INI 键保存安装前后的值。备份位于该游戏状态目录的 `backups/<backupId>/`。

意图清单和备份在首次游戏文件写入前保存，复制使用临时文件校验后原子替换，记录正在安装时也可按意图恢复。下载继承现有的重试、续传、SHA256、取消和下载任务；文件部署中途取消/失败会自动回滚。回滚失败明确报错并保留记录/备份，不调用旧版目录猜测恢复来覆盖新清单。

已有 INI 不使用包内模板覆盖，仅修改受管 DlssNr 键和目标 EXE 名称。保留其他节、参数、注释、行尾和 UTF-8/UTF-16 BOM；重复节/键和无法识别的编码会停止安装。自定义依赖目录需要先核对，不能静默改为默认路径。

恢复先检查全部文件/备份/目录，拒绝删除被其他软件改过的 Runtime。后续修改的 XeFG/FG/其他 INI 键会保留；受管参数恢复为安装前的值，并把编辑过的 INI 归档。仅删除 manifest 记录的新增文件和变空的组件目录，不递归清扫未知文件。切换后端、代理或升级完整包，先通过 manifest 恢复再部署；同后端高级参数通过逐键事务保存。

玩家方案支持新 ID 6，但只接受显式白名单的标量参数。不能分享路径、代理 DLL、模型、脚本或旧 LayerMode；30 Pass 必须同时明确解锁。复用不同后端前先恢复并部署匹配 Runtime，不能只改 NrBackend 假装已安装。

## 远程规则后台

服务端新增 `/admin/neural-rules`，沿用管理员身份与表单防伪校验。规则以 JSON 持久化，发布原因、操作人、修改前后内容保存为历史；revision 防止多人编辑覆盖。

配置 `NeuralRendering:RulesFile` 为持久化绝对路径，默认 `App_Data/neural-render-rules.json`。首次为空，不内置任何未经验证的具体游戏参数。客户端请求 `/api/v1/render-rules?gameId=steam:12345`。非 Steam 游戏使用现有 `title:<标题摘要>` ID；不是临时 UI 卡片 ID。

```json
{
  "schemaVersion": 1,
  "revision": "填写新的版本标识",
  "expiresUtc": "填写未来30天内的UTC时间",
  "rules": []
}
```

单条规则字段：gameId、scheme（固定 dlss5-neural-rendering）、backend、proxy、gpuSeries（rx6000/rx7000/rx9000）、graphicsApis（dx11/dx12/vulkan）、status（verified/community/experimental/unverified）、passes、nrInline、earlyAnyModuleWrap、allowEnhancedBarriers。一个游戏/系列/接口只能匹配一条。禁止任意键、URL 或命令。不内置按 EXE 名包含 forza 等关键词开启参数的规则。

高级参数预览短期复用有效规则，**每次真实安装主动查询最新规则**；网络失败只使用仍在有效期内的缓存，否则使用未验证默认值。服务器更新后无需重发客户端。未部署该服务端改动时，客户端会显示默认未验证状态，不能称远程规则已上线。

## 验证与实机验收

已增加 INI 保留、参数范围、远程规则范围/过期、社区参数白名单、成功回执、30 Pass 标记、真实发布包文件事务、取消/失败回滚、中断安装恢复及后台 HTTP 测试。

真实发布包事务测试使用**合成 lmxxf 模型数据和合成 EXE**，只验证文件操作。没有在 macOS 上运行 Windows DLL，没有验证任何游戏画质/FPS或 RX 6000/7000/9000 的实际稳定性。发布前仍需在对应 Windows AMD 设备上分别验收所用后端、模型、DX11/DX12/Vulkan 路径、首次模型编译、切换方案和恢复。

本次只修改代码；不打包、不推送、不发布 Release、不部署生产服务器。
