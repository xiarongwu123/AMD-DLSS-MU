using AmdNrAssistant;
using Mu.Presets;
using System.Text.Json.Nodes;

internal static class AuditRegressionTests
{
    public static void Run()
    {
        var count = 0;
        void Check(bool ok,string message) { if(!ok)throw new Exception(message);count++; }
        void Reject(Action action,string message) {try{action();}catch(InvalidDataException){count++;return;}throw new Exception(message);}
        PresetSetting Backend() => new("OptiScaler.ini","DlssNr","nrbackend","mochizuki");
        foreach(var key in new[]{"passes","PASSES","PaSsEs"})
            Reject(()=>PresetRules.Validate(6,[Backend(),new("OptiScaler.ini","DlssNr",key,"true")]),"case variant bypassed numeric pass validation");
        var settings = PresetRules.Validate(6,[Backend(),new("OptiScaler.ini","DlssNr","mochizukipasses","3")]);
        Check(CommunityPresetConfig.NeuralOptionsFromPreset(settings).Passes==3,"valid case-insensitive keys fail application");
        Reject(()=>PresetRules.Validate(6,[Backend(),new("OptiScaler.ini","DlssNr","mOchizukiModelScale","true")]),"model scale accepted a boolean");
        foreach(var name in new[]{"CON.ini","nul.zip","AUX.txt","PRN.cfg","COM1.json","LPT9.7z","nul .txt"})
            Reject(()=>PresetResources.ValidateAttachment(new(name,"YQ==")),"reserved Windows filename accepted: "+name);
        Check(PresetResources.ValidateAttachment(new("configuration.ini","YQ==")).Length==1,"valid attachment rejected");
        var error=NeuralRenderInstaller.ModelFailureMessage(1,"ModuleNotFoundError: numpy\nTraceback\nsubprocess.CalledProcessError","extract phase 2");
        Check(error.Contains("ModuleNotFoundError: numpy")&&error.Contains("CalledProcessError")&&error.Contains("extract phase 2")&&error.Contains("退出码：1"),"model failure lost its underlying cause");
        Check(NeuralRenderInstaller.ModelFailureMessage(0,"","").Contains("未输出详细原因"),"silent extraction failure has no actionable message");
        var large=NeuralRenderInstaller.ModelFailureMessage(1,"cause-start"+new string('x',50000)+"cause-end","");
        Check(large.Contains("cause-start")&&large.Contains("cause-end")&&large.Length<13000,"model diagnostic is unbounded or drops its endpoints");
        var root=Path.Combine(Path.GetTempPath(),"mu-marker-audit-"+Guid.NewGuid().ToString("N"));
        try
        {
            var effect=Path.Combine(root,"effects","DLSSNR","DLSSNR_AI_Filter.hlsl");Directory.CreateDirectory(Path.GetDirectoryName(effect)!);File.WriteAllText(effect,"fixture");
            var dir=Path.Combine(root,"config","v4e");Directory.CreateDirectory(dir);var config=Path.Combine(dir,"config.json");
            File.WriteAllText(config,MagpieIntegration.ConfigJson);MagpieIntegration.EnsureDefaultEffect(root,default);
            var data=JsonNode.Parse(File.ReadAllText(config))!.AsObject();data["scalingModes"]!.AsArray().RemoveAt(0);data["profiles"]![0]!["scalingMode"]=0;
            File.WriteAllText(config,data.ToJsonString());MagpieIntegration.EnsureDefaultEffect(root,default);
            var repaired=JsonNode.Parse(File.ReadAllText(config))!;
            Check(repaired["scalingModes"]!.AsArray().Any(m=>m!["name"]!.GetValue<string>()==MagpieIntegration.DefaultModeName),"migration marker prevents deleted group repair");
            Check(repaired["profiles"]![0]!["scalingMode"]!.GetValue<int>()==0,"repair resets a user's explicit selection");
            var before=File.ReadAllText(config);MagpieIntegration.EnsureDefaultEffect(root,default);
            Check(before==File.ReadAllText(config),"repair rewrites untouched config every launch");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        Console.WriteLine($"Audit boundary regressions: {count} assertions passed.");
    }
}
