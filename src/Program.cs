using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Windows.Forms;

namespace AmdNrAssistant;

internal static class Program
{
    [STAThread]
    static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new MainForm()); }
}

public sealed class GameCandidate
{
    public required string Title { get; init; }
    public required string ExePath { get; init; }
    public Icon? Icon { get; init; }
    public string InstallDirectory { get; init; } = "";
    public string? SteamAppId { get; init; }
    public string? CoverPath { get; set; }
}

public static class GameScanner
{
    static readonly string[] ignored = ["unins", "setup", "crash", "launcher", "redist", "benchmark", "easyanticheat"];
    public static Task<List<GameCandidate>> ScanAsync(CancellationToken token) => Task.Run(() => Scan(token), token);
    static List<GameCandidate> Scan(CancellationToken token)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady && (x.DriveType is DriveType.Fixed or DriveType.Removable)))
        {
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam", "steamapps", "common"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files", "Steam", "steamapps", "common"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "Games"));
        }
        var found = new Dictionary<string, GameCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            token.ThrowIfCancellationRequested();
            var steamIds = ReadSteamInstallIds(Directory.GetParent(root)?.FullName);
            IEnumerable<string> dirs; try { dirs = Directory.EnumerateDirectories(root); } catch { continue; }
            foreach (var dir in dirs)
            {
                token.ThrowIfCancellationRequested(); var exe = FindGameExe(dir); if (exe is null) continue;
                var title = Path.GetFileName(dir);
                steamIds.TryGetValue(title, out var appId);
                try { found[exe] = new GameCandidate { Title = title, ExePath = exe, InstallDirectory = dir, SteamAppId = appId, Icon = Icon.ExtractAssociatedIcon(exe) }; }
                catch { found[exe] = new GameCandidate { Title = title, ExePath = exe, InstallDirectory = dir, SteamAppId = appId }; }
            }
        }
        return found.Values.OrderBy(x => x.Title).ToList();
    }

    static Dictionary<string, string> ReadSteamInstallIds(string? steamApps)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(steamApps) || !Directory.Exists(steamApps)) return result;
        try
        {
            foreach (var file in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                var text = File.ReadAllText(file);
                var id = Regex.Match(text, @"""appid""\s+""(\d+)""").Groups[1].Value;
                var dir = Regex.Match(text, @"""installdir""\s+""([^""]+)""").Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(dir)) result[dir] = id;
            }
        }
        catch { }
        return result;
    }
    public static string? FindGameExe(string dir)
    {
        try
        {
            var files = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(x => !ignored.Any(word => Path.GetFileNameWithoutExtension(x).Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Select(x => new FileInfo(x)).Where(x => x.Length > 256 * 1024).OrderByDescending(x => x.Length).Take(8).ToList();
            return files.FirstOrDefault(x => !x.Name.Contains("launcher", StringComparison.OrdinalIgnoreCase))?.FullName ?? files.FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }
}

public sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 14;
    public Color BorderColor { get; set; } = default;
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 2 || Height < 2) return;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int r = Math.Max(1, Math.Min(Radius * 2, Math.Min(Width - 1, Height - 1)));
        path.AddArc(0, 0, r, r, 180, 90); path.AddArc(Width-r-1, 0, r, r, 270, 90);
        path.AddArc(Width-r-1, Height-r-1, r, r, 0, 90); path.AddArc(0, Height-r-1, r, r, 90, 90); path.CloseFigure();
        var old = Region; Region = new Region(path); old?.Dispose();
    }
    public RoundedPanel() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var r = Math.Max(1, Math.Min(Radius * 2, Math.Min(Width - 1, Height - 1))); path.AddArc(0, 0, r, r, 180, 90); path.AddArc(Width - r - 1, 0, r, r, 270, 90); path.AddArc(Width - r - 1, Height - r - 1, r, r, 0, 90); path.AddArc(0, Height - r - 1, r, r, 90, 90); path.CloseFigure();
        e.Graphics.Clear(Parent?.BackColor ?? BackColor); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using var brush = new SolidBrush(BackColor); e.Graphics.FillPath(brush, path);
        using var pen = new Pen(BorderColor == default ? Color.FromArgb(220, 226, 232) : BorderColor, BorderColor == default ? 1 : 2); e.Graphics.DrawPath(pen, path);

    }
}

