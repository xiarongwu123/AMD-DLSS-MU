using System.Drawing;
using System.Windows.Forms;
using Mu.Compatibility;

namespace AmdNrAssistant;

public sealed partial class MainForm
{
    readonly Panel compatibilityPage = new() { Dock = DockStyle.Fill, Padding = new Padding(28) };
    readonly TextBox compatibilitySearch = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索游戏名称 / Steam App ID", MaxLength = 120 };
    readonly ComboBox compatibilityGpuFilter = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly FlowLayoutPanel compatibilityGames = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    readonly FlowLayoutPanel compatibilityDetails = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    readonly Label compatibilityFeedback = new() { Dock = DockStyle.Fill, ForeColor = Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label compatibilityHardware = new() { Dock = DockStyle.Fill, ForeColor = Muted, AutoEllipsis = true };
    readonly CompatibilityEnvironmentReader compatibilityReader = new();
    IReadOnlyList<CompatibilityGpu> compatibilityGpus = Array.Empty<CompatibilityGpu>();
    CompatibilityGame? compatibilitySelected;
    RoundedButton compatibilitySubmitButton = null!;
    bool compatibilityInitialized, compatibilitySettingGpu, compatibilityReportOpen;
    int compatibilityRequest;

    sealed record GpuChoice(string Label, CompatibilityGpu? Gpu)
    {
        public override string ToString() => Label;
    }

    void BuildCompatibilityPage()
    {
        compatibilityPage.BackColor = Base;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        foreach (var height in new[] { 52, 36, 48, 34, 40 }) body.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(PageTitle("游戏兼容性", "Game compatibility"), 0, 0);
        body.Controls.Add(new Label { Text = "搜索你的游戏，查看其他玩家的测试环境与结果。", Dock = DockStyle.Fill, ForeColor = Muted }, 0, 1);
        var search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        compatibilitySearch.Margin = new Padding(0, 6, 12, 0);
        compatibilitySearch.AccessibleName = "搜索兼容性游戏";
        compatibilityGpuFilter.Margin = new Padding(0, 6, 12, 0);
        compatibilityGpuFilter.AccessibleName = "按显卡型号筛选实测";
        compatibilityGpuFilter.Items.Add(new GpuChoice("全部显卡", null)); compatibilityGpuFilter.SelectedIndex = 0;
        compatibilitySearch.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchCompatibilityAsync(); } };
        compatibilityGpuFilter.SelectedIndexChanged += async (_, _) => { if (!compatibilitySettingGpu && accountClient.IsOnline) await SearchCompatibilityAsync(); };
        var find = Action("搜索", "Search", async (_, _) => await SearchCompatibilityAsync()); find.Dock = DockStyle.Fill;
        var report = compatibilitySubmitButton = Action("提交测试结果", "Submit test", async (_, _) => await ShowCompatibilityReportAsync(compatibilitySelected));
        report.Enabled = false;
        report.Dock = DockStyle.Fill; report.BackColor = Acid; report.ForeColor = OnAccent;
        search.Controls.Add(compatibilitySearch, 0, 0); search.Controls.Add(compatibilityGpuFilter, 1, 0);
        search.Controls.Add(find, 2, 0); search.Controls.Add(report, 3, 0); body.Controls.Add(search, 0, 2);
        compatibilityHardware.Text = "登录后自动读取本机 GPU 与驱动；多显卡设备可切换筛选。";
        body.Controls.Add(compatibilityHardware, 0, 3); body.Controls.Add(compatibilityFeedback, 0, 4);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        compatibilityGames.Margin = new Padding(0, 0, 18, 0); compatibilityDetails.Padding = new Padding(12, 0, 4, 0);
        columns.Controls.Add(compatibilityGames, 0, 0); columns.Controls.Add(compatibilityDetails, 1, 0); body.Controls.Add(columns, 0, 5);
        compatibilityPage.Controls.Add(body); pageHost.Controls.Add(compatibilityPage);
        compatibilityGames.ClientSizeChanged += (_, _) => FitCompatibilityRows(compatibilityGames);
        compatibilityDetails.ClientSizeChanged += (_, _) => FitCompatibilityRows(compatibilityDetails);
        CompatibilityText(compatibilityDetails, "选择游戏，查看实测记录。没有记录的组合会显示“待验证”。");
    }

    static void ClearCompatibilityRows(FlowLayoutPanel panel)
    {
        while (panel.Controls.Count > 0) { var child = panel.Controls[0]; panel.Controls.Remove(child); child.Dispose(); }
    }

    static void FitCompatibilityRows(FlowLayoutPanel panel)
    {
        var width = Math.Max(180, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
        foreach (Control child in panel.Controls)
        {
            if (child is Label) child.MaximumSize = new Size(width, 0);
            child.Width = width;
        }
    }

    Label CompatibilityText(FlowLayoutPanel panel, string text, bool heading = false, Color? color = null)
    {
        var label = new Label { Text = text, AutoSize = true, ForeColor = color ?? (heading ? Ink : Muted),
            Font = new Font(Font.FontFamily, heading ? 16 : 10, heading ? FontStyle.Bold : FontStyle.Regular),
            Margin = new Padding(0, 4, 0, heading ? 14 : 18), UseMnemonic = false };
        panel.Controls.Add(label); FitCompatibilityRows(panel); return label;
    }

    static string CompatibilityState(string state) => state switch
    {
        "success" => "成功实测", "partial" => "部分成功", "failure" => "失败实测", "mixed" => "结果不一致", _ => "待验证"
    };
    static Color CompatibilityColor(string state) => state switch
    {
        "success" => Color.FromArgb(72, 174, 114), "partial" or "mixed" => Color.FromArgb(200, 149, 47),
        "failure" => Color.FromArgb(220, 91, 91), _ => Muted
    };
    static string CompatibilityNumbers(CompatibilityCounts c) => $"{c.Total} 次实测  ·  成功 {c.Success}  /  部分成功 {c.Partial}  /  失败 {c.Failure}";
    static string Known(string? value) => string.IsNullOrWhiteSpace(value) || value == "unknown" ? "未知" : value;
    string? CompatibilityGpuName => (compatibilityGpuFilter.SelectedItem as GpuChoice)?.Gpu?.Name;

    async Task InitializeCompatibilityAsync()
    {
        if (IsDisposed) return;
        if (compatibilityInitialized) { await SearchCompatibilityAsync(); return; }
        compatibilityInitialized = true;
        compatibilityHardware.Text = "正在读取本机 GPU 与驱动…";
        var hardware = compatibilityReader.ReadGpusAsync(lifetime.Token);
        await SearchCompatibilityAsync();
        try
        {
            compatibilityGpus = await hardware;
            if (IsDisposed || lifetime.IsCancellationRequested) return;
            compatibilitySettingGpu = true;
            foreach (var gpu in compatibilityGpus) compatibilityGpuFilter.Items.Add(new GpuChoice(gpu.Name, gpu));
            compatibilityHardware.Text = compatibilityGpus.Count == 0 || compatibilityGpus.All(g => g.Name == "unknown")
                ? "GPU 自动读取未完成；可浏览全部实测，提交时缺失字段会标为未知。"
                : "本机：" + string.Join("；", compatibilityGpus.Select(g => $"{g.Name} · 驱动 {Known(g.DriverVersion)}"));
            compatibilitySettingGpu = false;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!IsDisposed) compatibilityHardware.Text = "GPU 自动读取失败；缺失字段将标为未知。"; }
        finally { if (!IsDisposed) compatibilitySubmitButton.Enabled = true; }
    }

    void ClearCompatibilityAccess()
    {
        ++compatibilityRequest;
        compatibilitySelected = null;
        ClearCompatibilityRows(compatibilityGames); ClearCompatibilityRows(compatibilityDetails);
        compatibilityFeedback.Text = "登录并获得兼容查询权限后查看实测。";
    }

    async Task SearchCompatibilityAsync()
    {
        if (!accountClient.IsOnline || IsDisposed) return;
        var request = ++compatibilityRequest;
        var gpu = CompatibilityGpuName;
        compatibilitySelected = null;
        ClearCompatibilityRows(compatibilityGames); ClearCompatibilityRows(compatibilityDetails);
        compatibilityFeedback.Text = "正在查询玩家实测…";
        try
        {
            var response = await accountClient.SearchCompatibilityAsync(compatibilitySearch.Text.Trim(), gpu, lifetime.Token);
            if (IsDisposed || request != compatibilityRequest) return;
            compatibilityFeedback.Text = $"找到 {response.Items.Length} 款游戏（最多显示 50 款） · {(gpu == null ? "全部显卡" : gpu)} · 结果由玩家上报";
            foreach (var item in response.Items)
            {
                var button = new RoundedButton { Text = $"{item.Game.Name}\n{CompatibilityState(item.Status)} · {item.Counts.Total} 次实测",
                    Height = 86, BackColor = Surface, ForeColor = CompatibilityColor(item.Status), Radius = 12,
                    Margin = new Padding(0, 0, 0, 12), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 8, 0),
                    AccessibleName = item.Game.Name + " " + CompatibilityState(item.Status) };
                button.Click += async (_, _) => await LoadCompatibilityGameAsync(item.Game.Id);
                compatibilityGames.Controls.Add(button);
            }
            FitCompatibilityRows(compatibilityGames);
            CompatibilityText(compatibilityDetails, response.Items.Length == 0
                ? "尚无匹配的游戏。你可以选择本地游戏，提交第一条真实测试结果。"
                : "选择左侧游戏，查看显卡分布和最近测试环境。\n成功实测只代表所记录环境中的玩家反馈。");
        }
        catch (Exception e) { if (!IsDisposed && request == compatibilityRequest) compatibilityFeedback.Text = CompatibilityError(e); }
    }

    static string CompatibilityError(Exception error) => error is AccountApiException { Status: System.Net.HttpStatusCode.NotFound }
        ? "当前服务尚未提供兼容数据库，请更新服务端后重试。" : AccountError(error);

    async Task LoadCompatibilityGameAsync(string id)
    {
        var request = ++compatibilityRequest;
        var gpu = CompatibilityGpuName;
        compatibilityFeedback.Text = "正在读取测试环境…";
        try
        {
            var detail = await accountClient.GetCompatibilityAsync(id, gpu, lifetime.Token);
            if (IsDisposed || request != compatibilityRequest) return;
            compatibilitySelected = detail.Game;
            ClearCompatibilityRows(compatibilityDetails);
            CompatibilityText(compatibilityDetails, detail.Game.Name, true);
            CompatibilityText(compatibilityDetails, $"{CompatibilityState(detail.Status)} · {(gpu == null ? "全部显卡" : gpu)}\n{CompatibilityNumbers(detail.Counts)}", color: CompatibilityColor(detail.Status));
            CompatibilityText(compatibilityDetails, detail.LastTestedAt is { } last
                ? $"最近提交：{last.ToLocalTime():yyyy-MM-dd HH:mm} · 玩家上报，不代表所有驱动和游戏版本均兼容。"
                : "暂无实测记录，兼容性待验证。游戏目录收录不代表支持 DLSS5。");
            if (detail.Game.SteamAppId != null) CompatibilityText(compatibilityDetails, "Steam App ID：" + detail.Game.SteamAppId);
            if (detail.Gpus.Length > 0)
                CompatibilityText(compatibilityDetails, "显卡实测分布（全部显卡）\n" + string.Join("\n", detail.Gpus.Select(g => $"{g.GpuName}：{CompatibilityState(g.Status)} · {CompatibilityNumbers(g.Counts)}")));
            CompatibilityText(compatibilityDetails, "最近实测", true);
            foreach (var test in detail.Tests)
            {
                var env = test.Environment;
                CompatibilityText(compatibilityDetails,
                    $"{CompatibilityState(test.Result)} · {test.Tester} · 提交于 {test.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
                    $"{Known(env.Gpu.Name)} · 驱动 {Known(env.Gpu.DriverVersion)}\n" +
                    $"游戏 {Known(env.GameVersion)} · {Known(env.RenderApi)} · MU {Known(env.ToolVersion)}\n" +
                    $"DLSS {Known(env.DlssVersion)} · DLSS5 {Known(env.Dlss5Version)}\n" +
                    $"系统 {Known(env.OsVersion)} · 系统 DirectX {Known(env.SystemDirectX)}\n" +
                    $"游戏设置：{Known(env.Settings)}\n其他 Mod：{Known(env.OtherMods)}" +
                    (test.FailureReason == null ? "" : "\n问题：" + CompatibilityFailureLabel(test.FailureReason)) +
                    (string.IsNullOrWhiteSpace(test.Notes) ? "" : "\n说明：" + test.Notes), color: CompatibilityColor(test.Result));
            }
            if (detail.Tests.Length == 0) CompatibilityText(compatibilityDetails, "当前显卡筛选下暂无记录，欢迎完成游戏实测后提交。");
            compatibilityFeedback.Text = $"{detail.Game.Name} · 最近 {detail.Tests.Length} 条记录（最多 50 条）";
        }
        catch (Exception e) { if (!IsDisposed && request == compatibilityRequest) compatibilityFeedback.Text = CompatibilityError(e); }
    }

    static readonly (string Code, string Label)[] CompatibilityFailures =
    [
        ("unknown", "未知 / 其他问题"), ("startup_crash", "启动或进入游戏崩溃"), ("load_failed", "DLL 无法加载"),
        ("menu_missing", "DLSS 菜单未出现 / 功能未生效"), ("game_update", "游戏更新后失效"),
        ("anti_cheat", "反作弊拦截"), ("visual_artifacts", "UI 异常 / 画面闪烁"), ("performance", "性能下降")
    ];
    static string CompatibilityFailureLabel(string code) => CompatibilityFailures.FirstOrDefault(x => x.Code == code).Label ?? code;

    async Task ShowCompatibilityReportAsync(CompatibilityGame? catalogGame)
    {
        if (compatibilityReportOpen) return;
        compatibilityReportOpen = true;
        try { await EditCompatibilityReportAsync(catalogGame); }
        finally { compatibilityReportOpen = false; }
    }

    async Task EditCompatibilityReportAsync(CompatibilityGame? catalogGame)
    {
        if (!await RequireFeatureAsync("compatibility.submit")) return;
        using var dialog = new Form { Text = "提交游戏实测", Size = new Size(790, 820), MinimumSize = new Size(650, 620),
            StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.Dpi, Font = Font, MinimizeBox = false, MaximizeBox = false };
        ThemeWindow(dialog);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20) };
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 0, 12, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(fields); body.Controls.Add(scroll, 0, 0);
        void Field(string title, Control control, int height = 43)
        {
            var row = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            fields.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 6, 0, 5); control.AccessibleName = title;
            fields.Controls.Add(control, 1, row);
        }
        TextBox Input(string value, int max = 160) => new() { Text = value, MaxLength = max, BackColor = Base, ForeColor = Ink };
        var name = Input(catalogGame?.Name ?? ""); name.ReadOnly = catalogGame != null;
        var steam = Input(catalogGame?.SteamAppId ?? "", 16); steam.ReadOnly = catalogGame != null;
        Field("游戏名称", name); Field("Steam App ID", steam);
        var local = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(GameCandidate.Title) };
        foreach (var game in libraryGames.Where(g => catalogGame == null ||
            (catalogGame.SteamAppId != null ? g.SteamAppId == catalogGame.SteamAppId : string.Equals(g.Title, catalogGame.Name, StringComparison.OrdinalIgnoreCase)))) local.Items.Add(game);
        Field("本地游戏", local);
        var choose = new RoundedButton { Text = "选择游戏 EXE…", BackColor = Surface, ForeColor = Ink, Radius = 8 };
        Field("手动定位", choose);
        var gpuPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var gpu in compatibilityGpus) gpuPicker.Items.Add(new GpuChoice(gpu.Name, gpu));
        if (gpuPicker.Items.Count == 0) gpuPicker.Items.Add(new GpuChoice("未识别 GPU（以未知上报）", new("unknown", "unknown", null, null, "unknown")));
        gpuPicker.SelectedIndex = compatibilityGpus.Count <= 1 ? 0 : compatibilityGpus.ToList().FindIndex(g => g.Name == CompatibilityGpuName);
        Field("测试所用显卡", gpuPicker);
        var gpuName = Input("", 160); gpuName.ReadOnly = true; Field("GPU 型号", gpuName);
        var driver = Input("", 100); driver.ReadOnly = true; Field("Windows 驱动", driver);
        var detected = new Label { Text = "选择本地游戏后自动读取环境。", AutoEllipsis = true, ForeColor = Muted };
        Field("自动读取", detected, 98);
        var version = Input("unknown", 100); Field("游戏版本", version);
        var api = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        api.Items.AddRange(new object[] { "未知（未确认）", "DX11", "DX12", "Vulkan" }); api.SelectedIndex = 0;
        Field("游戏渲染 API", api);
        var settings = Input("", 1000); settings.PlaceholderText = "选填：分辨率、画质、帧生成等"; Field("游戏设置", settings);
        var mods = Input("", 1000); mods.PlaceholderText = "选填：其他 Mod 及版本；不确定可留空"; Field("其他 Mod", mods);
        var outcome = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        outcome.Items.AddRange(new object[] { "成功：功能、画面和运行正常", "部分成功：能开启，但存在问题", "失败：无法运行或功能未生效" });
        Field("DLSS5 实测", outcome);
        var reason = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
        reason.Items.AddRange(CompatibilityFailures.Select(x => (object)x.Label).ToArray()); reason.SelectedIndex = 0; Field("问题原因", reason);
        outcome.SelectedIndexChanged += (_, _) => reason.Enabled = outcome.SelectedIndex > 0;
        var notes = Input("", 1000); notes.Multiline = true; notes.PlaceholderText = "选填：问题现象或复现方式"; Field("补充说明", notes, 70);
        Field("公开范围", new Label { Text = "点击提交将公开上述环境与实测结果，以匿名玩家编号展示。请勿填写个人信息。仅在你提交时上传，不上传本机路径。", ForeColor = Muted }, 80);
        var feedback = new Label { Dock = DockStyle.Fill, ForeColor = Muted, Text = "请在完成游戏实测后提交。" };
        body.Controls.Add(feedback, 0, 1);
        var submit = new RoundedButton { Text = "提交测试", Dock = DockStyle.Right, Width = 180, Radius = 12, BackColor = Acid, ForeColor = OnAccent, Enabled = false };
        body.Controls.Add(submit, 0, 2); dialog.Controls.Add(body);
        using var reportLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CompatibilityEnvironment? environment = null;
        CompatibilitySubmission? pending = null;
        CompatibilityTest? saved = null;
        bool reading = false, submitting = false;
        int readVersion = 0;
        async Task ReadEnvironment()
        {
            if (local.SelectedItem is not GameCandidate game) return;
            if (gpuPicker.SelectedItem is not GpuChoice { Gpu: { } gpu }) { detected.Text = "检测到多张显卡，请选择实际测试所用的显卡。"; return; }
            var generation = ++readVersion; reading = true; submit.Enabled = false;
            detected.Text = "正在读取驱动、系统和游戏版本…";
            if (catalogGame == null) { name.Text = game.Title; steam.Text = game.SteamAppId ?? ""; }
            try
            {
                var snapshot = await compatibilityReader.ReadAsync(game.ExePath, game.InstallDirectory, gpu, reportLifetime.Token);
                if (dialog.IsDisposed || generation != readVersion) return;
                environment = snapshot; version.Text = snapshot.GameVersion;
                gpuName.Text = gpu.Name; gpuName.ReadOnly = gpu.Name != "unknown";
                driver.Text = gpu.DriverVersion; driver.ReadOnly = gpu.DriverVersion != "unknown";
                detected.Text = $"{gpu.Name} · 驱动 {Known(gpu.DriverVersion)}\n{Known(snapshot.OsVersion)} · 系统 DX {Known(snapshot.SystemDirectX)}\nDLSS {Known(snapshot.DlssVersion)} · DLSS5 {Known(snapshot.Dlss5Version)} · MU {Known(snapshot.ToolVersion)}";
                if (compatibilityReader.Warning != null) feedback.Text = compatibilityReader.Warning;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!dialog.IsDisposed && generation == readVersion) { environment = null; feedback.Text = "读取失败：" + e.Message; } }
            finally { if (!dialog.IsDisposed && generation == readVersion) { reading = false; submit.Enabled = environment != null; } }
        }
        local.SelectedIndexChanged += async (_, _) => await ReadEnvironment();
        gpuPicker.SelectedIndexChanged += async (_, _) => await ReadEnvironment();
        choose.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "游戏程序 (*.exe)|*.exe", Title = "选择实际游戏程序" };
            if (picker.ShowDialog(dialog) != DialogResult.OK) return;
            var game = new GameCandidate { Title = catalogGame?.Name ?? Path.GetFileNameWithoutExtension(picker.FileName), ExePath = picker.FileName,
                InstallDirectory = Path.GetDirectoryName(picker.FileName)!, SteamAppId = catalogGame?.SteamAppId };
            local.Items.Add(game); local.SelectedItem = game;
        };
        submit.Click += async (_, _) =>
        {
            if (submitting || reading || environment == null) return;
            if (pending == null)
            {
                if (string.IsNullOrWhiteSpace(name.Text) || outcome.SelectedIndex < 0) { feedback.Text = "请确认游戏名称，并选择实测结果。"; return; }
                pending = new CompatibilitySubmission(Guid.NewGuid().ToString("D"), catalogGame?.Id, name.Text.Trim(),
                    string.IsNullOrWhiteSpace(steam.Text) ? null : steam.Text.Trim(),
                    environment with { Gpu = environment.Gpu with { Name = string.IsNullOrWhiteSpace(gpuName.Text) ? "unknown" : gpuName.Text.Trim(),
                            DriverVersion = string.IsNullOrWhiteSpace(driver.Text) ? "unknown" : driver.Text.Trim() },
                        GameVersion = string.IsNullOrWhiteSpace(version.Text) ? "unknown" : version.Text.Trim(),
                        RenderApi = new[] { "unknown", "dx11", "dx12", "vulkan" }[api.SelectedIndex],
                        Settings = string.IsNullOrWhiteSpace(settings.Text) ? "unknown" : settings.Text.Trim(),
                        OtherMods = string.IsNullOrWhiteSpace(mods.Text) ? "unknown" : mods.Text.Trim() },
                    new[] { "success", "partial", "failure" }[outcome.SelectedIndex],
                    outcome.SelectedIndex == 0 ? null : CompatibilityFailures[reason.SelectedIndex].Code,
                    string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim());
            }
            submitting = true; submit.Enabled = false; fields.Enabled = false; feedback.Text = "正在提交实测记录…";
            try
            {
                saved = await accountClient.SubmitCompatibilityAsync(pending, reportLifetime.Token);
                dialog.DialogResult = DialogResult.OK; dialog.Close();
            }
            catch (AccountApiException e)
            {
                feedback.Text = CompatibilityError(e);
                if ((int)e.Status < 500) { pending = null; fields.Enabled = true; submit.Text = "提交测试"; }
                else submit.Text = "重试同一记录";
            }
            catch (Exception e) { feedback.Text = "提交结果未确认，可重试同一记录：" + AccountError(e); submit.Text = "重试同一记录"; }
            finally { submitting = false; if (!dialog.IsDisposed) submit.Enabled = true; }
        };
        dialog.FormClosing += (_, e) => { if (submitting && saved == null) e.Cancel = true; };
        dialog.FormClosed += (_, _) => reportLifetime.Cancel();
        dialog.Shown += (_, _) => { if (local.Items.Count > 0) local.SelectedIndex = 0; };
        dialog.ShowDialog(this);
        if (saved != null && !IsDisposed)
        {
            if (activePage != 0) SwitchPage(0);
            await SearchCompatibilityAsync();
            await LoadCompatibilityGameAsync(saved.GameId);
            ShowProductToast("测试结果已提交，感谢提供真实实测。", true);
        }
    }
}
