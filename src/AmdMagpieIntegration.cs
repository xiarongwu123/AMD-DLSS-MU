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
    // Pin the actual full release, whose Magpie.exe recognises this runtime.
    // The source tag has a different runtime digest and does not match its binary asset.
    public const string RuntimeSha256 = "0983b4ce860972fd8794be553f3880659252ad6457df27761ae6126c07959afc";
    public const string ExecutableSha256 = "f996a34c583fa2c51cfa341ce90507c24a604d46922a9b6f3efee366d5ce544b";
    public const string WeightsSha256 = "6bf8dc931ef3ccffe18c82de26ab374156e7f19539ffcf8eabaa25dca5cf15ab";
    internal static string? StorageOverride { get; set; }
    public static string Root => StorageOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD-NR-Assistant", "tools", "AmdMagpie");
    public static string Resources => Path.Combine(Root, "user-resources");
    public static string Installation => Path.Combine(Root, Tag);
    public static readonly string[] ResourceFiles = ["dlssnr_amd_pass1.dll", "dlssnr_on_amd_weights.bin"];
    public static void ValidateResources(string folder)
    {
        var runtime = Path.Combine(folder, ResourceFiles[0]); var weights = Path.Combine(folder, ResourceFiles[1]);
        Core.RejectLinks(runtime); Core.RejectLinks(weights);
        if (!File.Exists(runtime) || new FileInfo(runtime).Length != 38703616 || Core.Hash(runtime) != RuntimeSha256)
            throw new IOException("AMD 大力喜鹊配套运行库缺失或校验失败，需要重新准备完整包；不能使用其他版本的 DLL 替换。");
        Core.CheckPe(runtime, true);
        if (!File.Exists(weights) || new FileInfo(weights).Length != 147689451 || Core.Hash(weights) != WeightsSha256)
            throw new IOException("AMD 大力喜鹊配套模型缺失或校验失败，需要重新准备完整包。");
    }
    public static bool HasResources()
    {
        try { ValidateResources(Installation);return true; }catch(IOException){}
        try { ValidateResources(Resources);return true; }catch(IOException){return false;}
    }
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
        Directory.CreateDirectory(destination);
        long total=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var target=MagpieIntegration.SafeEntryPath(destination,entry.FullName);
            if((entry.ExternalAttributes>>16&0xf000)==0xa000||!seen.Add(target))throw new IOException("安装包含链接或重复路径。");
            total=checked(total+entry.Length);if(total>3L*1024*1024*1024)throw new IOException("安装包解压大小异常。");
            if(entry.FullName.EndsWith('/')||entry.FullName.EndsWith('\\')){Directory.CreateDirectory(target);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target,false);
        }
        var matches=Directory.GetFiles(destination,"Magpie.exe",SearchOption.AllDirectories);
        if(matches.Length!=1)throw new IOException("未找到唯一 AMD Magpie 主程序。");
        Core.CheckPe(matches[0],false);
        var app=Path.GetDirectoryName(matches[0])!;
        if(Core.Hash(matches[0])!=ExecutableSha256)throw new IOException("AMD 大力喜鹊主程序与固定发布包不一致。");
        if(!File.Exists(Path.Combine(app,"resources.pri"))||!File.Exists(Path.Combine(app,"effects","DLSSNR","DLSSNR_AI_Filter.hlsl")))
            throw new IOException("AMD 大力喜鹊完整包缺少资源或 DLSSNR 效果。");
        ValidateResources(app);
        var files=Directory.GetFiles(destination,"*",SearchOption.AllDirectories)
            .Where(p=>!Path.GetRelativePath(destination,p).Replace('\\','/').StartsWith("config/",StringComparison.OrdinalIgnoreCase)&&
                !p.Equals(Path.Combine(app,"dlssnr_on_amd.ini"),StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p=>Path.GetRelativePath(destination,p),p=>Core.Hash(p));
        File.WriteAllText(Path.Combine(destination,"mu-package.json"),JsonSerializer.Serialize(files));
        var config=Path.Combine(app,"config","v4e","config.json");Core.RejectLinks(config);
        if(!File.Exists(config)){Directory.CreateDirectory(Path.GetDirectoryName(config)!);File.WriteAllText(config,MagpieIntegration.ConfigJson);}
    }
    public static async Task<string> PrepareAsync(IProgress<string> progress,CancellationToken token)
    {
        Core.RejectLinks(Root);Directory.CreateDirectory(Root);
        using var gate=new FileStream(Path.Combine(Root,"prepare.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        progress.Report("正在校验 AMD 大力喜鹊组件…");
        var ready=await Task.Run(()=>
        {
            if(!Directory.Exists(Installation))return false;
            try{ValidateInstalledPayload(Installation,token);return true;}
            catch(Exception e)when(e is IOException or JsonException){return false;}
        },token);
        if(!ready)
        {
            var archive=Path.Combine(Root,PackageName);Core.RejectLinks(archive);
            var cached=await Task.Run(()=>File.Exists(archive)&&new FileInfo(archive).Length==Size&&Core.Hash(archive)==Sha256,token);
            if(!cached)
            {
                progress.Report("正在下载 AMD 大力喜鹊完整包（约 210 MiB），包含配套运行库与模型…");
                using var http=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};
                await DownloadSources.DownloadAsync(http,Repository+"/releases/download/"+Tag+"/"+PackageName,
                    "https://api.github.com/repos/A-ENTROPY/magpie-dlss5-amd/releases/assets/605761421",Sha256,Size,archive,
                    new Progress<int>(n=>progress.Report($"AMD 大力喜鹊下载 {n}%")),token,progress.Report);
            }
            progress.Report("正在解压并校验 AMD 大力喜鹊完整包…");
            var stage=Path.Combine(Root,".staging-"+Guid.NewGuid().ToString("N"));
            try
            {
                await Task.Run(()=>
                {
                    ExtractVerified(archive,stage,token);
                    ValidateInstalledPayload(stage,token);
                    if(Directory.Exists(Installation))
                    {
                        PreserveSettings(Installation,stage,token);
                        var backup=Installation+".before-repair-"+Guid.NewGuid().ToString("N");Core.RejectLinks(Installation);
                        token.ThrowIfCancellationRequested();Directory.Move(Installation,backup);
                        try{Directory.Move(stage,Installation);}
                        catch{Directory.Move(backup,Installation);throw;}
                    }
                    else{token.ThrowIfCancellationRequested();Directory.Move(stage,Installation);}
                },token);
            }
            finally
            {
                // A locked temporary file must not replace the original failure.
                // Every retry uses a fresh staging directory and keeps the verified archive.
                try{if(Directory.Exists(stage))Directory.Delete(stage,true);}
                catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        progress.Report("正在准备独立配置…");
        return await Task.Run(()=>
        {
            var exe=ValidateInstalledPayload(Installation,token);var folder=Path.GetDirectoryName(exe)!;
            var config=Path.Combine(folder,"config","v4e","config.json");Core.RejectLinks(config);
            if(!File.Exists(config)){Directory.CreateDirectory(Path.GetDirectoryName(config)!);File.WriteAllText(config,MagpieIntegration.ConfigJson);}
            var ini=Path.Combine(folder,"dlssnr_on_amd.ini");Core.RejectLinks(ini);
            if(!File.Exists(ini))File.WriteAllText(ini,"");
            MagpieIntegration.EnsureDefaultEffect(folder,token);
            return exe;
        },token);
    }
    static string ValidateInstalledPayload(string directory,CancellationToken token)
    {
        var exe=MagpieIntegration.ValidateInstallation(directory,token);
        if(Core.Hash(exe)!=ExecutableSha256)throw new IOException("AMD 大力喜鹊主程序校验失败。");
        Core.CheckPe(exe,false);ValidateResources(Path.GetDirectoryName(exe)!);return exe;
    }
    static void PreserveSettings(string previous,string destination,CancellationToken token)
    {
        Core.RejectLinks(previous);
        void CopyDirectory(string source,string target)
        {
            Core.RejectLinks(source);Core.RejectLinks(target);Directory.CreateDirectory(target);
            foreach(var file in Directory.EnumerateFiles(source)){token.ThrowIfCancellationRequested();Core.RejectLinks(file);File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);}
            foreach(var folder in Directory.EnumerateDirectories(source))CopyDirectory(folder,Path.Combine(target,Path.GetFileName(folder)));
        }
        var config=Path.Combine(previous,"config");
        if(Directory.Exists(config))CopyDirectory(config,Path.Combine(destination,"config"));
        var ini=Path.Combine(previous,"dlssnr_on_amd.ini");Core.RejectLinks(ini);
        if(File.Exists(ini)){token.ThrowIfCancellationRequested();File.Copy(ini,Path.Combine(destination,"dlssnr_on_amd.ini"),true);}
    }
}