public sealed class RoundedButton : Button
{
    public string Glyph { get; set; } = "";
    public bool Active { get; set; }
    public int Radius { get; set; } = 10;
    public RoundedButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; FlatAppearance.MouseOverBackColor = Color.Transparent; FlatAppearance.MouseDownBackColor = Color.Transparent; UseVisualStyleBackColor = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath(); var r = Math.Max(1, Math.Min(Radius * 2, Math.Min(Width - 1, Height - 1)));
        path.AddArc(0, 0, r, r, 180, 90); path.AddArc(Width - r - 1, 0, r, r, 270, 90); path.AddArc(Width - r - 1, Height - r - 1, r, r, 0, 90); path.AddArc(0, Height - r - 1, r, r, 90, 90); path.CloseFigure();
        e.Graphics.Clear(Parent?.BackColor ?? BackColor); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using var brush = new SolidBrush(Enabled ? BackColor : Color.FromArgb(226, 231, 236)); e.Graphics.FillPath(brush, path);
        var bounds = ClientRectangle;
        if (Glyph.Length > 0)
        {
            int inset = (int)(16 * DeviceDpi / 96f); int iconWidth = (int)(28 * DeviceDpi / 96f);
            using var iconFont = new Font("Segoe MDL2 Assets", 14);
            TextRenderer.DrawText(e.Graphics, Glyph, iconFont, new Rectangle(inset, 0, iconWidth, Height), ForeColor, TextFormatFlags.VerticalCenter);
            bounds = new Rectangle(inset + iconWidth + 8, 0, Math.Max(1, Width-inset-iconWidth-32), Height);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : Color.FromArgb(155, 163, 172), (Glyph.Length == 0 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Active) { using var dot = new SolidBrush(Color.FromArgb(101, 185, 12)); e.Graphics.FillEllipse(dot, Width-20, Height/2-4, 8, 8); }

    }
}

public sealed class CoverPictureBox : PictureBox
{
    public CoverPictureBox() { SizeMode = PictureBoxSizeMode.Normal; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (Image == null) return;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        var scale = Math.Min((float)Width / Image.Width, (float)Height / Image.Height);
        var w = Image.Width * scale; var h = Image.Height * scale;
        var dest = new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        e.Graphics.DrawImage(Image, dest);
    }
}

