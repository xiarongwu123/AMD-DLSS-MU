using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
namespace AmdNrAssistant;

public static class X86Bridge
{
    public const int Mode = 5;
    public const string Version = "ReShade bridge 0.7.12 / runtime 0.6.0";
    public static readonly string[] Proxies = ["dxgi.dll", "d3d9.dll", "d3d8.dll", "opengl32.dll"];
    public static readonly string[] Managed = ["amd-nr.addon32", "amd-nr-host64.exe", "amd-nr.ini", "ReShade.ini", "d3d9.dll", "d3d8.dll", "opengl32.dll"];
    public sealed record Pin(string Name, long Size, string Sha256, string Url, string[]? Mirrors);
    // Embedded metadata is used in desktop builds and linked test builds alike.
    public static Pin[] ReadPins()
    {
        using var stream=typeof(X86Bridge).Assembly.GetManifestResourceStream("Mu.X86Bridge.Pins") ?? throw new IOException("32位桥接组件清单缺失。");
        return JsonSerializer.Deserialize<Pin[]>(stream,new JsonSerializerOptions { PropertyNameCaseInsensitive=true }) ?? throw new IOException("32位桥接清单损坏。");
    }
    public static string DetectApi(string exe)
    {
        using var file=File.OpenRead(exe);using var pe=new PEReader(file);
        var rva=pe.PEHeaders.PEHeader?.ImportTableDirectory.RelativeVirtualAddress ?? 0;
        if(rva==0)return "unknown";
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries=pe.GetSectionData(rva).GetContent();
        for(int i=0;i+20<=entries.Length && i<5120;i+=20)
        {
            var nameRva=BitConverter.ToInt32(entries.AsSpan(i+12,4));if(nameRva==0)break;
            var name=pe.GetSectionData(nameRva).GetContent();var length=0;
            while(length<name.Length && length<256 && name[length]!=0)length++;
            names.Add(Encoding.ASCII.GetString(name.AsSpan(0,length)));
        }
        return ApiFromImports(names);
    }
    public static string ApiFromImports(IEnumerable<string> imports)
    {
        var names=imports.ToHashSet(StringComparer.OrdinalIgnoreCase);var apis=new List<string>();
        if(names.Contains("d3d8.dll"))apis.Add("dx8");
        if(names.Contains("d3d9.dll"))apis.Add("dx9");
        if(names.Contains("d3d10.dll")||names.Contains("d3d10_1.dll")||names.Contains("d3d11.dll"))apis.Add("dx11");
        if(names.Contains("opengl32.dll"))apis.Add("opengl");
        return apis.Count==1?apis[0]:"unknown";
    }
    public static string Proxy(string api)=>api switch { "dx8" or "dx9"=>"d3d9.dll", "dx11"=>"dxgi.dll", "opengl"=>"opengl32.dll", _=>throw new IOException("未能确认32位游戏接口，请在自定义中选择 DirectX 8、9、10/11 或 OpenGL。") };
    public static async Task<string> PrepareAsync(IProgress<string> progress,CancellationToken token)
    {
        var folder=Path.Combine(Core.InstallerCacheDirectory,"x86-bridge-0.7.12");Core.RejectLinks(folder);Directory.CreateDirectory(folder);
        using var client=new HttpClient();client.DefaultRequestHeaders.UserAgent.ParseAdd("AMD-DLSS-MU/2.2.1");
        foreach(var pin in ReadPins())
        {
            var path=Path.Combine(folder,pin.Name);Core.RejectLinks(path);
            if(File.Exists(path) && new FileInfo(path).Length==pin.Size && Core.Hash(path)==pin.Sha256)continue;
            progress.Report("正在下载32位桥接组件："+pin.Name);
            await DownloadSources.DownloadAsync(client,pin.Url,null,pin.Sha256,pin.Size,path,new Progress<int>(),token,s=>progress.Report(s),pin.Mirrors?.FirstOrDefault());
        }
        ValidatePackage(folder);return folder;
    }
    public static void ValidatePackage(string folder)
    {
        foreach(var pin in ReadPins())
        {
            var path=Path.Combine(folder,pin.Name);Core.RejectLinks(path);
            if(!File.Exists(path)||new FileInfo(path).Length!=pin.Size||Core.Hash(path)!=pin.Sha256)throw new IOException("32位桥接组件校验失败："+pin.Name);
            if(pin.Name.EndsWith(".bin"))continue;
            var want=pin.Name is "amd-nr-host64.exe" or "dlssnr_amd_pass1.dll" ? 0x8664 : 0x14c;
            if(Core.CheckPeMachine(path,pin.Name!="amd-nr-host64.exe",true)!=want)throw new IOException("桥接组件位数不匹配："+pin.Name);
        }
    }
    public static void Install(string exe,string package,string api)
    {
        Core.ValidateGame(exe);if(!Core.IsX86(exe))throw new IOException("32位桥接仅用于x86游戏。");
        GameManagement.EnsureClosed(exe);var proxy=Proxy(api);ValidatePackage(package);
        var files=new Dictionary<string,string> { [proxy]="dxgi.dll",["amd-nr.addon32"]="amd-nr.addon32",["amd-nr-host64.exe"]="amd-nr-host64.exe",["dlssnr_amd_pass1.dll"]="dlssnr_amd_pass1.dll",["dlssnr_on_amd_weights.bin"]="dlssnr_on_amd_weights.bin" };
        if(api=="dx8")files["d3d8.dll"]="d3d8to9.dll";
        var root=Path.GetDirectoryName(exe)!;
        foreach(var name in files.Keys.Concat(new[]{"amd-nr.ini","ReShade.ini"}))
        {var path=Path.Combine(root,name);Core.RejectLinks(path);if(File.Exists(path)||Directory.Exists(path))throw new IOException("游戏目录已有组件，请先恢复或卸载对应插件："+name);}
        GameManagement.Begin(exe,Mode,Version);
        try
        {
            foreach(var pair in files)
            {
                var source=Path.Combine(package,pair.Value);var target=Path.Combine(root,pair.Key);File.Copy(source,target,false);
                if(Core.Hash(source)!=Core.Hash(target))throw new IOException("桥接写入校验失败："+pair.Key);
            }
            File.WriteAllText(Path.Combine(root,"amd-nr.ini"),"[amd-nr]\r\nStartOn=1\r\nScale=1.0\r\nColourStrength=0.25\r\nStructure=1\r\nSkin=-1\r\nPasses=1\r\n");
            File.WriteAllText(Path.Combine(root,"ReShade.ini"),"[GENERAL]\r\nAddonPath=.\r\n[ADDON]\r\nDisabledAddons=\r\n");
            GameManagement.Finish(exe,true);
        }
        catch {GameManagement.Finish(exe,false);GameManagement.Restore(exe);throw;}
    }
}
