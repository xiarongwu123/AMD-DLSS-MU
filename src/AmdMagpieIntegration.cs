using System.IO.Compression;
using System.Text.Json;
namespace AmdNrAssistant;

// Isolated from the original Magpie integration. This fork has an exact 0.5.0
// runtime ABI; neither MU's 0.6.0 runtime nor NeuralHub 0.8 may be substituted.
public static class AmdMagpieIntegration
{
    public const string Tag = "v0.6.8.5-amd-rdna3";
    public const string Repository = "https://github.com/A-ENTROPY/magpie-dlss5-amd";
    public const string PackageName = "magpie-dlss5-amd-rdna3-full.zip";
    public const long Size = 220626282;
    public const string Sha256 = "8a2b45f10633f7154af690075db5255c62d3672d07032e65edd6058029b36aea";
    // DlssnrAmdBackend.cpp at release commit 46f59c5, kRuntimeSha256_050.
    public const string RuntimeSha256 = "cddfb09e019347957bf7b96c95c0e900e8d3062dfaed697a8a96b0a039aec31a";
    public const string WeightsSha256 = "6bf8dc931ef3ccffe18c82de26ab374156e7f19539ffcf8eabaa25dca5cf15ab";
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "tools", "AmdMagpie");
    public static string Resources => Path.Combine(Root, "user-resources");
    public static string Installation => Path.Combine(Root, Tag);
    public static readonly string[] ResourceFiles = ["dlssnr_amd_pass1.dll", "dlssnr_on_amd_weights.bin"];
    public static void ValidateResources(string folder)
    {
        var runtime = Path.Combine(folder, ResourceFiles[0]); var weights = Path.Combine(folder, ResourceFiles[1]);
        Core.RejectLinks(runtime); Core.RejectLinks(weights);
        if (!File.Exists(runtime) || new FileInfo(runtime).Length != 38703616 || Core.Hash(runtime) != RuntimeSha256)
            throw new IOException("AMD 大力喜鹊此版本只接受其核验的 Daniel 0.5.0 运行库；不能使用 0.6 或 0.8 替换。请自行提供匹配的 dlssnr_amd_pass1.dll。");
        Core.CheckPe(runtime, true);
        if (!File.Exists(weights) || new FileInfo(weights).Length != 147689451 || Core.Hash(weights) != WeightsSha256)
            throw new IOException("AMD 大力喜鹊需要匹配的 dlssnr_on_amd_weights.bin，请自行提供后重试。");
    }
    public static bool HasResources()
    { try { ValidateResources(Resources); return true; } catch (IOException) { return false; } }
    public static void ImportResources(string source)
    {
        ValidateResources(source); Core.RejectLinks(Resources); Directory.CreateDirectory(Resources);
        foreach(var name in ResourceFiles)
        {
            var target=Path.Combine(Resources,name); Core.RejectLinks(target);
            var temp=target+".tmp-"+Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(Path.Combine(source,name),temp,false);
                if(Core.Hash(temp)!=(name.EndsWith(".dll")?RuntimeSha256:WeightsSha256))throw new IOException("导入过程中组件发生变化。");
                File.Move(temp,target,true);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        ValidateResources(Resources);
    }
    public static void ExtractVerified(string archive, string destination, CancellationToken token)
    {
        Core.RejectLinks(archive); Core.RejectLinks(destination);
        if(new FileInfo(archive).Length!=Size||Core.Hash(archive)!=Sha256)throw new IOException("AMD 大力喜鹊安装包校验失败。");
        using var zip=ZipFile.OpenRead(archive);
        if(zip.Entries.Count>30000)throw new IOException("AMD 大力喜鹊文件数量异常。");
        long total=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var target=MagpieIntegration.SafeEntryPath(destination,entry.FullName);
            if((entry.ExternalAttributes>>16&0xf000)==0xa000||!seen.Add(target))throw new IOException("安装包含链接或重复路径。");
            total=checked(total+entry.Length);if(total>3L*1024*1024*1024)throw new IOException("安装包解压大小异常。");
            if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(target);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target,false);
        }
        var matches=Directory.GetFiles(destination,"Magpie.exe",SearchOption.AllDirectories);
        if(matches.Length!=1)throw new IOException("未找到唯一 AMD Magpie 主程序。");
        Core.CheckPe(matches[0],false);
        var app=Path.GetDirectoryName(matches[0])!;
        if(!File.Exists(Path.Combine(app,"resources.pri"))||!File.Exists(Path.Combine(app,"effects","DLSSNR","DLSSNR_AI_Filter.hlsl")))
            throw new IOException("AMD 大力喜鹊完整包缺少资源或 DLSSNR 效果。");
        var files=Directory.GetFiles(destination,"*",SearchOption.AllDirectories)
            .Where(p=>!Path.GetRelativePath(destination,p).Replace('\\','/').Contains("config/",StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p=>Path.GetRelativePath(destination,p),p=>Core.Hash(p));
        File.WriteAllText(Path.Combine(destination,"mu-package.json"),JsonSerializer.Serialize(files));
        var config=Path.Combine(app,"config","v4e","config.json");Core.RejectLinks(config);
        if(!File.Exists(config)){Directory.CreateDirectory(Path.GetDirectoryName(config)!);File.WriteAllText(config,MagpieIntegration.ConfigJson);}
    }
    public static async Task<string> PrepareAsync(IProgress<string> progress,CancellationToken token)
    {
        ValidateResources(Resources);Core.RejectLinks(Root);Directory.CreateDirectory(Root);
        using var gate=new FileStream(Path.Combine(Root,"prepare.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(!Directory.Exists(Installation))
        {
            var archive=Path.Combine(Root,PackageName);Core.RejectLinks(archive);
            if(!File.Exists(archive)||new FileInfo(archive).Length!=Size||Core.Hash(archive)!=Sha256)
            {
                using var http=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};
                await DownloadSources.DownloadAsync(http,Repository+"/releases/download/"+Tag+"/"+PackageName,
                    "https://api.github.com/repos/A-ENTROPY/magpie-dlss5-amd/releases/assets/605761421",Sha256,Size,archive,
                    new Progress<int>(n=>progress.Report($"AMD 大力喜鹊下载 {n}%")),token,progress.Report);
            }
            var stage=Path.Combine(Root,".staging-"+Guid.NewGuid().ToString("N"));
            try{await Task.Run(()=>ExtractVerified(archive,stage,token),token);token.ThrowIfCancellationRequested();Directory.Move(stage,Installation);}
            finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
        }
        var exe=MagpieIntegration.ValidateInstallation(Installation,token);var folder=Path.GetDirectoryName(exe)!;
        foreach(var name in ResourceFiles)
        {
            token.ThrowIfCancellationRequested();var target=Path.Combine(folder,name);Core.RejectLinks(target);
            if(File.Exists(target)){if(Core.Hash(target)!=Core.Hash(Path.Combine(Resources,name)))throw new IOException("AMD Magpie 目录已有不同运行库，请退出并备份后处理。");}
            else File.Copy(Path.Combine(Resources,name),target,false);
        }
        ValidateResources(folder);
        var ini=Path.Combine(folder,"dlssnr_on_amd.ini");Core.RejectLinks(ini);
        if(!File.Exists(ini))File.WriteAllText(ini,"");
        MagpieIntegration.EnsureDefaultEffect(folder,token);
        return exe;
    }
}
