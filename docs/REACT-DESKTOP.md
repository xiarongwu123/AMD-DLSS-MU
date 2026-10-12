# Windows React 客户端

开发分支：`feature/react-desktop`。这是独立测试版本，没有修改服务端、官网或正式版发布。

## 架构

- Windows `.exe`：现有 .NET 8 宿主，通过 WinForms WebView2 控件承载 React。
- 页面：`desktop-ui`，React 19 + TypeScript + Vite；资源随 exe 内置，不依赖远程页面或开发服务器。
- 本地桥接：`src/MainForm.React.cs`，按命令逐项调用现有 C# 服务。
- 安装器、文件验证、事务备份、HIP 安装、账号凭证和更新地址保留原有逻辑。
- 原 WinForms 控件暂时作为旧业务代码的内部适配器保留，不显示旧主界面。并非把安装器重写为 JavaScript；后续可将这些协调方法逐步抽为独立服务。
- 游戏中的原生插件菜单、F8 控制面板、Windows 文件选择框与 AMD 安装器仍为原生窗口。

## 页面与功能衔接

| 页面 | 接入的实际功能 |
| --- | --- |
| 首页 / 游戏库 | 扫描、手动添加、拖入 EXE、搜索、分类、真实封面；首页下滑跳转游戏库；开启与启动互斥；游戏库悬停移除（保存排除记录，手动添加恢复，不改游戏文件） |
| 智能推荐 | 显卡 / 专用显存 / HIP 检测；画质调用模式一；帧率调用 DLSS 5 + XeFG；仅自定义显示三套方案清单 |
| 配置 | 原 InstallAsync；原 HIP 7.2 引导；原权重释放 / 校验 / 下载与备份 |
| 恢复 | 原 RestoreAsync；恢复前确认；旧组件清理前单独确认 |
| 一键排查 | 原 SmartRenderDiagnostics；逐项展开检查结果；关闭 / 继续配置进入智能推荐；不增加运行状态、诊断预览或 F8 按钮 |
| 大力喜鹊 | 原自动准备 / 校验 / 启动流程 |
| 下载任务 | 下载事件与状态、暂停 / 继续 / 取消、重试、详情 |
| 许愿池 | 真实列表 / 搜索 / 筛选 / 分页、图片、提交、投票、评论；提交重试沿用同一 submissionId |
| 账户 | 原登录、注册、验证码、重置 / 修改密码、退出、联网验证 |
| 人工收款 | 原服务端启用后显示；真实订单、付款说明、待核对状态；不在客户端授予会员 |
| 设置 / 帮助 / 关于 | 深浅主题、现有语言开关、更新开关、PresentMon、删除监测数据、官方链接、第三方许可 |
| 官网更新 | 原 AutoUpdate，仍使用官网更新地址 |

推荐条件只影响推荐标记，不会因为显存未确认而禁用画质 / 帧率目标；真正安装仍执行既有文件、安全和环境检查。

## 桥接边界

只允许本地 `https://mu.desktop.local` 页面调用命令；禁止跨域导航、iframe、弹出窗口和宿主对象。
游戏以扫描或 Windows 选择框获得的条目为准，React 使用游戏 ID，不能提交任意安装路径。
拖入文件使用 WebView2 原生 File 附加对象，不信任 JS 提交的路径。
账户 token 不传入 React；安装 / 恢复继续调用原账户授权与事务逻辑。
写操作串行执行；下载以稳定 ID 操作，重试会等待真实安装流程。
资源按内容哈希提取并重新校验，避免旧页面缓存混入新版本。

## 构建

需要 Node.js 22+、.NET SDK 和现有 `assets/nvngx_dlssnr.dll`。

Windows:

```powershell
./scripts/build-react-desktop.ps1
```

也可先运行 `npm ci --prefix desktop-ui` 与 `npm run build --prefix desktop-ui`，再按原方式 `dotnet publish`。
缺少 React 构建资源时构建会明确失败。

运行要求：Windows x64 与 Microsoft Edge WebView2 Runtime。没有 Runtime 时启动界面提供微软官方下载入口，不会自动运行未知安装程序。

## 验证与限制

已在 macOS 交叉编译 Windows 宿主、构建 React，并检查浏览器布局和交互。浏览器 `?preview=1` 是明确标记的界面样例；打包桌面版始终调用原生桥接，不使用样例。
Windows WebView2 宿主启动、真实 AMD 显卡读取、游戏配置和管理员安装器尚需 Windows 真机验证。交叉编译成功不等于这些运行场景已经实测。
