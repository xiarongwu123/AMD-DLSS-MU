using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Windows.Forms;

namespace AmdNrAssistant;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 1 && args[0] == "--apply-update")
        {
            try { AutoUpdate.ApplyAsync().GetAwaiter().GetResult(); }
            catch (Exception e) { MessageBox.Show("更新未完成：" + e.Message + "\n旧版本备份保留在程序目录。", "AMD-DLSS-MU 更新"); }
            return;
        }
        var form = new MainForm();
        if (args.Length == 2 && args[0] == "--update-health")
            form.Shown += (_, _) => { try { AutoUpdate.ConfirmStartup(args[1]); } catch { /* Updater will roll back without a health acknowledgement. */ } };
        Application.Run(form);
    }
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
    static readonly string[] ignoredFiles =
    [
        "unins",
        "setup",
        "crash",
        "launcher",
        "redist",
        "benchmark",
        "easyanticheat",
        "battleye",
        "updater",
        "patcher",
        "configurator",
        "vcredist"
    ];

    static readonly HashSet<string> ignoredDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "redist",
            "redistributables",
            "__installer",
            "easyanticheat",
            "battleye",
            "crashreports",
            "crashes",
            "support",
            "tools",
            "_commonredist",
            "directx",
            "dotnet",
            "uninstall",
            "unins000"
        };

    public static Task<List<GameCandidate>> ScanAsync(
        CancellationToken token)
    {
        return Task.Run(() => Scan(token), token);
    }

    static List<GameCandidate> Scan(CancellationToken token)
    {
        var roots = DiscoverRoots();

        var found = new Dictionary<string, GameCandidate>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();

            if (!Directory.Exists(root))
            {
                continue;
            }

            var steamIds = ReadSteamInstallIds(
                Directory.GetParent(root)?.FullName);

            IEnumerable<string> directories;

            try
            {
                directories = Directory
                    .EnumerateDirectories(root)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                token.ThrowIfCancellationRequested();

                var exe = FindGameExe(directory);

                if (exe is null)
                {
                    continue;
                }

                var title = Path.GetFileName(directory);

                steamIds.TryGetValue(title, out var appId);

                try
                {
                    found[exe] = new GameCandidate
                    {
                        Title = title,
                        ExePath = exe,
                        InstallDirectory = directory,
                        SteamAppId = appId,
                        Icon = Icon.ExtractAssociatedIcon(exe)
                    };
                }
                catch
                {
                    found[exe] = new GameCandidate
                    {
                        Title = title,
                        ExePath = exe,
                        InstallDirectory = directory,
                        SteamAppId = appId
                    };
                }
            }
        }

        foreach (var entry in GameLibrary.DiscoverRegistered())
        {
            token.ThrowIfCancellationRequested();
            var exe = entry.Exe ?? FindGameExe(entry.Directory);
            if (exe != null && !found.ContainsKey(exe))
                found[exe] = new GameCandidate { Title = entry.Title, ExePath = exe, InstallDirectory = entry.Directory };
        }

        return found.Values
            .OrderBy(game => game.Title)
            .ToList();
    }

    static HashSet<string> DiscoverRoots()
    {
        var roots = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);

                if (Directory.Exists(fullPath))
                {
                    roots.Add(fullPath);
                }
            }
            catch
            {
                // 忽略无效路径。
            }
        }

        foreach (var drive in SafeDrives())
        {
            var root = drive.RootDirectory.FullName;

            foreach (var relative in new[]
                     {
                         @"SteamLibrary\steamapps\common",
                         @"Steam\steamapps\common",
                         @"Program Files (x86)\Steam\steamapps\common",
                         @"Program Files\Steam\steamapps\common",
                         @"Games",
                         @"Game",
                         @"Epic Games",
                         @"GOG Games",
                         @"XboxGames",
                         @"Riot Games",
                         @"Battle.net",
                         @"EA Games",
                         @"Origin Games",
                         @"Rockstar Games",
                         @"Program Files\Epic Games",
                         @"Program Files (x86)\Epic Games",
                         @"Program Files\GOG Galaxy\Games",
                         @"Program Files (x86)\GOG Galaxy\Games",
                         @"Ubisoft Game Launcher\games"
                         , @"Program Files\Ubisoft\Ubisoft Game Launcher\games"
                         , @"Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games"
                         , @"Program Files\EA Games"
                         , @"Program Files\Rockstar Games"
                     })
            {
                Add(Path.Combine(root, relative));
            }
        }

        foreach (var steamRoot in DiscoverSteamRoots())
        {
            Add(Path.Combine(
                steamRoot,
                "steamapps",
                "common"));

            var manifest = Path.Combine(
                steamRoot,
                "steamapps",
                "libraryfolders.vdf");

            foreach (var library in ReadSteamLibraryPaths(manifest))
            {
                Add(Path.Combine(
                    library,
                    "steamapps",
                    "common"));
            }
        }

        return roots;
    }

    /*
     * CS1626 修复：
     * 不再在带 catch 的 try 语句中使用 yield return。
     */
    static IEnumerable<DriveInfo> SafeDrives()
    {
        DriveInfo[] drives;

        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return Array.Empty<DriveInfo>();
        }

        var results = new List<DriveInfo>();

        foreach (var drive in drives)
        {
            try
            {
                if (drive.IsReady &&
                    drive.DriveType is
                        DriveType.Fixed or
                        DriveType.Removable)
                {
                    results.Add(drive);
                }
            }
            catch
            {
                // 跳过无权访问或不可用的磁盘。
            }
        }

        return results;
    }

    static IEnumerable<string> DiscoverSteamRoots()
    {
        var roots = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            try
            {
                var path = Path.GetFullPath(
                    value.Replace(
                        '/',
                        Path.DirectorySeparatorChar));

                if (Directory.Exists(path))
                {
                    roots.Add(path);
                }
            }
            catch
            {
                // 忽略无效路径。
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var view in new[]
                     {
                         RegistryView.Default,
                         RegistryView.Registry64,
                         RegistryView.Registry32
                     })
            {
                foreach (var hive in new[]
                         {
                             RegistryHive.CurrentUser,
                             RegistryHive.LocalMachine
                         })
                {
                    try
                    {
                        using var baseKey =
                            RegistryKey.OpenBaseKey(hive, view);

                        using var key = baseKey.OpenSubKey(
                            @"Software\Valve\Steam");

                        Add(key?.GetValue("SteamPath") as string);
                        Add(key?.GetValue("InstallPath") as string);
                    }
                    catch
                    {
                        // Steam 注册表项可能不存在。
                    }
                }
            }
        }

        foreach (var drive in SafeDrives())
        {
            var root = drive.RootDirectory.FullName;

            Add(Path.Combine(root, "Steam"));

            Add(Path.Combine(
                root,
                "Program Files (x86)",
                "Steam"));

            Add(Path.Combine(
                root,
                "Program Files",
                "Steam"));

            Add(Path.Combine(root, "SteamLibrary"));
        }

        return roots;
    }

    static IEnumerable<string> ReadSteamLibraryPaths(
        string manifest)
    {
        if (!File.Exists(manifest))
        {
            yield break;
        }

        string text;

        try
        {
            text = File.ReadAllText(manifest);
        }
        catch
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(
                     text,
                     @"""path""\s+""([^""]+)""",
                     RegexOptions.IgnoreCase))
        {
            var path = match.Groups[1]
                .Value
                .Replace(@"\\", @"\");

            if (Directory.Exists(path))
            {
                yield return path;
            }
        }
    }

    static Dictionary<string, string> ReadSteamInstallIds(
        string? steamApps)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(steamApps) ||
            !Directory.Exists(steamApps))
        {
            return result;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(
                         steamApps,
                         "appmanifest_*.acf",
                         SearchOption.TopDirectoryOnly))
            {
                var text = File.ReadAllText(file);

                var id = Regex.Match(
                        text,
                        @"""appid""\s+""(\d+)""")
                    .Groups[1]
                    .Value;

                var directory = Regex.Match(
                        text,
                        @"""installdir""\s+""([^""]+)""")
                    .Groups[1]
                    .Value;

                if (!string.IsNullOrWhiteSpace(id) &&
                    !string.IsNullOrWhiteSpace(directory))
                {
                    result[directory] = id;
                }
            }
        }
        catch
        {
            // 忽略损坏或无法读取的 Steam 清单。
        }

        return result;
    }

    public static string? FindGameExe(string directory)
    {
        var folderName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(directory));

        return EnumerateExeFiles(directory, 4)
            .Where(IsCandidate)
            .Select(path =>
            {
                try
                {
                    var information = new FileInfo(path);
                    var name = Path.GetFileNameWithoutExtension(path);

                    var score = Math.Min(
                        information.Length / (1024 * 1024),
                        1000);

                    if (string.Equals(
                            name,
                            folderName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        score += 5000;
                    }

                    if (name.Contains(
                            "shipping",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        score += 200;
                    }

                    if (name.Contains(
                            "win64",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        score += 100;
                    }

                    return (
                        path,
                        score,
                        size: information.Length);
                }
                catch
                {
                    return (
                        path,
                        score: long.MinValue,
                        size: 0L);
                }
            })
            .Where(candidate =>
                candidate.score != long.MinValue)
            .OrderByDescending(candidate =>
                candidate.score)
            .ThenByDescending(candidate =>
                candidate.size)
            .Select(candidate =>
                candidate.path)
            .FirstOrDefault();
    }

    static bool IsCandidate(string path)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(path);

            if (ignoredFiles.Any(word =>
                    name.Contains(
                        word,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return new FileInfo(path).Length >= 128 * 1024;
        }
        catch
        {
            return false;
        }
    }

    static IEnumerable<string> EnumerateExeFiles(
        string directory,
        int depth)
    {
        IEnumerable<string> files;

        try
        {
            files = Directory
                .EnumerateFiles(
                    directory,
                    "*.exe",
                    SearchOption.TopDirectoryOnly)
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }

        if (depth <= 0)
        {
            yield break;
        }

        IEnumerable<string> children;

        try
        {
            children = Directory
                .EnumerateDirectories(directory)
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var child in children)
        {
            try
            {
                if (ignoredDirectories.Contains(
                        Path.GetFileName(child)))
                {
                    continue;
                }

                if ((File.GetAttributes(child) &
                     FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            foreach (var file in EnumerateExeFiles(
                         child,
                         depth - 1))
            {
                yield return file;
            }
        }
    }
}

public sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 14;

    public Color BorderColor { get; set; } = default;

    public RoundedPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);

        if (Width < 2 || Height < 2)
        {
            return;
        }

        using var path =
            new System.Drawing.Drawing2D.GraphicsPath();

        var radius = Math.Max(
            1,
            Math.Min(
                Radius * 2,
                Math.Min(Width - 1, Height - 1)));

        path.AddArc(0, 0, radius, radius, 180, 90);

        path.AddArc(
            Width - radius - 1,
            0,
            radius,
            radius,
            270,
            90);

        path.AddArc(
            Width - radius - 1,
            Height - radius - 1,
            radius,
            radius,
            0,
            90);

        path.AddArc(
            0,
            Height - radius - 1,
            radius,
            radius,
            90,
            90);

        path.CloseFigure();

        var previousRegion = Region;

        Region = new Region(path);

        previousRegion?.Dispose();
    }

    protected override void OnPaintBackground(
        PaintEventArgs e)
    {
        using var path =
            new System.Drawing.Drawing2D.GraphicsPath();

        var radius = Math.Max(
            1,
            Math.Min(
                Radius * 2,
                Math.Min(Width - 1, Height - 1)));

        path.AddArc(0, 0, radius, radius, 180, 90);

        path.AddArc(
            Width - radius - 1,
            0,
            radius,
            radius,
            270,
            90);

        path.AddArc(
            Width - radius - 1,
            Height - radius - 1,
            radius,
            radius,
            0,
            90);

        path.AddArc(
            0,
            Height - radius - 1,
            radius,
            radius,
            90,
            90);

        path.CloseFigure();

        e.Graphics.Clear(Parent?.BackColor ?? BackColor);

        e.Graphics.SmoothingMode =
            System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(BackColor);

        e.Graphics.FillPath(brush, path);

        var borderColor = BorderColor == default
            ? Color.FromArgb(220, 226, 232)
            : BorderColor;

        var borderWidth = BorderColor == default ? 1 : 2;

        using var pen = new Pen(borderColor, borderWidth);

        e.Graphics.DrawPath(pen, path);
    }
}

public sealed class RoundedButton : Button
{
    public string Glyph { get; set; } = "";

    public bool Active { get; set; }

    public int Radius { get; set; } = 10;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        UseVisualStyleBackColor = false;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var path =
            new System.Drawing.Drawing2D.GraphicsPath();

        var radius = Math.Max(
            1,
            Math.Min(
                Radius * 2,
                Math.Min(Width - 1, Height - 1)));

        path.AddArc(0, 0, radius, radius, 180, 90);

        path.AddArc(
            Width - radius - 1,
            0,
            radius,
            radius,
            270,
            90);

        path.AddArc(
            Width - radius - 1,
            Height - radius - 1,
            radius,
            radius,
            0,
            90);

        path.AddArc(
            0,
            Height - radius - 1,
            radius,
            radius,
            90,
            90);

        path.CloseFigure();

        e.Graphics.Clear(Parent?.BackColor ?? BackColor);

        e.Graphics.SmoothingMode =
            System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var fillColor = Enabled
            ? BackColor
            : Color.FromArgb(226, 231, 236);

        using var brush = new SolidBrush(fillColor);

        e.Graphics.FillPath(brush, path);

        var bounds = ClientRectangle;

        if (Glyph.Length > 0)
        {
            var inset = (int)(16 * DeviceDpi / 96f);
            var iconWidth = (int)(28 * DeviceDpi / 96f);

            using var iconFont =
                new Font("Segoe MDL2 Assets", 14);

            TextRenderer.DrawText(
                e.Graphics,
                Glyph,
                iconFont,
                new Rectangle(
                    inset,
                    0,
                    iconWidth,
                    Height),
                ForeColor,
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.HorizontalCenter);

            bounds = new Rectangle(
                inset + iconWidth + 8,
                0,
                Math.Max(
                    1,
                    Width - inset - iconWidth - 32),
                Height);
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            bounds,
            Enabled
                ? ForeColor
                : Color.FromArgb(155, 163, 172),
            (Glyph.Length == 0
                ? TextFormatFlags.HorizontalCenter
                : TextFormatFlags.Left) |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);

        if (Active)
        {
            using var dot = new SolidBrush(
                Color.FromArgb(101, 185, 12));

            e.Graphics.FillEllipse(
                dot,
                Width - 20,
                Height / 2 - 4,
                8,
                8);
        }
    }
}

public sealed class CoverPictureBox : PictureBox
{
    public CoverPictureBox()
    {
        SizeMode = PictureBoxSizeMode.Normal;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        if (Image is null)
        {
            return;
        }

        e.Graphics.InterpolationMode =
            System.Drawing.Drawing2D.InterpolationMode
                .HighQualityBicubic;

        var scale = Math.Min(
            (float)Width / Image.Width,
            (float)Height / Image.Height);

        var width = Image.Width * scale;
        var height = Image.Height * scale;

        var destination = new RectangleF(
            (Width - width) / 2,
            (Height - height) / 2,
            width,
            height);

        e.Graphics.DrawImage(Image, destination);
    }
}

public sealed partial class MainForm : Form
{
    enum InstallMode
    {
        Official = 0,
        // Persisted IDs are stable: 1 is the retired Pre-SR mode, 2 is OptiScaler.
        OptiScalerStandard = 2
    }

    readonly ComboBox installMode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 300
    };

    readonly FlowLayoutPanel games = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        WrapContents = true,
        Padding = new Padding(20),
        BackColor = Color.FromArgb(245, 247, 250)
    };

    readonly Label selected = new()
    {
        AutoSize = true,
        ForeColor = Color.FromArgb(70, 78, 92),
        Text = "尚未选择游戏"
    };

    readonly Label status = new()
    {
        AutoSize = true,
        ForeColor = Color.FromArgb(80, 88, 102),
        Text = "正在扫描游戏…"
    };

    readonly ProgressBar progress = new()
    {
        Width = 180,
        Height = 8,
        Style = ProgressBarStyle.Marquee,
        Visible = false
    };

    readonly Button install = new RoundedButton
    {
        Text = "一键配置",
        AutoSize = true,
        Enabled = false
    };

    readonly Button restore = new RoundedButton
    {
        Text = "恢复配置",
        AutoSize = true,
        Enabled = false
    };

    readonly Button scan = new RoundedButton
    {
        Text = "重新扫描",
        AutoSize = true
    };

    readonly CancellationTokenSource lifetime = new();

    readonly string workRoot = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "AMD-NR-Assistant",
        "transactions");

    string? selectedGame;
    string? transactionState;

    InstallMode SelectedInstallMode =>
        installMode.SelectedIndex == 1
            ? InstallMode.OptiScalerStandard
            : InstallMode.Official;

    RoundedPanel? selectedCard;

    Panel? aboutPanel;
    Panel? homePanel;

    Label? homeSummary;

    bool english;

    async Task ScanGamesAsync()
    {
        if (busy || !scan.Enabled) return;
        scan.Enabled = false;
        progress.Visible = true;

        status.Text = "正在扫描游戏目录…";

        try
        {
            var list = await GameScanner.ScanAsync(
                lifetime.Token);

            // Merge after the asynchronous scan so games added during scanning are not lost.
            var manualWarning = "";
            try
            {
                foreach (var exe in GameLibrary.ReadManual().Where(File.Exists))
                    if (!list.Any(g => string.Equals(g.ExePath, exe, StringComparison.OrdinalIgnoreCase)))
                        list.Add(new GameCandidate { Title = Path.GetFileNameWithoutExtension(exe), ExePath = exe, InstallDirectory = Path.GetDirectoryName(exe)! });
            }
            catch (IOException e) { manualWarning = "；" + e.Message; }
            libraryGames = list;
            selectedCard = null;
            while (games.Controls.Count > 0) { var card = games.Controls[0]; games.Controls.Remove(card); card.Dispose(); }

            foreach (var game in list)
            {
                var card = CreateGameCard(game);
                card.Visible = game.Title.Contains(librarySearch.Text, StringComparison.OrdinalIgnoreCase);
                games.Controls.Add(card);
            }

            status.Text = list.Count == 0
                ? "没有找到游戏。可以使用“添加游戏”选择 EXE。"
                : $"找到 {list.Count} 个游戏，正在加载封面…";

            await LoadCoversAsync(list.ToArray());

            RefreshHome();

            status.Text = list.Count == 0
                ? "没有找到游戏。可以使用“添加游戏”选择 EXE。"
                : $"找到 {list.Count} 个游戏，点击卡片选择。";
            status.Text += manualWarning;
        }
        catch (OperationCanceledException)
        {
            // 程序关闭时取消扫描。
        }
        catch (Exception exception)
        {
            status.Text = "扫描失败：" + exception.Message;
        }
        finally
        {
            scan.Enabled = true;
            progress.Visible = false;
        }
    }

    async Task LoadCoversAsync(
        IReadOnlyList<GameCandidate> list)
    {
        var cache = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "AMD-NR-Assistant",
            "covers");

        Directory.CreateDirectory(cache);

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        foreach (var game in list.Where(game =>
                     !string.IsNullOrWhiteSpace(
                         game.SteamAppId)))
        {
            try
            {
                var local = Path.Combine(
                    cache,
                    game.SteamAppId + ".jpg");

                if (!File.Exists(local))
                {
                    byte[] data;

                    try
                    {
                        data = await client.GetByteArrayAsync(
                            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{game.SteamAppId}/library_600x900_2x.jpg",
                            lifetime.Token);
                    }
                    catch
                    {
                        data = await client.GetByteArrayAsync(
                            $"https://steamcdn-a.akamaihd.net/steam/apps/{game.SteamAppId}/library_600x900_2x.jpg",
                            lifetime.Token);
                    }

                    if (data.Length < 10_000)
                    {
                        continue;
                    }

                    await File.WriteAllBytesAsync(
                        local,
                        data,
                        lifetime.Token);
                }

                game.CoverPath = local;

                var card = games.Controls
                    .Cast<Control>()
                    .FirstOrDefault(control =>
                        ReferenceEquals(
                            control.Tag,
                            game));

                if (card?.Controls
                        .OfType<PictureBox>()
                        .FirstOrDefault() is PictureBox picture)
                {
                    using var stream = new MemoryStream(
                        await File.ReadAllBytesAsync(
                            local,
                            lifetime.Token));

                    using var source = Image.FromStream(stream);

                    var previousImage = picture.Image;

                    picture.Image = new Bitmap(source);
                    picture.SizeMode = PictureBoxSizeMode.Zoom;

                    previousImage?.Dispose();
                }
            }
            catch
            {
                // 网络不可用时保留游戏 EXE 图标。
            }
        }
    }

    Control CreateGameCard(GameCandidate game)
    {
        var card = new RoundedPanel
        {
            Width = 230,
            Height = 411,
            Margin = new Padding(10),
            Padding = new Padding(1),
            BackColor = Color.White,
            Cursor = Cursors.Hand,
            Tag = game,
            Radius = 14
        };

        Image image;

        if (game.CoverPath is not null &&
            File.Exists(game.CoverPath))
        {
            using var source = Image.FromFile(game.CoverPath);
            image = new Bitmap(source);
        }
        else
        {
            image = game.Icon?.ToBitmap() ??
                    SystemIcons.Application.ToBitmap();
        }

        var icon = new CoverPictureBox
        {
            Width = 226,
            Height = 339,
            Location = new Point(2, 2),
            BackColor = Color.FromArgb(225, 231, 236),
            Image = image
        };

        var title = new Label
        {
            Text = game.Title,
            Location = new Point(14, 352),
            Width = 208,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold),
            AutoEllipsis = true
        };

        var ready = false;

        try
        {
            ready = Core.CheckInstalled(
                    Path.GetDirectoryName(game.ExePath)!)
                .Ready;
        }
        catch
        {
            // 尚未配置或无法读取目录。
        }

        var availability = new Label
        {
            Text = ready
                ? english
                    ? "Files present · runtime unverified"
                    : "组件存在 · 生效未验证"
                : english
                    ? "Compatibility unverified"
                    : "兼容性待检测",

            Location = new Point(14, 381),
            Width = 202,
            Height = 24,
            ForeColor = Color.FromArgb(78, 158, 0),
            AutoEllipsis = true
        };

        void Pick(object? sender, EventArgs eventArgs)
        {
            if (busy) return;
            if (selectedCard is not null)
            {
                selectedCard.BorderColor = default;
                selectedCard.Invalidate();
            }

            selectedCard = card;

            card.BorderColor =
                Color.FromArgb(92, 178, 0);

            card.Invalidate();

            selectedGame = game.ExePath;

            SwitchPage(1);

            selected.Text = english
                ? "Selected: " + game.Title
                : "已选择：" + game.Title;

            status.Text = ready
                ? english
                    ? "DLSS 5 is configured. Enable FSR and press End in game."
                    : "此游戏已配置 DLSS 5；启动游戏并启用 FSR后按 End。"
                : english
                    ? "You can now click Configure."
                    : "现在可以点击“一键配置”。";

            UpdateButtons();
        }

        foreach (var control in new Control[]
                 {
                     card,
                     icon,
                     title,
                     availability
                 })
        {
            control.Click += Pick;
        }

        card.Controls.Add(icon);
        card.Controls.Add(title);
        card.Controls.Add(availability);

        return card;
    }

    void SelectGameManually(
        object? sender,
        EventArgs eventArgs)
    {
        if (busy) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "游戏程序 (*.exe)|*.exe",
            Title = "选择游戏本体 EXE"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        AddPath(dialog.FileName);
    }

    async Task InstallAsync()
    {
        if (selectedGame is null)
        {
            return;
        }

        if (busy) return;
        var targetExe = selectedGame;
        var chosenMode = SelectedInstallMode;
        Diagnostics.Record(targetExe, "install", "start", chosenMode.ToString());
        busy = true;
        libraryPage.Enabled = false;
        install.Enabled = false;
        restore.Enabled = false;

        string? state = null;
        bool completed = false;

        try
        {
            var compatibility = await Task.Run(() => GameManagement.Check(targetExe, true, chosenMode != InstallMode.OptiScalerStandard));
            if (compatibility.Blocked)
            {
                status.Text = "安装检查未通过，请查看提示；旧组件冲突可点击“恢复配置”处理。";
                Diagnostics.Record(targetExe, "compatibility", "blocked", string.Join("; ", compatibility.Details));
                MessageBox.Show(this, string.Join("\n", compatibility.Details), "安装检查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (GameManagement.Read(targetExe) is { Phase: not "restored" })
                throw new IOException("请先恢复此游戏，再安装或切换模式。");
            if (MessageBox.Show(this, compatibility.Summary + "\n\n" + string.Join("\n", compatibility.Details) + "\n\n是否继续安装？", "兼容性检查", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            if (chosenMode == InstallMode.OptiScalerStandard)
            {
                await InstallOptiScalerStandardAsync(targetExe); return;
            }

            var gameDirectory = Path.GetDirectoryName(
                Path.GetFullPath(selectedGame))!;

            state = Path.Combine(
                workRoot,
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(state);

            var installerPath = Path.Combine(
                state,
                Core.InstallerName);

            var bundledDll = Path.Combine(
                state,
                Core.DllName);

            status.Text = "正在释放并校验内置 DLSS 5 组件…";
            Core.ExtractBundledDll(bundledDll);
            Diagnostics.Record(targetExe, "bundled-dll", "validated", Core.Hash(bundledDll));

            ReleaseInfo release;

            var cachedInstaller =
                Core.FindCachedInstaller();

            if (cachedInstaller is not null)
            {
                File.Copy(
                    cachedInstaller,
                    installerPath,
                    false);

                release = new ReleaseInfo(
                    Core.ReviewedTag,
                    Core.Repository +
                    "/releases/download/" +
                    Core.ReviewedTag +
                    "/" +
                    Core.InstallerName,
                    Core.ReviewedInstallerSha256,
                    Core.ReviewedInstallerSize);

                status.Text =
                    $"已使用本机缓存的官方 {Core.ReviewedTag} 安装器。";
            }
            else
            {
                try
                {
                    using var client = new HttpClient
                    {
                        Timeout = Timeout.InfiniteTimeSpan
                    };

                    status.Text =
                        "正在连接官方 GitHub 并核对安装器…";
                    Diagnostics.Record(targetExe, "github-release-query", "start");

                    release = await Core.GetReleaseAsync(
                        client,
                        lifetime.Token);

                    status.Text =
                        $"正在下载官方安装器 {release.Tag}…";
                    Diagnostics.Record(targetExe, "installer-download", "start", release.Tag);

                    await Core.DownloadAsync(
                        client,
                        release,
                        installerPath,
                        new Progress<int>(n => status.Text = $"正在下载模式一安装器：{n}%"),
                        lifetime.Token,
                        message => { status.Text = message; Diagnostics.Record(targetExe, "installer-download", "source", message); });

                    Core.CacheInstaller(installerPath);
                }
                catch (Exception networkError)
                    when (networkError is HttpRequestException ||
                          networkError is TaskCanceledException &&
                          !lifetime.IsCancellationRequested)
                {
                    Diagnostics.Record(targetExe, "network", "failed", networkError.ToString());
                    cachedInstaller =
                        Core.FindCachedInstaller();

                    if (cachedInstaller is null)
                    {
                        throw new IOException(
                            "无法连接官方 GitHub（443），且本机没有已校验缓存。" +
                            "请在网络可用时运行一次，或将获授权的官方安装器" +
                            "放在应用 EXE 同目录后重试。",
                            networkError);
                    }

                    File.Copy(
                        cachedInstaller,
                        installerPath,
                        false);

                    release = new ReleaseInfo(
                        Core.ReviewedTag,
                        Core.Repository +
                        "/releases/download/" +
                        Core.ReviewedTag +
                        "/" +
                        Core.InstallerName,
                        Core.ReviewedInstallerSha256,
                        Core.ReviewedInstallerSize);

                    status.Text =
                        "GitHub 当前不可访问，已使用本机缓存的官方安装器。";
                }
            }

            Core.ValidateDll(bundledDll);
            Diagnostics.Record(targetExe, "installer-download", "validated", release.Tag);

            status.Text = "正在准备游戏目录…";

            GameManagement.Begin(targetExe, 0);
            Diagnostics.Record(targetExe, "game-files", "staging");

            Core.Stage(
                gameDirectory,
                installerPath,
                bundledDll,
                state,
                release.Tag);

            var stagedGameDll = Path.Combine(gameDirectory, Core.DllName);
            if (!File.Exists(stagedGameDll))
                throw new IOException("模式一准备阶段未找到 " + Core.DllName + "。可能被 Windows 安全软件隔离；请检查 Windows 安全中心的保护历史。");
            var bundledHash = Core.Hash(bundledDll);
            var stagedHash = Core.Hash(stagedGameDll);
            if (!string.Equals(stagedHash, bundledHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("模式一 DLL 写入游戏目录后哈希发生变化，未启动安装器。预期：" + bundledHash + "，实际：" + stagedHash);
            Diagnostics.Record(targetExe, "game-files", "dll-staged", stagedHash);

            var stagedInstaller = Path.Combine(
                gameDirectory,
                Core.InstallerName);

            Core.ValidateInstaller(stagedInstaller);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(
                    stagedInstaller)
                {
                    WorkingDirectory = gameDirectory,
                    UseShellExecute = true
                }
            };

            if (!process.Start())
            {
                throw new IOException(
                    "无法启动官方安装器。");
            }
            Diagnostics.Record(targetExe, "external-installer", "started");

            Core.MarkPhase(
                state,
                "installer-launched");

            status.Text =
                "请在官方安装器窗口中完成安装；" +
                "完成后助手会自动核验。";

            await process.WaitForExitAsync(
                lifetime.Token);
            Diagnostics.Record(targetExe, "external-installer", "exited", "exitCode=" + process.ExitCode);

            Core.MarkPhase(
                state,
                "installer-returned");

            var check =
                Core.CheckInstalled(gameDirectory);

            var finalDll = Path.Combine(gameDirectory, Core.DllName);
            if (!File.Exists(finalDll))
            {
                Diagnostics.Record(targetExe, "installed-files", "dll-missing",
                    "官方安装器退出后缺失；准备阶段哈希=" + Core.Hash(bundledDll));
            }
            else
            {
                Diagnostics.Record(targetExe, "installed-files", "dll-present", Core.Hash(finalDll));
            }

            for (var attempt = 0;
                 !check.Ready && attempt < 6;
                 attempt++)
            {
                await Task.Delay(
                    500,
                    lifetime.Token);

                check = Core.CheckInstalled(
                    gameDirectory);
            }

            if (!check.Ready)
            {
                Diagnostics.Record(targetExe, "installed-files", "incomplete", check.Message);
                status.Text = check.Message;

                MessageBox.Show(
                    this,
                    check.Message +
                    "\n\n请确认安装器与 nvngx_dlssnr.dll " +
                    "位于游戏 EXE 同一目录，并在游戏设置中启用 FSR。",
                    "安装未完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            var configuredFile = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "AMD-NR-Assistant",
                "configured-games.txt");

            Directory.CreateDirectory(
                Path.GetDirectoryName(configuredFile)!);

            var configured = File.Exists(configuredFile)
                ? File.ReadAllLines(configuredFile)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            configured.Remove(selectedGame);
            configured.Add(selectedGame);

            File.WriteAllLines(
                configuredFile,
                configured);

            transactionState = state;

            completed = true;
            Diagnostics.Record(targetExe, "installed-files", "complete", "游戏运行效果尚未验证。");

            RefreshHome();

            status.Text =
                $"配置完成（代理：{check.ProxyName}）。" +
                "启动游戏并启用 FSR，按 End 打开菜单。";

            MessageBox.Show(
                this,
                "配置已完成。请启动游戏并启用 FSR；" +
                "进入游戏后按 End 打开菜单。\n\n" +
                "如果 End 无反应，请检查游戏 EXE 目录是否存在代理 DLL、" +
                "dlssnr_on_amd.ini 和 dlssnr_on_amd_weights.bin。",
                "AMD DLSS MU",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            Diagnostics.Record(targetExe, "install", "cancelled");
            status.Text = "操作已取消。";
        }
        catch (Exception exception)
        {
            Diagnostics.Record(targetExe, "install", "failed", exception.ToString());
            status.Text = exception.Message;

            if (state is not null &&
                File.Exists(Path.Combine(
                    state,
                    "journal.json")))
            {
                try
                {
                    var journal =
                        System.Text.Json.JsonSerializer
                            .Deserialize<Journal>(
                                File.ReadAllText(
                                    Path.Combine(
                                        state,
                                        "journal.json")));

                    if (journal?.Phase is
                        "prepared" or "staged")
                    {
                        Core.Rollback(state);
                    }
                }
                catch
                {
                    // 回滚失败时保留事务记录。
                }
            }
        }
        finally
        {
            try
            {
                if (GameManagement.Read(targetExe) is { Phase: "installing" })
                    GameManagement.Finish(targetExe, completed);
            }
            catch (Exception e) { status.Text = "安装记录未完成，请保留现场：" + e.Message; }
            busy = false;
            libraryPage.Enabled = true;
            UpdateButtons();
        }
    }

    async Task<ReleaseInfo?> CheckReleaseAsync()
    {
        using var client = new HttpClient();

        return await Core.GetReleaseAsync(
            client,
            lifetime.Token);
    }

    async Task RestoreAsync()
    {
        if (busy || selectedGame == null) return;
        var target = selectedGame;
        busy = true; libraryPage.Enabled = false; UpdateButtons();
        try
        {
            if (GameManagement.Read(target) is { Phase: not "restored" } record)
            {
                var files = string.Join("\n", record.Files.Where(f => f.Before != f.After).Select(f => (f.Before.Length == 0 ? "移除：" : "还原：") + f.Name));
                if (MessageBox.Show(this, "将恢复安装前备份，移除本次新增组件。改动过的 INI 配置会先另存备份。\n\n" + files,
                    "恢复配置", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                await Task.Run(() => GameManagement.Restore(target, true));
                status.Text = "已恢复安装前配置，本次新增组件已移除。备份保留在：" + GameManagement.State(target);
            }
            else
            {
                var candidates = await Task.Run(() => GameManagement.LegacyCandidates(target));
                if (candidates.Count == 0) { status.Text = "未发现可清理的组件。"; return; }
                if (MessageBox.Show(this, "此游戏没有有效的安装前备份，无法保证还原原始状态。\n以下文件可能属于旧版 DLSS 或其他插件；移出后相关插件可能停止工作。\n\n" +
                    string.Join("\n", candidates.Keys) + "\n\n确认将以上文件从游戏目录移到独立备份目录？如需撤销，可关闭游戏后从备份目录复制回原位置。",
                    "恢复配置 — 旧组件清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                var archive = await Task.Run(() => GameManagement.QuarantineLegacy(target, candidates));
                status.Text = "候选组件已移出，可重新配置。备份：" + archive;
                MessageBox.Show(this, "清理完成。文件及清单已保存到：\n" + archive + "\n\n如游戏异常，关闭游戏后将所需文件复制回游戏 EXE 目录。", "恢复配置");
            }
            var configuredFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "configured-games.txt");
            if (File.Exists(configuredFile))
                File.WriteAllLines(configuredFile, File.ReadAllLines(configuredFile).Where(p => !string.Equals(p, target, StringComparison.OrdinalIgnoreCase)));
            transactionState = null;
        }
        catch (Exception exception)
        {
            status.Text = exception.Message;
            MessageBox.Show(this, exception.Message, "恢复未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false; libraryPage.Enabled = true;
            RefreshHome(); UpdateButtons();
        }
    }


    void UpdateButtons()
    {
        try
        {
            var record = selectedGame == null ? null : GameManagement.Read(selectedGame);
            transactionState = null;
            install.Enabled = !busy && selectedGame != null && (record == null || record.Phase == "restored");
            restore.Enabled = !busy && selectedGame != null;
        }
        catch (Exception e) { install.Enabled = restore.Enabled = false; status.Text = e.Message; }
    }
}
