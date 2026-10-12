# 社区 DLSS 配置分享（测试版）

客户端入口：游戏的「智能推荐」→「看看朋友们怎么配」。用户可按当前游戏查看方案，打开详情检查参数，再一键应用；「分享我的配置」会从本机实际安装记录和 INI 生成快照，填写标题、说明和可选截图后上传。

## 数据与应用边界

- 游戏通过 Steam App ID 或规范化标题摘要匹配；不发送本地游戏路径。
- 仅分享配置文件中的短标量参数。`ProcessFilter`、进程名、`HipDevice`、路径、日志、DLL 和权重文件不会上传。
- 模式 0 使用 `dlssnr_on_amd.ini` 的 `[DlssNrOnAmd]`；模式 2、4 使用 `OptiScaler.ini` 的受控节。上传前读取真实安装记录，模式 2 同时保存 DX12/Vulkan 接口。
- 应用前检查游戏、路线、组件版本和参数；游戏必须关闭。若需切换路线，走现有恢复与安装流程。配置变更使用 `LivePanel.SaveConfig` 备份并原子写入。原有游戏文件恢复入口仍可用。
- 社区参数是玩家分享的设置，不等于已验证的帧率或画质效果。需在游戏中确认。

## 后台

服务启动时增量创建 `CommunityPresets` 表。`/api/v1/presets` 提供登录后的列表、详情和上传；同一提交 ID 去重，每个账户每天最多上传 5 条。截图在客户端重新编码为小于 200 KB 的 JPEG，后台仅接收该格式。当前测试版上传后直接对同游戏用户可见；正式开放前应补充审核和举报入口。

客户端与后台需同时部署；仅更新客户端时，列表会提示「社区方案后台尚未更新」。

## 本地验证

`npm test --prefix desktop-ui`、`npm run build --prefix desktop-ui`、`dotnet run --project server/Mu.Server.Tests/Mu.Server.Tests.csproj`、`DOTNET_ROLL_FORWARD=Major dotnet run --project tests/Management.Tests.csproj -- verify-presets`。
