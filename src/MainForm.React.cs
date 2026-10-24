using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Mu.Presets;
using Mu.Wishes;

namespace AmdNrAssistant;

// The React view calls existing audited installation services through this narrow adapter.
// Account tokens never leave AccountApiClient. Only games discovered/selected by the native picker are addressable.
public sealed partial class MainForm
{
    const string ReactOrigin = "https://mu.desktop.local";
    readonly JsonSerializerOptions reactJson = new(JsonSerializerDefaults.Web);
    WebView2? reactView;
    System.Windows.Forms.Timer? reactStateTimer;
    readonly Dictionary<string, TaskCompletionSource<bool>> reactPrompts = new();
    bool reactMutation;
    bool reactAccountMutation;
    bool reactReady;
    string lastReactState = "";
    string? reactFailure;
    readonly HashSet<string> reactRequests = new();

    async Task InitializeReactAsync()
    {
        var loading = new Label { Dock = DockStyle.Fill, Text = "正在启动 MU…", TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Base, ForeColor = Ink, Font = Font };
        Controls.Add(loading); loading.BringToFront();
        try
        {
            var folder = ExtractReactAssets();
            var environment = await CoreWebView2Environment.CreateAsync(null,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "webview"));
            reactView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Base };
            Controls.Add(reactView); reactView.BringToFront();
            await reactView.EnsureCoreWebView2Async(environment);
            var core = reactView.CoreWebView2;
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsWebMessageEnabled = true;
            core.SetVirtualHostNameToFolderMapping("mu.desktop.local", folder, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (!IsReactOrigin(e.Uri)) e.Cancel = true; reactReady = false; };
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += ReactMessageReceived;
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess) { loaded.TrySetResult(false); loading.Text = "界面加载失败：" + e.WebErrorStatus; loading.BringToFront(); return; }
                reactReady = true; loading.Visible = false; PublishReactState(true); loaded.TrySetResult(true);
            };
            core.ProcessFailed += (_, _) => { reactReady = false; loading.Text = "界面进程中断，请重新打开 MU。安装记录仍保存在本机。"; loading.Visible = true; loading.BringToFront(); };
            core.Navigate(ReactOrigin + "/index.html");
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token)) return;
            reactStateTimer = new System.Windows.Forms.Timer { Interval = 300 };
            reactStateTimer.Tick += (_, _) => PublishReactState();
            reactStateTimer.Start();
            FormClosed += (_, _) =>
            {
                reactStateTimer?.Dispose();
                foreach (var prompt in reactPrompts.Values) prompt.TrySetResult(false);
                reactPrompts.Clear();
                reactView?.Dispose();
            };
        }
        catch (Exception e)
        {
            loading.Text = "React 界面启动失败：" + e.Message + "\n\n需要 Microsoft Edge WebView2 Runtime。";
            var installRuntime = new Button { Text = "获取 Microsoft WebView2 Runtime", Dock = DockStyle.Bottom, Height = 48 };
            installRuntime.Click += (_, _) => Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });
            Controls.Add(installRuntime); installRuntime.BringToFront(); loading.BringToFront();
        }
    }

    static bool IsReactOrigin(string? uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && parsed.Scheme == "https" && parsed.Host == "mu.desktop.local" && parsed.Port == 443;

    static string ExtractReactAssets()
    {
        var assembly = typeof(MainForm).Assembly;
        var artwork = new Dictionary<string,string>
        {
            ["AmdNrAssistant.amd_dlss_mu_logo.png"]="amd_dlss_mu_logo.png",
            ["AmdNrAssistant.mu-home-banner-new.png"]="mu-home-banner-new.png",
            ["AmdNrAssistant.mu-add-game-art.png"]="mu-add-game-art.png",
            ["AmdNrAssistant.mu-account-art.png"]="mu-account-art.png",
            ["AmdNrAssistant.mu-wish-pool-hero.png"]="mu-wish-pool-hero.png",
            ["AmdNrAssistant.mu-hero-art.png"]="mu-hero-art.png",
            ["AmdNrAssistant.WeChatQr"]="wechat-xrwCoder.jpg"
        };
        var resources = assembly.GetManifestResourceNames().Where(n => n.StartsWith("ReactUI/", StringComparison.Ordinal) || artwork.ContainsKey(n)).Order().ToArray();
        if (!resources.Any(n => n == "ReactUI/index.html")) throw new IOException("构建缺少 React 页面，请先运行 desktop-ui 的 npm ci && npm run build。");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = new List<(string Name, byte[] Bytes)>();
        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var memory = new MemoryStream(); stream.CopyTo(memory);
            var bytes = memory.ToArray(); entries.Add((artwork.TryGetValue(resource, out var imageName) ? "artwork/" + imageName : resource[8..].Replace('\\', '/'), bytes));
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes(resource)); digest.AppendData(bytes);
        }
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "react-ui", Convert.ToHexString(digest.GetHashAndReset())[..24]);
        Core.RejectLinks(folder);
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in entries)
        {
            var path = Path.GetFullPath(Path.Combine(folder, name));
            if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("非法 UI 资源路径。");
            Core.RejectLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Revalidate extracted files to prevent an old or modified UI from running with native privileges.
            if (!File.Exists(path) || !SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(bytes)))
                File.WriteAllBytes(path, bytes);
        }
        return folder;
    }

    void ReactEvent(string name, object? data)
    {
        if (!reactReady || reactView?.CoreWebView2 == null || !IsReactOrigin(reactView.Source?.AbsoluteUri)) return;
        reactView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { @event = name, data }, reactJson));
    }
    object ReactState() => new
    {
        version = AutoUpdate.DisplayVersion, page = activePage switch { 1 => "library", 2 => "about", 3 => "downloads", 4 => "help", 5 => "settings", 6 => "account", 7 => "magpie", 8 => "wishes", _ => "home" },
        games = libraryGames.Select(g => new { id = ReactGameId(g.ExePath), g.Title,
            searchTerms = GameSearch.Terms(g.Title, g.SteamAppId, g.InstallDirectory),
            cover = ReactCover(g), state = GetLibraryState(g).ToString(), mode = GameManagement.Read(g.ExePath)?.Mode, g.SteamAppId }).ToArray(),
        account = accountClient.Account, online = accountClient.IsOnline, reconnecting = accountClient.IsReconnecting, viewAvailable = accountClient.CanKeepVerifiedView,
        busy=busy||smartApplying, scanning = !scan.Enabled, status = ReactAccountStatus(status.Text), accountFeedback = ReactAccountStatus(accountFeedback),
        magpiePreparing,
        settings = new { dark = darkMode, english, autoUpdate = autoCheckUpdates, presentMon = presentMonPath ?? "" },
        downloads = downloadRows.Select(r => new { id = r.Id, r.Title, r.State, r.Percent, r.Size, r.Detail, paused = r.Session.Paused, r.Independent }).ToArray(),
        magpieStatus = magpieStatus.Text
    };
    string ReactAccountStatus(string text) => accountClient.IsOnline && IsAccountConnectionFeedback(text)
        ? "账户连接已恢复。" : text;
    readonly Dictionary<string, (DateTime Time, string? Data)> reactCoverCache = new();
    string? ReactCover(GameCandidate game)
    {
        if (game.CoverPath is not { } path || !File.Exists(path)) return null;
        var time = File.GetLastWriteTimeUtc(path);
        if (reactCoverCache.TryGetValue(path, out var cached) && cached.Time == time) return cached.Data;
        if (new FileInfo(path).Length > 8 * 1024 * 1024) return null;
        using var image = Image.FromFile(path);
        using var resized = new Bitmap(image, new Size(310, 394));
        using var memory = new MemoryStream(); resized.Save(memory, System.Drawing.Imaging.ImageFormat.Jpeg);
        var data = "data:image/jpeg;base64," + Convert.ToBase64String(memory.ToArray());
        reactCoverCache[path] = (time, data); return data;
    }
    static string ReactGameId(string exe) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(exe.ToUpperInvariant())))[..24];
    string ReactGame(JsonElement args)
    {
        var id = Arg(args, "gameId");
        return libraryGames.FirstOrDefault(g => ReactGameId(g.ExePath) == id)?.ExePath ?? throw new IOException("游戏不在本机游戏库中，请重新扫描。");
    }
    string ReactPresetGameKey(string exe)
    {
        var game = libraryGames.FirstOrDefault(g => g.ExePath.Equals(exe, StringComparison.OrdinalIgnoreCase))
            ?? throw new IOException("游戏不在本机游戏库中。");
        if (game.SteamAppId is { Length: > 0 } steam && steam.All(char.IsAsciiDigit)) return "steam:" + steam;
        var title = game.Title.Trim().ToLowerInvariant();
        return "title:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(title)))[..24];
    }
    string ReactPresetGameName(string exe) => libraryGames.First(g => g.ExePath.Equals(exe, StringComparison.OrdinalIgnoreCase)).Title;
    static string CurrentPresetVersion(int mode, string? backend = null) => mode switch
    {
        0 => Core.ReviewedTag,
        2 => "OptiScaler " + OptiInstaller.Version,
        4 => "DLSS5 + XeFG 2X (v1.10.3.1/v10.6)",
        6 => backend == Mu.Neural.NeuralBackends.LmxxfRdna3 ? "AMDNR " + Mu.Neural.NeuralBackends.AmdNrVersion : NeuralRenderInstaller.Version,
        8 => LegacyBridge.Version,
        _ => throw new IOException("未知图形方案。")
    };
    static string Arg(JsonElement args, string name, string fallback = "") => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    static bool Flag(JsonElement args, string name, bool fallback = false) => args.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    static int Number(JsonElement args, string name, int fallback = 0) => args.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    NeuralOptions ReadNeuralOptions(JsonElement args)
    {
        var options = args.TryGetProperty("neural", out var element)
            ? element.Deserialize<NeuralOptions>(reactJson) ?? throw new IOException("神经渲染参数为空。") : new();
        NeuralRenderPlanner.Validate(options); return options;
    }
    static object? NeuralReceipt(GameRecord? record) => record is { Phase: "installed", Neural: { } n }
        ? new { n.Backend, n.Passes, n.Proxy, n.GraphicsApi, n.Options, n.PackageVersion,
            statusLabel = Mu.Neural.NeuralRules.StatusLabel(n.Verification),
            modeLabel = n.Passes switch { 1 => "标准", 2 => "增强", 3 => "高质量", <= 8 => "实验", _ => "实验 / 压力测试" } } : null;
    void PublishReactState(bool force = false)
    {
        if (!reactReady) return;
        try { var json = JsonSerializer.Serialize(ReactState(), reactJson); if (!force && json == lastReactState) return; lastReactState = json; ReactEvent("state", JsonSerializer.Deserialize<JsonElement>(json)); }
        catch (Exception e) { Diagnostics.Record(selectedGame ?? "", "react-state", "error", e.Message); }
    }
    async void ReactMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsReactOrigin(e.Source) || !IsReactOrigin(reactView?.Source?.AbsoluteUri)) return;
        string? id = null;
        try
        {
            if (e.WebMessageAsJson.Length > 3 * 1024 * 1024) throw new IOException("请求过大。");
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            id = Arg(root, "id"); if (!Guid.TryParse(id, out _) || !reactRequests.Add(id)) return;
            var command = Arg(root, "command");
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : JsonSerializer.SerializeToElement(new { });
            var result = await ReactCommandAsync(command, args, e.AdditionalObjects);
            ReactReply(id, true, result, null);
        }
        catch (Exception error) { if (id != null) ReactReply(id, false, null, error is AccountApiException ? AccountError(error) : error.Message); }
        finally { if (id != null) reactRequests.Remove(id); PublishReactState(); }
    }
    void ReactReply(string id, bool ok, object? result, string? error)
    {
        if (reactReady && reactView?.CoreWebView2 != null && IsReactOrigin(reactView.Source?.AbsoluteUri))
            reactView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok, result, error }, reactJson));
    }
    async Task ReactAuthorize(string feature)
    {
        if (!await RequireFeatureAsync(feature)) throw new IOException(accountFeedback.Length > 0 ? accountFeedback : "当前账户无法使用此功能。");
    }
    async Task<object?> ReactCommandAsync(string command, JsonElement args, IReadOnlyList<object>? additionalObjects = null)
    {
        switch (command)
        {
            case "state": return ReactState();
            case "promptReply":
                if (reactPrompts.Remove(Arg(args, "promptId"), out var prompt)) prompt.TrySetResult(Flag(args, "accepted"));
                return null;
            case "window":
                switch (Arg(args, "action")) { case "minimize": WindowState = FormWindowState.Minimized; break; case "maximize": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break; case "close": Close(); break; case "drag": ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); break; }
                return null;
            case "navigate":
                var page = Arg(args, "page") switch { "home" => 0, "library" => 1, "about" => 2, "downloads" => 3, "help" => 4, "settings" => 5, "account" => 6, "magpie" => 7, "wishes" => 8, _ => throw new IOException("未知页面。") };
                if (!CanNavigateToPage(page)) throw new IOException("请先登录 MU 账户。");
                SwitchPage(page); return null;
            case "login": case "register": case "resetPassword": case "changePassword": case "logout": case "sendCode": case "refreshAccount": case "enterHome":
                if (reactAccountMutation || accountRequestBusy) throw new IOException("账户请求正在处理中。");
                reactAccountMutation = true; accountRequestBusy = true;
                try
                {
                    switch (command)
                    {
                        case "login": await accountClient.LoginAsync(Arg(args,"email"),Arg(args,"password"),Flag(args,"keep",true),lifetime.Token); accountFeedback="登录成功。"; SwitchPage(0); await ScanGamesAsync(); break;
                        case "register": await accountClient.RegisterAsync(Arg(args,"email"),Arg(args,"code"),Arg(args,"password"),Flag(args,"keep",true),lifetime.Token); accountFeedback="账户已创建。"; SwitchPage(0); await ScanGamesAsync(); break;
                        case "sendCode": var purpose=Arg(args,"purpose"); if(purpose is not ("register" or "reset")) throw new IOException("验证码用途无效。"); return await accountClient.SendCodeAsync(Arg(args,"email"),purpose,lifetime.Token);
                        case "resetPassword": await accountClient.ResetPasswordAsync(Arg(args,"email"),Arg(args,"code"),Arg(args,"password"),lifetime.Token); break;
                        case "changePassword": await accountClient.ChangePasswordAsync(Arg(args,"current"),Arg(args,"password"),lifetime.Token); break;
                        case "logout": if (busy || accountTransactionDepth > 0) throw new IOException("请先等待安装或恢复操作完成。"); await accountClient.LogoutAsync(lifetime.Token); SwitchPage(6); break;
                        case "refreshAccount": await accountClient.HeartbeatAsync(lifetime.Token); accountFeedback = "会员状态已更新。"; status.Text = accountFeedback; break;
                        case "enterHome":
                            await accountClient.HeartbeatAsync(lifetime.Token); accountFeedback = "账户状态已更新。"; status.Text = accountFeedback;
                            await ReactAuthorize("library.manage"); SwitchPage(0);
                            if (libraryGames.Count == 0) await ScanGamesAsync();
                            break;
                    }
                    ApplyAccountGate(); return null;
                }
                catch (Exception error) { accountFeedback = AccountError(error); status.Text = accountFeedback; throw; }
                finally { reactAccountMutation=false; accountRequestBusy=false; ApplyAccountGate(); }
            case "removeGame":
                await ReactAuthorize("library.manage");
                if (busy || reactMutation || !scan.Enabled) throw new IOException("请等待当前操作完成。");
                var removedExe = ReactGame(args); GameLibrary.Remove(removedExe);
                libraryGames.RemoveAll(g => g.ExePath.Equals(removedExe, StringComparison.OrdinalIgnoreCase));
                reactCoverCache.Clear(); libraryStateCache.Clear(); telemetryMonitor?.SetGames(libraryGames);
                if (selectedGame == removedExe) selectedGame = null;
                return null;
            case "presetSnapshot":
            {
                await ReactAuthorize("configuration.edit");
                var exe = ReactGame(args);
                var (record, settings) = CommunityPresetConfig.Capture(exe);
                return new { mode = record.Mode, componentVersion = record.Version,
                    vulkan = CommunityPresetConfig.UsesVulkan(record),
                    gameName = ReactPresetGameName(exe), settings };
            }
            case "presets":
            {
                var presetPage = int.TryParse(Arg(args, "page"), out var requestedPage) ? requestedPage : 1;
                return await accountClient.GetPresetsAsync(lifetime.Token, presetPage);
            }
            case "preset": return await accountClient.GetPresetAsync(Arg(args, "id"), lifetime.Token);
            case "submitPreset":
            {
                await ReactAuthorize("configuration.edit");
                var includeConfiguration = !args.TryGetProperty("includeConfiguration", out _) || Flag(args, "includeConfiguration");
                var scope = Arg(args, "scope") is "universal" ? "universal" : "game";
                var exe = includeConfiguration || scope == "game" ? ReactGame(args) : null;
                var mode = -1; var version = "resources-v1"; var vulkan = false;
                PresetSetting[] settings = [];
                var goal = "custom";
                if (includeConfiguration)
                {
                    var captured = CommunityPresetConfig.Capture(exe!);
                    mode = captured.Record.Mode; version = captured.Record.Version; settings = captured.Settings;
                    vulkan = CommunityPresetConfig.UsesVulkan(captured.Record); goal = Arg(args, "goal");
                    if (goal is not ("quality" or "performance" or "custom") ||
                        (goal == "quality" && mode is not (0 or 6)) || (goal == "performance" && mode != 4))
                        throw new IOException("分享类型与实际安装方案不匹配。");
                }
                PresetAttachmentUpload? attachment = null;
                var files = (additionalObjects ?? Array.Empty<object>()).OfType<CoreWebView2File>().ToArray();
                if (files.Length > 1) throw new IOException("每条分享最多上传一个附件，多文件请先打包。");
                if (files.Length == 1)
                {
                    var path = files[0].Path; Core.RejectLinks(path);
                    var info = new FileInfo(path);
                    if (info.Length is < 1 or > PresetResources.MaxAttachmentBytes) throw new IOException("附件不能超过10 MB。");
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (input.Length is < 1 or > PresetResources.MaxAttachmentBytes) throw new IOException("附件不能超过10 MB。");
                    var bytes = new byte[checked((int)input.Length)];
                    await input.ReadExactlyAsync(bytes, lifetime.Token);
                    attachment = new(info.Name, Convert.ToBase64String(bytes)); PresetResources.ValidateAttachment(attachment);
                }
                return await accountClient.SubmitPresetAsync(new PresetSubmission(
                    Arg(args, "submissionId"), exe == null ? "universal" : ReactPresetGameKey(exe), exe == null ? "通用资源" : ReactPresetGameName(exe),
                    mode, goal, vulkan, version, Arg(args, "title"), Arg(args, "description"),
                    Arg(args, "imageData") is { Length: > 0 } image ? image : null, settings,
                    Arg(args, "thumbnailData") is { Length: > 0 } thumbnail ? thumbnail : null, scope,
                    Arg(args, "resourceUrl"), Arg(args, "resourceCode"), attachment), lifetime.Token);
            }
            case "presetAttachment":
            {
                var download = await accountClient.GetPresetAttachmentAsync(Arg(args, "id"), lifetime.Token);
                var bytes = PresetResources.ValidateAttachment(new(download.Attachment.Name, download.Data));
                if (bytes.Length != download.Attachment.Size || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(download.Attachment.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("附件校验失败，请重新下载。");
                using var save = new SaveFileDialog { FileName = download.Attachment.Name, Filter = "附件文件|*.*", OverwritePrompt = true };
                if (save.ShowDialog(this) != DialogResult.OK) return false;
                Core.RejectLinks(save.FileName);
                var temporary = save.FileName + ".mu-download-" + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, lifetime.Token);
                    Core.RejectLinks(save.FileName); File.Move(temporary, save.FileName, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return true;
            }
            case "presetResource":
            {
                var detail = await accountClient.GetPresetAsync(Arg(args, "id"), lifetime.Token);
                var resourceLink = PresetResources.ValidateUrl(detail.Item.ResourceUrl) ?? throw new IOException("分享者未提供网盘链接。");
                if (Flag(args, "copy")) Clipboard.SetText(resourceLink + (detail.Item.ResourceCode is { Length: > 0 } code ? "\r\n提取码：" + code : ""));
                else Process.Start(new ProcessStartInfo(resourceLink) { UseShellExecute = true });
                return null;
            }
            case "applyPreset":
            {
                if (reactMutation || busy || smartApplying && !smartExecutingInstall) throw new IOException("请等待当前操作完成。");
                reactMutation = true; reactFailure = null;
                try
                {
                    await ReactAuthorize("configuration.edit");
                    var exe = ReactGame(args);
                    var preset = await accountClient.GetPresetAsync(Arg(args, "id"), lifetime.Token);
                    if (RenderSchemes.PremiumMode(preset.Item.Mode)) await ReactAuthorize("schemes.premium.configure");
                    if (preset.Item.Mode == -1) throw new IOException("这条分享仅包含资源，请下载附件或打开网盘查看使用说明。");
                    if (preset.Item.Scope != "universal" && preset.Item.GameKey != ReactPresetGameKey(exe)) throw new IOException("这是其他游戏的专用方案，请选择对应游戏或使用通用方案。");
                    PresetRules.Validate(preset.Item.Mode, preset.Settings);
                    if (preset.Item.Mode == 6) neuralOptions = CommunityPresetConfig.NeuralOptionsFromPreset(preset.Settings, preset.Item.ComponentVersion);
                    if (!CurrentPresetVersion(preset.Item.Mode, preset.Item.Mode == 6 ? neuralOptions.Backend : null).Equals(preset.Item.ComponentVersion, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("分享方案的组件版本与当前 MU 不同，暂不能一键复用。");
                    var installed = GameManagement.Read(exe);
                    if (installed is { Phase: "installed" } &&
                        (installed.Mode != preset.Item.Mode || !installed.Version.Equals(preset.Item.ComponentVersion, StringComparison.OrdinalIgnoreCase) ||
                         installed.Mode == 2 && CommunityPresetConfig.UsesVulkan(installed) != preset.Item.Vulkan ||
                         installed.Neural != null && installed.Neural.Backend != neuralOptions.Backend))
                    {
                        selectedGame = exe;
                        await RestoreAsync();
                        if (GameManagement.Read(exe) is { Phase: "installed" })
                            throw new IOException("尚未恢复旧方案，当前配置未改变。");
                        installed = GameManagement.Read(exe);
                    }
                    if (installed is not { Phase: "installed" })
                    {
                        selectedGame = exe;
                        installMode.SelectedIndex = preset.Item.Mode switch { 0 => 0, 4 => 2, 6 => 4, _ => 1 };
                        optiVulkan = preset.Item.Vulkan; optiFgInput = "nofg";
                        await InstallAsync();
                        if (reactFailure != null) throw new IOException(reactFailure);
                    }
                    if(preset.Item.Mode==6)await ValidateExistingSmartSettingsAsync(exe,CommunityPresetConfig.NeuralOptionsFromPreset(preset.Settings,preset.Item.ComponentVersion));
                    var localPreset = preset with { Item = preset.Item with { GameKey = ReactPresetGameKey(exe), GameName = ReactPresetGameName(exe) } };
                    await Task.Run(() => CommunityPresetConfig.Apply(exe, localPreset), lifetime.Token);
                    status.Text = "已应用社区方案：" + preset.Item.Title + "。效果请在游戏中确认。";
                    libraryStateCache.Clear();
                    return null;
                }
                finally { reactMutation = false; }
            }
            case "scan": await ScanGamesAsync(); return null;
            case "addGame":
                await ReactAuthorize("library.manage"); if (busy || reactMutation) throw new IOException("请等待当前操作完成。");
                using (var picker = new OpenFileDialog { Filter="游戏程序 (*.exe)|*.exe",CheckFileExists=true })
                {
                    if(picker.ShowDialog(this)!=DialogResult.OK)return null;
                    var exe=GameManagement.ResolveGameExecutable(picker.FileName); Core.ValidateGame(exe); GameLibrary.AddManual(exe);
                    if(!libraryGames.Any(g=>g.ExePath.Equals(exe,StringComparison.OrdinalIgnoreCase))) libraryGames.Add(new GameCandidate {Title=Path.GetFileNameWithoutExtension(exe),ExePath=exe,InstallDirectory=Path.GetDirectoryName(exe)!});
                    telemetryMonitor?.SetGames(libraryGames); libraryStateCache.Clear(); return ReactGameId(exe);
                }
            case "dropGames":
                await ReactAuthorize("library.manage"); if(busy || reactMutation)throw new IOException("请等待当前操作完成。");
                foreach(var file in (additionalObjects ?? Array.Empty<object>()).OfType<CoreWebView2File>().Take(20))
                {
                    var exe = GameManagement.ResolveGameExecutable(file.Path); Core.ValidateGame(exe); GameLibrary.AddManual(exe);
                    if(!libraryGames.Any(g=>g.ExePath.Equals(exe,StringComparison.OrdinalIgnoreCase)))libraryGames.Add(new GameCandidate { Title=Path.GetFileNameWithoutExtension(exe),ExePath=exe,InstallDirectory=Path.GetDirectoryName(exe)! });
                }
                telemetryMonitor?.SetGames(libraryGames);libraryStateCache.Clear();return null;
            case "recommend":
                await ReactAuthorize("library.manage"); var target=ReactGame(args);
                ReactEvent("detection",new {gameId=Arg(args,"gameId"),stage="正在读取显卡与专用显存"});
                var gpu=SmartRenderAdvisor.SelectGpu(await new CompatibilityEnvironmentReader().ReadGpusAsync(lifetime.Token));
                if(gpu==null)throw new IOException("没有读取到显卡，可以重新检测或选择自定义方案。");
                neuralGpuRead=Task.FromResult<Mu.Compatibility.CompatibilityGpu?>(gpu); neuralGpuReadAt=DateTime.UtcNow;
                ReactEvent("detection",new {gameId=Arg(args,"gameId"),stage="正在检查 HIP 和游戏组件"});
                var checks=await Task.Run(()=> {var rx=GameManagement.HasRx6000([gpu.Name]);var hip=rx?HipRuntime.HasVersion72():SmartRenderDiagnostics.HasHipRuntime(); return (quality:(hip||rx)&&!GameManagement.Check(target,true,true,0).Blocked,combined:(hip||rx)&&!GameManagement.Check(target,true,true,4).Blocked,hipPending:rx&&!hip);},lifetime.Token);
                var screen=Screen.FromControl(this).Bounds;
                var is32Bit=Core.IsX86(target);
                var graphicsApi="unknown";
                if(is32Bit)try{graphicsApi=X86Bridge.DetectApi(target);}catch(Exception e)when(e is IOException or BadImageFormatException){ }
                return new {gpu,width=screen.Width,height=screen.Height,recommendation=SmartRenderAdvisor.Recommend(gpu,checks.quality,checks.combined),hipSetupPending=checks.hipPending,is32Bit,graphicsApi};
            case "supportedGames": return await accountClient.GetSupportedGamesAsync(lifetime.Token);
            case "remoteService": await ReactAuthorize("services.remote"); return true;
            case "checks": await ReactAuthorize("diagnostics.use"); var checkExe=ReactGame(args); return await Task.Run(()=>SmartRenderDiagnostics.Check(checkExe),lifetime.Token);
            case "inspect": await ReactAuthorize("diagnostics.use"); var inspectExe=ReactGame(args); return await Task.Run(()=>new {check=GameManagement.Check(inspectExe,false),record=GameManagement.Read(inspectExe),runtime=GameManagement.Runtime(inspectExe)},lifetime.Token);
            case "exportDiagnostic":
                await ReactAuthorize("diagnostics.use"); var exportExe=ReactGame(args); var text=await Task.Run(()=>Diagnostics.Export(exportExe),lifetime.Token);
                if (!Flag(args,"save")) return text;
                using(var save=new SaveFileDialog {Filter="诊断文件 (*.json)|*.json",FileName="MU-diagnostic-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"}) if(save.ShowDialog(this)==DialogResult.OK){await ReactAuthorize("diagnostics.use");await File.WriteAllTextAsync(save.FileName,text,lifetime.Token);} return null;
            case "launch": await LaunchGameAsync(ReactGame(args)); return null;
            case "gameRecommendation": return await BuildSmartRecommendationAsync(args);
            case "recommendationApply": return await ApplySmartRecommendationAsync(args);
            case "recommendationVerify":
            {
                await ReactAuthorize("schemes.game.view");var observed=VerifySmart(ReactGame(args),Arg(args,"feedback","unknown"));
                return new{verification=observed.Result,fallback=observed.Advice};
            }
            case "recommendationFeedback": await SubmitSmartFeedbackAsync(ReactGame(args),args); return true;
            case "neuralStatus": return NeuralReceipt(GameManagement.Read(ReactGame(args)));
            case "neuralPlan":
            {
                await ReactAuthorize("schemes.premium.configure"); var exe = ReactGame(args);
                var plan = await BuildNeuralPlanAsync(exe, ReadNeuralOptions(args));
                return new { plan, missingResources = await Task.Run(() => NeuralRenderInstaller.MissingResources(exe, plan), lifetime.Token),
                    resourcePreparation = await Task.Run(() => NeuralRenderInstaller.ResourcePreparation(exe, plan), lifetime.Token),
                    installed = NeuralReceipt(GameManagement.Read(exe)) };
            }
            case "neuralResources":
            {
                if (busy || reactMutation) throw new IOException("请等待当前操作完成。");
                await ReactAuthorize("schemes.premium.configure");
                using var picker = new FolderBrowserDialog { Description = "选择你有权使用的模型与运行资源目录。MU 只导入模型、已核验 Runtime 和 NVIDIA DLL，不运行目录中的安装器。", UseDescriptionForTitle = true };
                if (picker.ShowDialog(this) != DialogResult.OK) return false;
                reactMutation = true;
                try { await Task.Run(() => NeuralRenderInstaller.ImportResources(picker.SelectedPath), lifetime.Token); return true; }
                finally { reactMutation = false; }
            }
            case "neuralUpdate":
            {
                if (busy || reactMutation) throw new IOException("请等待当前操作完成。");
                reactMutation = true;
                try
                {
                    await ReactAuthorize("schemes.premium.configure"); var exe = ReactGame(args);
                    var record = GameManagement.Read(exe);
                    if (record is not { Phase: "installed", Neural: not null }) throw new IOException("请先完成神经渲染配置。");
                    var options = ReadNeuralOptions(args);
                    await ValidateExistingSmartSettingsAsync(exe,options with{Backend=record.Neural.Backend});
                    var plan = await BuildNeuralPlanAsync(exe, options with { Backend = record.Neural.Backend, Proxy = record.Neural.Proxy, GraphicsApi = record.Neural.GraphicsApi });
                    using var transaction = BeginAccountTransaction();
                    await Task.Run(() => NeuralRenderInstaller.UpdateSettings(exe, plan), lifetime.Token);
                    return NeuralReceipt(GameManagement.Read(exe));
                }
                finally { reactMutation = false; libraryStateCache.Clear(); }
            }
            case "magpie": await StartMagpieAsync(); return null;
            case "amdMagpie": return await StartAmdMagpieAsync();
            case "configure": case "restore": case "daniel08": case "update":
                if (reactMutation || busy || smartApplying && !smartExecutingInstall) throw new IOException("请等待当前操作完成。");
                reactMutation=true; reactFailure=null;
                try
                {
                    if(command=="configure")
                    {
                        var mode=Number(args,"mode",-1); if(mode is not (0 or 2 or 4 or 5 or 6 or 8))throw new IOException("未知图形方案。");
                        var configureExe = ReactGame(args);
                        RenderSchemes.Validate(Arg(args,"scheme"), mode, Core.IsX86(configureExe));
                        var manualScheme=Arg(args,"scheme");
                        if(manualScheme.Length==0||manualScheme=="game")manualScheme=mode switch{4=>"fps",5 or 8=>"legacy",2=>"standard",_=>"compat"};
                        await ManualSmartPreflightAsync(configureExe,manualScheme,args);
                        if (RenderSchemes.PremiumMode(mode)) await ReactAuthorize("schemes.premium.configure");
                        selectedGame=configureExe; installMode.SelectedIndex=mode==8?5:mode==6?4:mode==0?0:mode==4?2:mode==5?3:1;
                        neuralOptions = ReadNeuralOptions(args);
                        if(neuralOptions.Backend=="auto"&&smartPreflightBackend!=null)neuralOptions=neuralOptions with{Backend=smartPreflightBackend};
                        x86GraphicsApi=InstallationContext.BridgeApi(mode,Arg(args,"x86Api","auto"));
                        optiVulkan=Flag(args,"vulkan");optiFgInput=Arg(args,"fgInput","nofg"); if(optiFgInput is not ("nofg" or "fsrfg" or "dlssg"))throw new IOException("未知帧生成入口。");
                        var installedSuccessfully=await InstallAsync(InstallationContext.Read(args));
                        if(reactFailure!=null)throw new IOException(reactFailure);
                        if(!installedSuccessfully||!IsConfigured(configureExe))throw new IOException(status.Text);
                        if(mode==6) return NeuralReceipt(GameManagement.Read(configureExe));
                    }
                    else if(command=="restore")
                    {
                        selectedGame=ReactGame(args);await RestoreAsync();
                        if(GameManagement.Read(selectedGame) is {Phase:not "restored"})throw new IOException(status.Text);
                    }
                    else if(command=="daniel08")return await StartDaniel08Async();
                    else await CheckAppUpdateAsync(false);
                    return null;
                }
                finally { reactMutation=false; smartPreflightExe=null; smartPreflightBackend=null; libraryStateCache.Clear(); }
            case "downloadAction":
                var row=downloadRows.FirstOrDefault(r=>r.Id==Arg(args,"id")) ?? throw new IOException("下载任务不存在。");
                switch(Arg(args,"action"))
                {
                    case "pause": if(row.Pause.Enabled){if(row.Session.Paused)await ReactAuthorize(row.FeatureKey);row.Session.TogglePause();}break;
                    case "cancel": row.Session.Cancel();break;
                    case "retry":
                        if(row.Independent){await row.RetryAction();break;}
                        if(reactMutation||busy)throw new IOException("请等待当前操作完成。");reactMutation=true;try{await row.RetryAction();}finally{reactMutation=false;}break;
                    default:throw new IOException("未知下载操作。");
                }
                return null;
            case "settings":
                await ReactAuthorize("configuration.edit"); ApplyTheme(Flag(args,"dark",darkMode),false);english=Flag(args,"english",english);autoCheckUpdates=Flag(args,"autoUpdate",autoCheckUpdates);SavePreferences();return null;
            case "presentMon":
                await ReactAuthorize("configuration.edit");using(var picker=new OpenFileDialog{Filter="PresentMon (*.exe)|*.exe",CheckFileExists=true})if(picker.ShowDialog(this)==DialogResult.OK){presentMonPath=picker.FileName;if(telemetryMonitor!=null)telemetryMonitor.PresentMonPath=presentMonPath;SavePreferences();}return null;
            case "eraseTelemetry": await ReactAuthorize("configuration.edit"); if(telemetryMonitor!=null)await telemetryMonitor.DeleteMyDataAsync(lifetime.Token);return null;
            case "panel": if (busy || reactMutation) throw new IOException("请等待当前操作完成。"); selectedGame=ReactGame(args);await ReactAuthorize("diagnostics.use"); OpenPanel();return null;
            case "wishes": return await accountClient.GetWishesAsync(Arg(args,"filter","all"),Arg(args,"query"),Arg(args,"sort","newest"),Math.Max(1,Number(args,"page",1)),lifetime.Token);
            case "wishDetail": return await accountClient.GetWishAsync(Arg(args,"id"),lifetime.Token);
            case "submitWish": return await accountClient.SubmitWishAsync(new WishSubmission(Guid.TryParse(Arg(args,"submissionId"), out var submissionId) ? submissionId.ToString("N") : throw new IOException("愿望提交标识无效。"),Arg(args,"content"),Arg(args,"imageData") is {Length:>0} data?data:null),lifetime.Token);
            case "voteWish": return await accountClient.VoteWishAsync(Arg(args,"id"),Flag(args,"voted"),lifetime.Token);
            case "commentWish": return await accountClient.CommentWishAsync(Arg(args,"id"),Arg(args,"content"),lifetime.Token);
            case "plans": return await accountClient.GetPurchasePlansAsync(lifetime.Token);
            case "orders": return await accountClient.GetPurchaseOrdersAsync(lifetime.Token);
            case "createOrder": return await accountClient.CreatePurchaseOrderAsync(Arg(args,"planId"),lifetime.Token);
            case "order": return await accountClient.GetPurchaseOrderAsync(Arg(args,"id"),lifetime.Token);
            case "reviewOrder": await accountClient.SubmitPurchaseForReviewAsync(Arg(args,"id"),lifetime.Token);return null;
            case "legal":
                var kind = Arg(args,"kind"); if (kind is not ("terms" or "privacy")) throw new IOException("未知协议。");
                Process.Start(new ProcessStartInfo(AccountApiClient.DefaultBaseUrl + "legal/" + kind) { UseShellExecute = true }); return null;
            case "licenses":
                var assembly = typeof(MainForm).Assembly; var contents = new List<string>();
                foreach (var name in assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Licenses.")))
                { using var stream=assembly.GetManifestResourceStream(name)!;using var reader=new StreamReader(stream);contents.Add(name+"\n\n"+reader.ReadToEnd()); }
                return string.Join("\n\n",contents);
            case "openLink":
                var url=Arg(args,"url"); if(!Uri.TryCreate(url,UriKind.Absolute,out var link)||link.Scheme!="https"||!new[]{"amd-dlss-mu.claude-api.cn","github.com","www.amd.com","learn.microsoft.com","mu-api.claude-api.cn"}.Contains(link.Host))throw new IOException("此链接未在官方链接列表中。");
                Process.Start(new ProcessStartInfo(link.AbsoluteUri){UseShellExecute=true});return null;
            default: throw new IOException("未知客户端指令。");
        }
    }

    async Task<DialogResult> UiMessageAsync(string text, string title, MessageBoxButtons buttons=MessageBoxButtons.OK, string? confirmLabel=null, MessageBoxDefaultButton defaultButton=MessageBoxDefaultButton.Button1)
    {
        if (!reactReady) return MessageBox.Show(this,text,title,buttons,MessageBoxIcon.None,defaultButton);
        var id=Guid.NewGuid().ToString("N");
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        reactPrompts.Add(id,completion);ReactEvent("prompt",new {id,title,text,confirm=buttons==MessageBoxButtons.YesNo,confirmLabel});
        using var registration=lifetime.Token.Register(()=>completion.TrySetResult(false));
        try {return await completion.Task?buttons==MessageBoxButtons.YesNo?DialogResult.Yes:DialogResult.OK:DialogResult.No;}
        finally {reactPrompts.Remove(id);}
    }
}
