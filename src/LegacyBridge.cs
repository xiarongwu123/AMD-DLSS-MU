using System.IO.Compression;
namespace AmdNrAssistant;

// The x86 bridge stays on its reviewed ABI. This is a separate x64 ReShade route,
// not an attempt to drop the incompatible NeuralHub 0.8 runtime into an old addon.
public static class LegacyBridge
{
    public const int Mode = 8;
    public const string Version = "ReShade64 6.8.0 / addon 0.7.12 / runtime 0.6.0";
    public const string ReShadeHash = "0cee63f9c9f13f3ac909c5b4903f4dbb4b719a7ab3b4f13b0deaf83c814b94f7";
    public static readonly string[] Managed = ["amd-nr.addon64", "amd-nr.ini", "ReShade.ini", "d3d9.dll", "opengl32.dll"];
    public static string Proxy(string api) => api switch
    { "dx9" => "d3d9.dll", "dx11" => "dxgi.dll", "opengl" => "opengl32.dll", _ => throw new IOException("老游戏64位方案支持 DirectX 9、10/11 和 OpenGL；请确认游戏图形接口。") };
    static X86Bridge.Pin[] Pins() => X86Bridge.ReadPins().Where(p => p.Name is "dlssnr_amd_pass1.dll" or "dlssnr_on_amd_weights.bin").Concat(new[] {
        new X86Bridge.Pin("amd-nr.addon64", 868352, "44f6935f5545d35e855704011728dcf7207e71985430dc98d3857107568d246a", "https://huggingface.co/datasets/zmodelerlover/amd-nr/resolve/main/addon/0.7.12/amd-nr.addon64", ["https://github.com/zmodelerlover/dlss5-neural-amd/releases/download/v0.7.12/amd-nr.addon64"]),
        new X86Bridge.Pin("ReShade_Setup_6.8.0_Addon.exe", 4318424, "afe4c8f13048306307983b8b3d41d5bf00a86820440b0e57dea10950e1176445", "https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe", [])
    }).ToArray();
    public static async Task<string> PrepareAsync(IProgress<string> progress, CancellationToken token)
    {
        var folder = Path.Combine(Core.InstallerCacheDirectory, "legacy64-0.7.12"); Core.RejectLinks(folder); Directory.CreateDirectory(folder);
        using var gate = new FileStream(Path.Combine(folder,"prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        foreach (var pin in Pins())
        {
            var target = Path.Combine(folder, pin.Name); Core.RejectLinks(target);
            if (File.Exists(target) && new FileInfo(target).Length == pin.Size && Core.Hash(target) == pin.Sha256) continue;
            progress.Report("正在准备老游戏组件：" + pin.Name);
            await DownloadSources.DownloadAsync(http, pin.Url, null, pin.Sha256, pin.Size, target, new Progress<int>(), token, progress.Report, pin.Mirrors?.FirstOrDefault());
        }
        token.ThrowIfCancellationRequested();
        var dll = Path.Combine(folder, "ReShade64.dll"); Core.RejectLinks(dll);
        if (!File.Exists(dll) || Core.Hash(dll) != ReShadeHash)
        {
            ExtractReShade(Path.Combine(folder, "ReShade_Setup_6.8.0_Addon.exe"), dll);
        }
        ValidatePackage(folder); return folder;
    }
    public static void ExtractReShade(string archive, string destination)
    {
        Core.RejectLinks(archive); Core.RejectLinks(destination);
        var pin = Pins().Single(p => p.Name == "ReShade_Setup_6.8.0_Addon.exe");
        if(new FileInfo(archive).Length!=pin.Size || Core.Hash(archive)!=pin.Sha256)throw new IOException("ReShade 原始包校验失败。");
        // ReShade is an SFX with ZIP-relative directory offsets. .NET's ZipFile
        // reader does not adjust for the PE prefix; locate the ZIP origin from
        // the reviewed end-of-directory record instead of executing its setup.
        var data=File.ReadAllBytes(archive);var end=-1;
        for(var i=data.Length-22;i>=Math.Max(0,data.Length-65557);i--)
            if(BitConverter.ToUInt32(data,i)==0x06054b50u && i+22+BitConverter.ToUInt16(data,i+20)<=data.Length){end=i;break;}
        if(end<0)throw new IOException("ReShade 压缩目录无效。");
        var start=checked(end-(int)BitConverter.ToUInt32(data,end+12)-(int)BitConverter.ToUInt32(data,end+16));
        if(start<0||start>=end||BitConverter.ToUInt32(data,start)!=0x04034b50u)throw new IOException("ReShade 压缩数据偏移无效。");
        // The Authenticode certificate follows the ZIP end record; exclude it.
        using var payload=new MemoryStream(data,start,end+22+BitConverter.ToUInt16(data,end+20)-start,false);
        using var zip = new ZipArchive(payload,ZipArchiveMode.Read);
        var entries = zip.Entries.Where(e => e.FullName == "ReShade64.dll").ToArray();
        if(entries.Length!=1 || entries[0].Length!=5592064)throw new IOException("ReShade 安装包不含已核验的64位组件。");
        using var input=entries[0].Open();using var bytes=new MemoryStream();input.CopyTo(bytes);
        if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray())).ToLowerInvariant()!=ReShadeHash)
            throw new IOException("ReShade64 解压摘要不匹配。");
        NeuralRenderInstaller.AtomicWrite(destination,bytes.ToArray());
    }
    public static void ValidatePackage(string folder)
    {
        foreach (var pin in Pins())
        {
            var path = Path.Combine(folder, pin.Name); Core.RejectLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length != pin.Size || Core.Hash(path) != pin.Sha256)
                throw new IOException("老游戏组件不匹配：" + pin.Name);
        }
        var reshade = Path.Combine(folder, "ReShade64.dll"); Core.RejectLinks(reshade);
        if (!File.Exists(reshade) || Core.Hash(reshade) != ReShadeHash) throw new IOException("ReShade64 校验失败。");
        foreach (var name in new[] { "ReShade64.dll", "amd-nr.addon64", "dlssnr_amd_pass1.dll" }) Core.CheckPe(Path.Combine(folder, name), true);
    }
    public static void Install(string exe, string package, string api)
    {
        Core.ValidateGame(exe); if (Core.IsX86(exe)) throw new IOException("此入口用于64位游戏；32位游戏使用专门桥接。");
        GameManagement.EnsureClosed(exe); var proxy = Proxy(api); ValidatePackage(package);
        var files = new Dictionary<string,string> { [proxy]="ReShade64.dll", ["amd-nr.addon64"]="amd-nr.addon64", ["dlssnr_amd_pass1.dll"]="dlssnr_amd_pass1.dll", ["dlssnr_on_amd_weights.bin"]="dlssnr_on_amd_weights.bin" };
        var directory = Path.GetDirectoryName(exe)!;
        foreach (var name in files.Keys.Concat(new[] { "amd-nr.ini", "ReShade.ini" }))
        { var target=Path.Combine(directory,name); Core.RejectLinks(target); if(File.Exists(target)||Directory.Exists(target))throw new IOException("请先恢复已有插件："+name); }
        GameManagement.Begin(exe, Mode, Version);
        try
        {
            foreach (var pair in files)
            {
                var source=Path.Combine(package,pair.Value); var target=Path.Combine(directory,pair.Key); File.Copy(source,target,false);
                if(Core.Hash(target)!=Core.Hash(source))throw new IOException("老游戏组件写入失败："+pair.Key);
            }
            File.WriteAllText(Path.Combine(directory,"amd-nr.ini"),"[amd-nr]\r\nStartOn=1\r\nScale=1.0\r\nColourStrength=0.25\r\nStructure=1\r\nSkin=-1\r\nPasses=1\r\n");
            File.WriteAllText(Path.Combine(directory,"ReShade.ini"),"[GENERAL]\r\nAddonPath=.\r\n[ADDON]\r\nDisabledAddons=\r\n");
            GameManagement.Finish(exe,true);
        }
        catch { GameManagement.Finish(exe,false); GameManagement.Restore(exe); throw; }
    }
}
