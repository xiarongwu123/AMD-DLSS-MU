using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Mu.Recommendation;
namespace AmdNrAssistant;

public static class EnvironmentDetector
{
    public static async Task<EnvironmentSnapshot> DetectAsync(string exe,string gameId,string title,int width,int height,
        CancellationToken token,string confirmedApi="",string confirmedInput="",string windowed="")
    {
        exe=Path.GetFullPath(exe);Core.ValidateGame(exe);
        var reader=new CompatibilityEnvironmentReader();var gpus=await reader.ReadGpusAsync(token);
        var gpu=SmartRenderAdvisor.SelectGpu(gpus);var facts=new Dictionary<string,Observation>();
        void Fact(string key,string? value,string source,string confidence="CONFIRMED")=>facts[key]=string.IsNullOrWhiteSpace(value)||value=="unknown"?new():new(value,confidence,source);
        var environment=await reader.ReadAsync(exe,Path.GetDirectoryName(exe),gpu??new("unknown","unknown",null,null,"unknown"),token);
        Fact("gpuModel",gpu?.Name,"dxdiag；多适配器时并不证明游戏使用此适配器",gpus.Count==1?"CONFIRMED":"INFERRED");
        Fact("gpuVendor",gpu?.Vendor,"dxdiag");Fact("driver",gpu?.DriverVersion,"Windows驱动版本；不等同Adrenalin版本");
        Fact("deviceId",gpu?.DeviceId,"dxdiag",gpus.Count==1?"CONFIRMED":"INFERRED");
        Fact("architecture",NeuralRenderPlanner.GpuSeries(gpu?.Name??"") switch{"rx6000"=>"rdna2","rx7000"=>"rdna3","rx9000"=>"rdna4",_=>"unknown"},"显卡型号推断","INFERRED");
        Fact("vramMb",gpu?.VramMb?.ToString(),"dxdiag DedicatedMemory");facts["vramBudgetMb"]=new();
        Fact("os",OperatingSystem.IsWindows()?"windows":"other",RuntimeInformation.OSDescription);
        Fact("osBuild",Environment.OSVersion.Version.Build.ToString(),"Windows系统版本");
        Fact("gameId",gameId,"本机游戏库标准标识");Fact("gameVersion",environment.GameVersion,"EXE版本与Steam构建清单");
        Fact("gameBuild",await Task.Run(()=>Core.Hash(exe),token),"主EXE SHA256；版本未知时也不假造版本号");
        Fact("bitness",Core.IsX86(exe)?"32":"64","PE Machine");
        var api="unknown";try{api=X86Bridge.DetectApi(exe);if(api=="unknown")api=NeuralRenderPlanner.DetectApi(exe);}catch(Exception e)when(e is IOException or BadImageFormatException or ArgumentOutOfRangeException){}
        Fact("api",api,"PE静态导入，非实际运行接口","INFERRED");
        if(RecommendationRuleSchema.Apis.Contains(confirmedApi))Fact("api",confirmedApi,"用户按游戏实际设置确认，非自动检测");
        if(new[]{"dlss","xess","fsr","none"}.Contains(confirmedInput))Fact("upscalerInput",confirmedInput,"用户确认当前游戏超分输入");else facts["upscalerInput"]=new();
        if(windowed is "true" or "false")Fact("windowed",windowed,"用户确认窗口模式");else facts["windowed"]=new();
        Fact("resolution",width>0&&height>0?$"{width}x{height}":"unknown","当前显示器");
        var root=Path.GetDirectoryName(exe)!;var components=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var name in GameManagement.Loaders.Concat(new[]{"OptiScaler.ini","ReShade.ini","amd-nr.addon32","amd-nr.addon64","dlssnr_amd_pass1.dll","LmxxfNrRuntime.dll","MochizukiNrRuntime.dll"}).Distinct())
        {
            var file=Path.Combine(root,name);Core.RejectLinks(file);if(File.Exists(file))components[name]=Core.Hash(file);
        }
        var optiFolder=Path.Combine(root,"OptiScaler");Core.RejectLinks(optiFolder);
        if(Directory.Exists(optiFolder))foreach(var folder in GameManagement.ComponentDirectories(optiFolder))
            foreach(var file in Directory.EnumerateFiles(folder).Where(f=>new[]{".dll",".ini",".asi"}.Contains(Path.GetExtension(f).ToLowerInvariant())).Take(512))
            {Core.RejectLinks(file);components[Path.GetRelativePath(root,file).Replace('\\','/')]=Core.Hash(file);}
        var record=GameManagement.Read(exe);var active=record is {Phase:not "restored"};
        var conflicts=components.Keys.Where(name=>GameManagement.Loaders.Contains(name,StringComparer.OrdinalIgnoreCase)&&
            !(active&&record!.Files.Any(f=>f.Name.Equals(name,StringComparison.OrdinalIgnoreCase)&&f.After==components[name]))).ToArray();
        if(!active&&components.Keys.Any(x=>x.StartsWith("OptiScaler/",StringComparison.OrdinalIgnoreCase)))conflicts=conflicts.Append("OptiScaler/").ToArray();
        var integrity=false;if(active&&record!=null)foreach(var f in record.Files.Where(x=>x.Before!=x.After))
        {var file=Path.Combine(root,f.Name);Core.RejectLinks(file);if(!File.Exists(file)||Core.Hash(file)!=f.After){integrity=true;break;}}
        var resources=new Dictionary<string,bool>{["hip"]=SmartRenderDiagnostics.HasHipRuntime(),["amd-magpie-resources"]=AmdMagpieIntegration.HasResources()};
        resources["hip-preparable"]=facts["architecture"].Value=="rdna2";
        foreach(var backend in new[]{"daniel","lmxxf","mochizuki"})
        {
            if(backend=="mochizuki")
            {
                try{resources["mochizuki-model"]=NeuralRenderInstaller.HasVerifiedMochizukiModel(exe);}
                catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException){resources["mochizuki-model"]=false;}
                resources["mochizuki-model-preparable"]=NeuralRenderInstaller.CanPrepareMochizukiModel;
                continue;
            }
            var plan=new NeuralPlan(gameId,backend,"dx12","dxgi.dll",1,"unverified","probe",false,false,false,[],new(Backend:backend));
            bool ready;try{ready=NeuralRenderInstaller.MissingResources(exe,plan).Length==0;}catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException){ready=false;}
            if(backend=="daniel"){resources["daniel-runtime"]=ready;resources["daniel-weights"]=ready;}else resources[backend+"-model"]=ready;
        }
        bool backup=false;
        try
        {
            // Probe MU's backup volume only. Recommendation never writes into the game directory.
            Core.RejectLinks(GameManagement.Root);Directory.CreateDirectory(GameManagement.Root);
            var probe=Path.Combine(GameManagement.Root,".recommendation-probe-"+Guid.NewGuid().ToString("N"));
            using(var stream=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)){stream.WriteByte(0);stream.Flush(true);}
            backup=true;
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
        var snapshot=new EnvironmentSnapshot{Executable=exe,GameId=gameId,GameName=title,Fields=facts,Components=components,Resources=resources,
            Conflicts=conflicts,AntiCheat=GameManagement.DetectAntiCheat(exe),Running=GameManagement.RunningSince(exe)!=null,
            BackupReady=backup,HasConfiguration=active,IntegrityFailed=integrity};
        return snapshot with{Fingerprint=Fingerprint(snapshot)};
    }
    public static string Fingerprint(EnvironmentSnapshot snapshot)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        new{snapshot.Fields,snapshot.Components,snapshot.Resources,snapshot.Conflicts,snapshot.AntiCheat,snapshot.Running,snapshot.BackupReady,snapshot.HasConfiguration,snapshot.IntegrityFailed}))));
}
