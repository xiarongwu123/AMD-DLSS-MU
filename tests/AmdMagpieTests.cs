using AmdNrAssistant;
using System.Text.Json;

internal static class AmdMagpieTests
{
    sealed class Phases(List<string> messages):IProgress<string>
    {public void Report(string message)=>messages.Add(message);}

    public static async Task RunRealPackageAsync(string archive,string destination)
    {
        var count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("AMD Magpie: "+name);count++;}
        var root=Path.Combine(Path.GetFullPath(destination),"launch-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);AmdMagpieIntegration.StorageOverride=root;
        var messages=new List<string>();var progress=new Phases(messages);
        try
        {
            Check(Core.Hash(archive)==AmdMagpieIntegration.Sha256,"real full package has the pinned digest");
            var cached=Path.Combine(root,AmdMagpieIntegration.PackageName);File.Copy(archive,cached);
            var unrelated=Path.Combine(root,"unrelated.txt");File.WriteAllText(unrelated,"keep");
            var exe=await AmdMagpieIntegration.PrepareAsync(progress,default);
            Check(exe==Path.Combine(AmdMagpieIntegration.Installation,"Magpie.exe"),"cold preparation returns AMD standalone executable");
            Check(!Directory.Exists(AmdMagpieIntegration.Resources),"full package needs no manual resource import");
            Check(Core.Hash(exe)==AmdMagpieIntegration.ExecutableSha256,"real executable is the pinned AMD build");
            Check(File.ReadAllBytes(exe).AsSpan().IndexOf(Convert.FromHexString(AmdMagpieIntegration.RuntimeSha256))>=0,
                "release executable recognises the exact bundled runtime digest");
            AmdMagpieIntegration.ValidateResources(AmdMagpieIntegration.Installation);
            Check(AmdMagpieIntegration.HasResources(),"installed full package counts as ready resources");
            var config=Path.Combine(AmdMagpieIntegration.Installation,"config","v4e","config.json");
            using(var parsed=JsonDocument.Parse(File.ReadAllText(config)))
            {
                var selected=parsed.RootElement.GetProperty("profiles")[0].GetProperty("scalingMode").GetInt32();
                Check(parsed.RootElement.GetProperty("scalingModes")[selected].GetProperty("effects")[0].GetProperty("name").GetString()==MagpieIntegration.DefaultEffect,
                    "portable configuration selects the real DLSSNR effect");
            }
            var custom=File.ReadAllText(config).Replace("\"zh-cn\"","\"en-us\"");File.WriteAllText(config,custom);
            var ini=Path.Combine(AmdMagpieIntegration.Installation,"dlssnr_on_amd.ini");
            File.WriteAllText(ini,"[DlssNrOnAmd]\nExposure=0.9\n; user setting\n");var originalIni=File.ReadAllText(ini);
            var timestamp=File.GetLastWriteTimeUtc(exe);File.Delete(cached);
            Check(await AmdMagpieIntegration.PrepareAsync(progress,default)==exe&&!File.Exists(cached),"repeat open reuses installation without download");
            Check(File.GetLastWriteTimeUtc(exe)==timestamp&&File.ReadAllText(config)==custom&&File.ReadAllText(ini)==originalIni,
                "repeat open leaves payload and editable settings untouched");

            File.Copy(archive,cached);File.WriteAllText(exe,"damaged");
            Check(await AmdMagpieIntegration.PrepareAsync(progress,default)==exe&&Core.Hash(exe)==AmdMagpieIntegration.ExecutableSha256,
                "damaged installation is rebuilt from verified cache");
            Check(File.ReadAllText(config)==custom&&File.ReadAllText(ini)==originalIni,"repair preserves user profile and runtime settings");
            var backup=Directory.GetDirectories(root,AmdMagpieIntegration.Tag+".before-repair-*").Single();
            Check(File.ReadAllText(Path.Combine(backup,"Magpie.exe"))=="damaged","repair retains original directory for recovery");

            File.Delete(Path.Combine(AmdMagpieIntegration.Installation,"mu-package.json"));
            await AmdMagpieIntegration.PrepareAsync(progress,default);
            Check(File.Exists(Path.Combine(AmdMagpieIntegration.Installation,"mu-package.json"))&&File.ReadAllText(config)==custom,
                "interrupted legacy installation with no receipt is recovered without losing settings");
            File.WriteAllText(Path.Combine(AmdMagpieIntegration.Installation,"mu-package.json"),"{");
            await AmdMagpieIntegration.PrepareAsync(progress,default);
            Check(Core.Hash(exe)==AmdMagpieIntegration.ExecutableSha256&&File.ReadAllText(config)==custom,
                "truncated installation receipt is repaired without manual cleanup");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            try{await AmdMagpieIntegration.PrepareAsync(progress,cancelled.Token);throw new Exception("Cancellation ignored");}
            catch(OperationCanceledException){Check(Core.Hash(exe)==AmdMagpieIntegration.ExecutableSha256,"cancellation preserves installed tool");}
            Check(File.ReadAllText(unrelated)=="keep"&&!Directory.GetDirectories(root,".staging-*").Any(),"preparation leaves unrelated files and no staging residue");
            Check(messages.Any(x=>x.Contains("解压"))&&messages.Any(x=>x.Contains("独立配置")),"real preparation reports extraction and configuration progress");
            Console.WriteLine($"AMD Magpie real full-package launch preparation passed: {count} assertions. Windows process/UI and GPU effects require a Windows device.");
        }
        finally{AmdMagpieIntegration.StorageOverride=null;}
    }
}
