# 高级会员支持游戏库

`GET /api/v1/catalog/supported-games` 要求登录、有效 Pro 会员和已启用的 `games.supported.view` 权限。新建权限默认仅 Pro 可用。普通会员和过期会员返回 403；成功响应禁止缓存。

## 私有名单

在构建服务端前准备 `server/private/supported-games.json`。该目录已从 Git 排除，请通过私有渠道提供文件，不要提交到公开仓库，也不要放到网站静态目录。

结构：

```json
{"asOf":"2026-10-14","games":[{"title":"游戏标题","canonicalName":"游戏英文名","steamAppId":null,"aliases":["上报名称"]}]}
```

构建时名单嵌入服务端程序集；客户端不内置完整名单。没有提供私有文件时，服务端其他功能仍可运行，该接口向会员返回“支持游戏库暂未配置”。

上线需要同步更新客户端和服务端，沿用现有 Pro 会员开通与有效期。没有部署新服务端时，新客户端无法读取付费名单。

会员开通文案使用“100+实测游戏”，当前私有名单满足该数量。测试数据与真实名单应保持分开；发布前核对实际名单数量。

## 验证

`dotnet run --project server/Mu.Server.HttpTests/Mu.Server.HttpTests.csproj -c Release -- --supported-games`
