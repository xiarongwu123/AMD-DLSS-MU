using System.Diagnostics;
using Mu.Recommendation;
namespace AmdNrAssistant;

public static class VerificationService
{
    public static VerificationResult Observe(string exe,RecommendationCandidate? candidate,bool acceptExperience=false)
    {
        var errors=new List<string>();var evidence=new List<string>();var record=GameManagement.Read(exe);
        var installed=record is {Phase:"installed"};var loaded=false;var active=false;
        if(candidate!=null&&record!=null)
        {
            var expected=candidate.Scheme switch{"fps"=>4,"legacy"=>candidate.Configuration.Bitness==32?5:8,"standard"=>2,_=>6};
            if(record.Mode!=expected||record.Neural!=null&&record.Neural.PackageVersion!=candidate.Configuration.ComponentVersion)installed=false;
        }
        if(candidate!=null&&installed&&candidate.Configuration.Ini.Count>0)
        {
            var path=Path.Combine(Path.GetDirectoryName(exe)!,candidate.Scheme=="legacy"?"amd-nr.ini":"OptiScaler.ini");Core.RejectLinks(path);
            var text=File.Exists(path)?File.ReadAllText(path):"";
            if(candidate.Configuration.Ini.Any(section=>section.Value.Any(pair=>ManagedIni.Get(text,section.Key,pair.Key)!=pair.Value)))
            {installed=false;errors.Add("PRESET_CHANGED");evidence.Add("当前参数与这次推荐预设不一致，不能把结果归入原预设。");}
        }
        if(candidate?.External==true)return new("UNKNOWN",false,false,false,false,[],["外部工具管理；MU无法确认该游戏已部署或实际生效。"],DateTimeOffset.UtcNow);
        if(installed&&record!=null)
        {
            foreach(var file in record.Files.Where(f=>f.Before!=f.After))
            {
                var path=Path.Combine(Path.GetDirectoryName(exe)!,file.Name);Core.RejectLinks(path);
                if(!File.Exists(path)||Core.Hash(path)!=file.After){installed=false;errors.Add("INTEGRITY_FAILED");break;}
            }
            if(installed)evidence.Add("安装文件与MU所有权记录的SHA256一致；不证明运行生效。");
        }
        if(installed&&record!=null)
        {
            var names=candidate?.Scheme=="standard"?new[]{candidate.Configuration.Proxy}:candidate?.Configuration.Backend switch{"mochizuki"=>new[]{"MochizukiNrRuntime.dll"},"lmxxf"=>new[]{"LmxxfNrRuntime.dll"},_=>new[]{"dlssnr_amd_pass1.dll","dlssnr_amd_pass2.dll","version.dll"}};
            foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))using(process)
            {
                try
                {
                    if(!string.Equals(process.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase))continue;
                    loaded=process.Modules.Cast<ProcessModule>().Any(m=>names.Contains(Path.GetFileName(m.FileName),StringComparer.OrdinalIgnoreCase)&&
                        record.Files.Any(f=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exe)!,f.Name)).Equals(m.FileName,StringComparison.OrdinalIgnoreCase)&&File.Exists(m.FileName)&&Core.Hash(m.FileName)==f.After));
                }catch(Exception e)when(e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException or IOException){evidence.Add("游戏进程模块不可读取，加载状态未知。");}
            }
        }
        if(loaded)evidence.Add("在当前游戏进程观察到与安装清单匹配的目标Runtime模块。");
        // None of the current seven pinned packages exposes an authenticated, versioned
        // processed-frame counter to MU. Generic log words and process exit are not ACTIVE/crash evidence.
        evidence.Add("当前上游版本尚无已接入的可靠功能执行计数接口，无法确认NR/XeFG实际生效。");
        if(acceptExperience)evidence.Add("用户反馈体验可接受；保留为用户反馈，不冒充运行时执行证据。");
        return new(errors.Count>0?"FAILED":active&&acceptExperience?"EXPERIENCE_CONFIRMED":active?"ACTIVE":loaded?"LOADED":installed?"INSTALLED":"UNKNOWN",
            installed,loaded,active,active&&acceptExperience,errors.Distinct().ToArray(),evidence.ToArray(),DateTimeOffset.UtcNow);
    }
    public static string[] ClassifyFreshLog(string text)=>new Dictionary<string,string>{["failed to load"]="PROXY_LOAD_FAILED",["initialization failed"]="NR_INITIALIZATION_FAILED",
        ["invalid kernel"]="MODEL_COMPILE_FAILED",["device removed"]="RUNTIME_EXIT",["hash mismatch"]="INTEGRITY_FAILED",["anti-cheat"]="ANTI_CHEAT"}
        .Where(p=>text.Contains(p.Key,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).Distinct().ToArray();
}
