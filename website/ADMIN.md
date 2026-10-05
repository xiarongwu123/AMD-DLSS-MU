# 发行管理

后台地址：`https://amd-dlss-mu.claude-api.cn/admin`。

后台包含数据总览、软件发行、用户反馈、指标与埋点四个页面。数据总览支持时间范围选择、自动刷新和大屏模式；反馈可查看详情并标记处理状态。完整统计口径、已接入事件及 Windows 客户端下一阶段方案见 `ANALYTICS.md`。

首次密码只存放在服务器的 `/home/xrw/amd-dlss-mu-site/data/admin-initial-password.txt`，权限为 `0600`。可通过以下命令读取：

```sh
ssh -p 1013 xrw@72.11.138.132 'cat /home/xrw/amd-dlss-mu-site/data/admin-initial-password.txt'
```

在“软件发行”选择本地 `AMD-DLSS-MU.exe`、填写更新说明，点击“上传并校验”。后台以 4 MiB 分片上传，网络暂时中断会查询已接收进度并重试。页面内重试可继续上传，关闭或刷新页面后需重新选择文件上传。

服务器自动读取 Windows x64 EXE 的内部版本、检查客户端名称并计算 SHA-256。确认预览中的版本、大小和哈希后，勾选确认并点击“确认发布更新”。仅在文件校验完成后原子切换版本；失败时线上包保持不变。文件最大 1 GiB，新版本必须高于线上版本，不能用同版本的不同文件覆盖更新。

也可展开“从 GitHub 导入”，沿用正式 Release 同步流程。发布后官网直接传输文件，客户端通过 `/api/updates/latest` 获取更新，并使用含版本与哈希的固定 `/updates/.../AMD-DLSS-MU.exe` 地址下载。历史包保留，发布新版本不会改变正在下载的旧地址。

从 2.0.6 开始，客户端检查更新与下载都优先使用官网，官网失败后回退 GitHub。备用下载需要在 `xiarongwu123/AMD-DLSS-MU` 发布相同版本、相同字节的 EXE；仅上传官网也能供新版客户端下载，不依赖 GitHub 发布。2.0.5 及更早客户端仍从 GitHub 获取首次升级。

上传、分片、校验和发布接口均要求现有管理员登录，并校验同源请求；上传记录绑定当前登录会话。未完成的临时分片两小时后清理。服务器不会执行上传的程序。发行配置保存在 `data/release.json`，历史描述在 `data/releases/`，安装包在 `data/packages/`，由 Docker 卷持久化。`PUBLIC_ORIGIN` 与 `ADMIN_PASSWORD_HASH` 保留在服务器 `.env`。服务只绑定 `127.0.0.1:8088`，由现有 Cloudflare Tunnel 提供 HTTPS。
