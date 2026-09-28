# 发行管理

后台地址：`https://amd-dlss-mu.claude-api.cn/admin`。

后台包含数据总览、软件发行、用户反馈、指标与埋点四个页面。数据总览支持时间范围选择、自动刷新和大屏模式；反馈可查看详情并标记处理状态。完整统计口径、已接入事件及 Windows 客户端下一阶段方案见 `ANALYTICS.md`。

首次密码只存放在服务器的 `/home/xrw/amd-dlss-mu-site/data/admin-initial-password.txt`，权限为 `0600`。可通过以下命令读取：

```sh
ssh -p 1013 xrw@72.11.138.132 'cat /home/xrw/amd-dlss-mu-site/data/admin-initial-password.txt'
```

更新安装包时，先在 `xiarongwu123/AMD-DLSS-MU` 的 GitHub Release 发布正式版本，附件必须命名为 `AMD-DLSS-MU.exe`，并提供 SHA-256 digest。随后在后台输入版本标签（例如 `v1.3.0`），点击“预览并校验”，核对大小及哈希，再确认发布。官网会立即更新下载目标、版本、大小、日期及哈希；下载请求跳转到 GitHub 附件，不经本机服务器传输大文件。

后台只允许从上述官方仓库的正式 Release 选择附件，不接受任意 URL 或网页上传。发行配置保存在 `data/release.json`，与反馈数据一起由 Docker 卷持久化。`PUBLIC_ORIGIN` 与 `ADMIN_PASSWORD_HASH` 存在服务器 `.env`，不应进入公开目录或源码包。服务端口只绑定 `127.0.0.1:8088`，由现有 Cloudflare Tunnel 对外提供 HTTPS。
