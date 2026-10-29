using System.IO.Compression;
using System.Text.Json;

namespace AmdNrAssistant;

public static class HighQualityInstaller
{
    public const string Version = "0.0.8-w08g5-preview";
    public sealed record Pin(long Size, string Sha256);
    static readonly Lazy<Dictionary<string, Pin>> pins = new(() => {
        using var s = typeof(HighQualityInstaller).Assembly.GetManifestResourceStream("Mu.HighQuality.Pins") ?? throw new IOException("缺少画质增强版资源清单。");
        return JsonSerializer.Deserialize<Dictionary<string,Pin>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    });
    public static string Route(string api, string input, string requested)
    {
        if (api is not ("dx9" or "dx10" or "dx11" or "dx12" or "vulkan")) throw new IOException("请确认游戏实际图形接口。");
        var route = requested == "auto" ? api == "dx9" ? "dx9" : input is "dlss" or "fsr" or "xess" && api != "dx10" ? "optiscaler" : input == "none" || api == "dx10" ? "reshade" : throw new IOException("请选择游戏是否提供 DLSS/FSR/XeSS，或手动选择接入方式。") : requested;
        if (route is not ("optiscaler" or "reshade" or "dx9") || route == "optiscaler" && api is "dx9" or "dx10" || route == "dx9" && api != "dx9") throw new IOException("接入方式与图形接口不匹配。");
        return api == "dx9" ? "dx9" : route == "reshade" && api == "vulkan" ? "vulkan" : route;
    }
    public static Dictionary<string,string> Map(string route)
    {
        if (route is not ("optiscaler" or "reshade" or "dx9" or "vulkan")) throw new IOException("未知画质增强版接入方式。");
        var map = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in pins.Value.Keys)
        {
            if (name.StartsWith("dlssnr-amd/")) map[name] = name;
            if (route == "optiscaler")
            {
                if (name.StartsWith("optiscaler/release/OptiScaler/")) map[name[19..]] = name;
                if (name.StartsWith("optiscaler/release/Licenses/")) map["OptiScaler/" + name[19..]] = name;
                if (name.StartsWith("optiscaler/asi/") && name.EndsWith("LICENSE.txt")) map["OptiScaler/Licenses/" + Path.GetFileName(name)] = name;
            }
            else if (name.StartsWith("reshade/") && name != "reshade/ReShadePreset-d3d9.ini")
            {
                if (route == "reshade" && (name.StartsWith("reshade/vk-override/") || name == "reshade/ReShade64.dll")) continue;
                map[name[8..]] = name;
            }
        }
        if (route == "optiscaler")
        {
            map["dxgi.dll"] = "optiscaler/release/OptiScaler.dll";
            map["OptiScaler.ini"] = "optiscaler/release/OptiScaler.ini";
            map["OptiScaler/plugins/dlssnr-amd.asi"] = "optiscaler/asi/dlssnr-amd.asi";
            map["dlssnr-amd/nr_present_probe.exe"] = "optiscaler/asi/nr_present_probe.exe";
            map["OptiScaler/Licenses/OptiScaler_LICENSE.txt"] = "optiscaler/OptiScaler_LICENSE.txt";
        }
        else if (route == "reshade") map["dxgi.dll"] = "reshade/ReShade64.dll";
        else
        {
            map["vulkan-1.dll"] = "vulkan/vulkan-1.dll";
            map["Licenses/MU-Vulkan-Loader-LICENSE.txt"] = "vulkan/Vulkan-Loader-LICENSE.txt";
            if (route == "dx9") { map["dxgi.dll"] = "dxvk/dxgi.dll"; map["d3d9.dll"] = "dxvk/d3d9.dll"; map["ReShadePreset.ini"] = "reshade/ReShadePreset-d3d9.ini"; map["Licenses/MU-DXVK-LICENSE.txt"] = "dxvk/LICENSE.txt"; }
        }
        map["dlssnr-amd.ini"] = route == "optiscaler" ? "ini/dlssnr-amd-optiscaler-renodx.ini" : "ini/dlssnr-amd-addon.ini";
        return map;
    }
    static readonly Lazy<HashSet<string>> managed = new(() => new[] { "optiscaler", "reshade", "dx9", "vulkan" }.SelectMany(route => Map(route).Keys).ToHashSet(StringComparer.OrdinalIgnoreCase));
    public static bool ManagedPath(string name) => managed.Value.Contains(name.Replace('\\','/'));
    public static async Task<string> PrepareAsync(string route, IProgress<string> progress, CancellationToken token)
    {
        var stage = Path.Combine(Core.InstallerCacheDirectory, "high-quality-" + Guid.NewGuid().ToString("N"));
        Core.RejectLinks(stage); Directory.CreateDirectory(stage);
        try
        {
            using var stream = typeof(HighQualityInstaller).Assembly.GetManifestResourceStream("Mu.HighQuality.Package") ?? throw new IOException("缺少画质增强版完整资源包。");
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var map = Map(route); var index = 0;
            foreach (var (target, source) in map)
            {
                token.ThrowIfCancellationRequested();
                var entry = zip.GetEntry(source) ?? throw new IOException("缺少组件：" + source);
                var pin = pins.Value[source]; if (entry.Length != pin.Size) throw new IOException("组件大小错误：" + source);
                var dest = Path.Combine(stage, target); Core.RejectLinks(dest); Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using (var input = entry.Open()) await using (var output = new FileStream(dest, FileMode.CreateNew)) await input.CopyToAsync(output, token);
                if (Core.Hash(dest) != pin.Sha256) throw new IOException("组件校验失败：" + source);
                if (++index % 25 == 0) progress.Report($"画质增强版：准备并校验组件 {index}/{map.Count}");
            }
            var ini = Path.Combine(stage,"dlssnr-amd.ini");
            var text = File.ReadAllText(ini); text = ManagedIni.Set(text,"Network","Model","CC_SMALL_FP8");
            text = ManagedIni.Set(text,"RenoDX","Enabled","1"); File.WriteAllText(ini,text);
            if (route == "optiscaler") { ini = Path.Combine(stage,"OptiScaler.ini"); File.WriteAllText(ini,ManagedIni.Set(File.ReadAllText(ini),"Plugins","LoadAsiPlugins","true")); }
            progress.Report("运行组件与两份模型已校验，准备部署。"); return stage;
        }
        catch { Directory.Delete(stage,true); throw; }
    }
    public static void Install(string exe, string stage, string route, CancellationToken token, IProgress<string> progress)
    {
        Core.ValidateGame(exe); Core.CheckPe(exe,false); GameManagement.EnsureClosed(exe);
        if(GameManagement.Read(exe) is {Phase:not "restored"}) throw new IOException("请先恢复原配置。");
        var root = Path.GetDirectoryName(exe)!;
        if(GameManagement.Loaders.Any(n=>File.Exists(Path.Combine(root,n)))) throw new IOException("检测到旧代理，请先恢复或卸载旧插件。");
        var map = Map(route); var files = new List<ManagedFile>();
        foreach(var name in map.Keys)
        {
            token.ThrowIfCancellationRequested(); var dest=Path.Combine(root,name); Core.RejectLinks(dest);
            var source=Path.Combine(stage,name); Core.RejectLinks(source);
            var before=File.Exists(dest)?Core.Hash(dest):"";
            if(File.Exists(dest) && name.EndsWith(".ini",StringComparison.OrdinalIgnoreCase))
            {
                var decoded=ManagedIni.Decode(File.ReadAllBytes(dest));
                var text=decoded.Text;
                if(name=="OptiScaler.ini") text=ManagedIni.Set(text,"Plugins","LoadAsiPlugins","true");
                else if(name=="dlssnr-amd.ini") { text=ManagedIni.Set(text,"Network","Model","CC_SMALL_FP8");text=ManagedIni.Set(text,"RenoDX","Enabled","1"); }
                else throw new IOException("存在旧 ReShade 配置，请先备份并卸载旧插件："+name);
                File.WriteAllBytes(source,ManagedIni.Encode(text,decoded.Encoding));
            }
            if(before.Length>0)
            {
                var backup=Path.Combine(GameManagement.State(exe),name+".backup"); Core.RejectLinks(backup);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Copy(dest,backup,true);
                if(Core.Hash(backup)!=before)throw new IOException("备份失败："+name);
            }
            files.Add(new(name,before,Core.Hash(source)));
        }
        token.ThrowIfCancellationRequested();
        var record=new GameRecord(exe,6,"画质增强版 "+Version,DateTime.UtcNow,"incomplete",files){Scheme="high-quality"};
        GameManagement.Save(record);
        try
        {
            foreach(var f in files)
            {
                token.ThrowIfCancellationRequested(); var target=Path.Combine(root,f.Name);Core.RejectLinks(target);
                if((File.Exists(target)?Core.Hash(target):"")!=f.Before)throw new IOException("安装期间文件变更："+f.Name);
                if(f.Before==f.After)continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var tmp=target+".mu-"+Guid.NewGuid().ToString("N");
                progress.Report("正在部署画质增强组件："+f.Name);
                try {File.Copy(Path.Combine(stage,f.Name),tmp);if(Core.Hash(tmp)!=f.After)throw new IOException("复制校验失败");File.Move(tmp,target,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}
            }
            foreach(var f in files)if(Core.Hash(Path.Combine(root,f.Name))!=f.After)throw new IOException("部署校验失败："+f.Name);
            GameManagement.Save(record with{Phase="installed"});progress.Report("画质增强版文件已部署；请进游戏确认 ACO 与 NR 生效。");
        }
        catch(Exception error)
        {
            try {GameManagement.Restore(exe,true);}catch(Exception rollback){throw new IOException("安装失败且回滚未完成，备份已保留。",new AggregateException(error,rollback));}throw;
        }
    }
}