public sealed partial class MainForm : Form
{
    readonly FlowLayoutPanel games = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(20), BackColor = Color.FromArgb(245, 247, 250) };
    readonly Label selected = new() { AutoSize = true, ForeColor = Color.FromArgb(70, 78, 92), Text = "尚未选择游戏" };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(80, 88, 102), Text = "正在扫描游戏…" };
    readonly ProgressBar progress = new() { Width = 180, Height = 8, Style = ProgressBarStyle.Marquee, Visible = false };
    readonly Button install = new RoundedButton { Text = "一键配置", AutoSize = true, Enabled = false };
    readonly Button restore = new RoundedButton { Text = "恢复配置", AutoSize = true, Enabled = false };
    readonly Button scan = new RoundedButton { Text = "重新扫描", AutoSize = true };
    readonly CancellationTokenSource lifetime = new();
    readonly string workRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "transactions");
    string? selectedGame, transactionState;
    RoundedPanel? selectedCard;
    Panel? aboutPanel;
    Panel? homePanel;
    Label? homeSummary;
    bool english;

    async Task ScanGamesAsync()
    {
        scan.Enabled = false; progress.Visible = true; games.Controls.Clear(); status.Text = "正在扫描 Steam 游戏目录…";
        try
        {
            var list = await GameScanner.ScanAsync(lifetime.Token);
            libraryGames = list;
            foreach (var game in list) games.Controls.Add(CreateGameCard(game));
            status.Text = list.Count == 0 ? "没有找到游戏。可以使用“添加游戏”选择 EXE。" : $"找到 {list.Count} 个游戏，正在加载封面…";
            await LoadCoversAsync(list);
            RefreshHome();
            status.Text = list.Count == 0 ? "没有找到游戏。可以使用“添加游戏”选择 EXE。" : $"找到 {list.Count} 个游戏，点击卡片选择。";
        }
        catch (OperationCanceledException) { }
        catch (Exception x) { status.Text = "扫描失败：" + x.Message; }
        finally { scan.Enabled = true; progress.Visible = false; }
    }
    async Task LoadCoversAsync(IReadOnlyList<GameCandidate> list)
    {
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "covers");
        Directory.CreateDirectory(cache);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        foreach (var game in list.Where(x => !string.IsNullOrWhiteSpace(x.SteamAppId)))
        {
            try
            {
                var local = Path.Combine(cache, game.SteamAppId + ".jpg");
                if (!File.Exists(local))
                {
                    byte[] data;
                    try { data = await client.GetByteArrayAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{game.SteamAppId}/library_600x900_2x.jpg", lifetime.Token); }
                    catch { data = await client.GetByteArrayAsync($"https://steamcdn-a.akamaihd.net/steam/apps/{game.SteamAppId}/library_600x900_2x.jpg", lifetime.Token); }
                    if (data.Length < 10_000) continue;
                    await File.WriteAllBytesAsync(local, data, lifetime.Token);
                }
                game.CoverPath = local;
                var card = games.Controls.Cast<Control>().FirstOrDefault(c => ReferenceEquals(c.Tag, game));
                if (card?.Controls.OfType<PictureBox>().FirstOrDefault() is PictureBox picture)
                {
                    using var stream = new MemoryStream(await File.ReadAllBytesAsync(local, lifetime.Token));
                    using var source = Image.FromStream(stream);
                    picture.Image = new Bitmap(source);
                    picture.SizeMode = PictureBoxSizeMode.Zoom;
                }
            }
            catch { /* 网络不可用时保留 EXE 图标 */ }
        }
    }
    Control CreateGameCard(GameCandidate game)
    {
        var card = new RoundedPanel { Width = 230, Height = 411, Margin = new Padding(10), Padding = new Padding(1), BackColor = Color.White, Cursor = Cursors.Hand, Tag = game, Radius = 14 };
        var icon = new CoverPictureBox { Width = 226, Height = 339, Location = new Point(2, 2), BackColor = Color.FromArgb(225, 231, 236), Image = game.CoverPath != null && File.Exists(game.CoverPath) ? Image.FromFile(game.CoverPath) : game.Icon?.ToBitmap() ?? SystemIcons.Application.ToBitmap() };
        var title = new Label { Text = game.Title, Location = new Point(14, 352), Width = 208, Height = 24, Font = new Font(Font, FontStyle.Bold), AutoEllipsis = true };
        var path = new Label { Text = english ? "Compatibility unverified" : "可配置 DLSS 5 · 待验证", Location = new Point(14, 381), Width = 202, Height = 24, ForeColor = Color.FromArgb(78, 158, 0), AutoEllipsis = true };
        void Pick(object? _, EventArgs __) { if (selectedCard != null) { selectedCard.BorderColor = default; selectedCard.Invalidate(); } selectedCard = card; card.BorderColor = Color.FromArgb(92, 178, 0); card.Invalidate(); selectedGame = game.ExePath; SwitchPage(1); selected.Text = "已选择：" + game.Title; status.Text = "现在可以点击“一键配置”。"; install.Enabled = transactionState == null; }
        foreach (Control child in new Control[] { card, icon, title, path }) child.Click += Pick;
        card.Controls.Add(icon); card.Controls.Add(title); card.Controls.Add(path); card.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f)); return card;
    }
    void SelectGameManually(object? sender, EventArgs e)
    {
        using var d = new OpenFileDialog { Filter = "游戏程序 (*.exe)|*.exe", Title = "选择游戏本体 EXE" }; if (d.ShowDialog() != DialogResult.OK) return;
        try { Core.ValidateGame(d.FileName); selectedGame = d.FileName; selected.Text = "已选择：" + Path.GetFileNameWithoutExtension(d.FileName); status.Text = "现在可以点击“一键配置”。"; install.Enabled = transactionState == null; } catch (Exception x) { status.Text = x.Message; }
    }
    async Task InstallAsync()
    {
        if (selectedGame == null) return; install.Enabled = false; restore.Enabled = false; string? state = null;
        try
        {
            var release = await CheckReleaseAsync() ?? throw new IOException("无法核对官方版本。"); var gameDir = Path.GetDirectoryName(Path.GetFullPath(selectedGame))!;
            state = Path.Combine(workRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(state); var installer = Path.Combine(state, Core.InstallerName); var bundledDll = Path.Combine(state, Core.DllName);
            status.Text = "正在释放并校验内置 DLSS 5 组件…"; Core.ExtractBundledDll(bundledDll); using var client = new HttpClient(); status.Text = $"正在下载官方安装器 {release.Tag}…"; await Core.DownloadAsync(client, release, installer, new Progress<int>(_ => { }), lifetime.Token);
            status.Text = "正在准备游戏目录…"; Core.Stage(gameDir, installer, bundledDll, state, release.Tag); transactionState = state;
            var configuredFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "configured-games.txt"); Directory.CreateDirectory(Path.GetDirectoryName(configuredFile)!);
            var configured = File.Exists(configuredFile) ? File.ReadAllLines(configuredFile).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase); configured.Remove(selectedGame); configured.Add(selectedGame); File.WriteAllLines(configuredFile, configured);
            RefreshHome(); Core.MarkPhase(state, "installer-launched"); Process.Start(new ProcessStartInfo(installer) { WorkingDirectory = gameDir, UseShellExecute = true }); status.Text = "官方安装器已启动，请按其提示完成安装。"; MessageBox.Show("官方安装器已打开。完成后启动游戏、启用 FSR，按 End 打开菜单。", "配置已准备", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { status.Text = "操作已取消。"; }
        catch (Exception x) { status.Text = x.Message; if (state != null && File.Exists(Path.Combine(state, "journal.json"))) { try { Core.Rollback(state); } catch { } } }
        finally { UpdateButtons(); }
    }
    async Task<ReleaseInfo?> CheckReleaseAsync() { using var c = new HttpClient(); return await Core.GetReleaseAsync(c, lifetime.Token); }
    async Task RestoreAsync() { if (transactionState == null) return; try { Core.Rollback(transactionState); transactionState = null; status.Text = english ? "Configuration restored." : "配置已恢复。"; } catch (Exception x) { status.Text = x.Message; } finally { UpdateButtons(); await Task.CompletedTask; } }
    void UpdateButtons() { install.Enabled = selectedGame != null && transactionState == null; restore.Enabled = transactionState != null; }
}
