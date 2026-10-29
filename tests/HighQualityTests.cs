using AmdNrAssistant;
internal static class HighQualityTests
{
    sealed class Progress(Action<string> report) : IProgress<string> { public void Report(string value)=>report(value); }
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    public static async Task Run()
    {
        Check(HighQualityInstaller.Route("dx12","dlss","auto")=="optiscaler","DX12 input route");
        Check(HighQualityInstaller.Route("dx11","none","auto")=="reshade","DX11 feeder route");
        Check(HighQualityInstaller.Route("dx9","none","auto")=="dx9","DX9 bridge route");
        Check(HighQualityInstaller.Route("vulkan","none","auto")=="vulkan","Vulkan layer route");
        try { RenderSchemes.Validate("high-quality",6,true);throw new Exception("x86 accepted"); } catch(IOException) { }
        var root=Path.Combine(Path.GetTempPath(),"mu-quality-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var prior=GameManagement.StorageOverride;GameManagement.StorageOverride=Path.Combine(root,"records");
        try
        {
            foreach(var route in new[]{"optiscaler","reshade","dx9","vulkan"})
            {
                var stage=await HighQualityInstaller.PrepareAsync(route,new Progress(_=>{}),default);
                try
                {
                    var map=HighQualityInstaller.Map(route);
                    Check(map.Keys.All(GameManagement.IsTracked),"Untracked payload path");
                    Check(File.Exists(Path.Combine(stage,"dlssnr-amd/dlssnr.bin"))&&File.Exists(Path.Combine(stage,"dlssnr-amd/dlssnr-small.bin")),"Missing model");
                    var game=Path.Combine(root,route);Directory.CreateDirectory(game);var exe=Path.Combine(game,"game.exe");
                    var bytes=new byte[512];bytes[0]=0x4d;bytes[1]=0x5a;BitConverter.GetBytes(128).CopyTo(bytes,60);bytes[128]=0x50;bytes[129]=0x45;BitConverter.GetBytes((ushort)0x8664).CopyTo(bytes,132);File.WriteAllBytes(exe,bytes);
                    const string original="[XeFG]\nEnabled=true\n[User]\nKeep=mine\n";
                    if(route=="optiscaler")File.WriteAllText(Path.Combine(game,"OptiScaler.ini"),original,System.Text.Encoding.Unicode);
                    File.WriteAllText(Path.Combine(game,"user.txt"),"keep");
                    HighQualityInstaller.Install(exe,stage,route,default,new Progress(_=>{}));
                    Check(GameManagement.Read(exe) is {Scheme:"high-quality",Phase:"installed"},"Missing successful manifest");
                    Check(map.Keys.All(n=>File.Exists(Path.Combine(game,n))),"Missing installed payload");
                    if(route=="optiscaler")Check(File.ReadAllText(Path.Combine(game,"OptiScaler.ini")).Contains(original)&&ManagedIni.Get(File.ReadAllText(Path.Combine(game,"OptiScaler.ini")),"Plugins","LoadAsiPlugins")=="true","Existing INI lost");
                    if(route=="optiscaler")Check(File.ReadAllBytes(Path.Combine(game,"OptiScaler.ini")).Take(2).SequenceEqual(new byte[]{255,254}),"INI encoding lost");
                    GameManagement.Restore(exe,true);
                    Check(!File.Exists(Path.Combine(game,"dlssnr-amd/dlssnr.bin"))&&File.ReadAllText(Path.Combine(game,"user.txt"))=="keep","Restore damaged files");
                    if(route=="optiscaler")Check(File.ReadAllText(Path.Combine(game,"OptiScaler.ini"))==original,"INI backup not restored exactly");
                    using var cancel=new CancellationTokenSource();
                    try { HighQualityInstaller.Install(exe,stage,route,cancel.Token,new Progress(s=>{if(s.StartsWith("正在部署"))cancel.Cancel();}));throw new Exception("Cancellation ignored"); }catch(OperationCanceledException){}
                    Check(GameManagement.Read(exe)?.Phase=="restored"&&!File.Exists(Path.Combine(game,"dlssnr-amd/dlssnr.bin")),"Cancellation rollback failed");
                    Console.WriteLine($"High-quality {route}: full payload verification, install, INI preservation, restore, cancellation rollback passed ({map.Count} files).");
                }
                finally {Directory.Delete(stage,true);}
            }
        }
        finally {GameManagement.StorageOverride=prior;Directory.Delete(root,true);}
    }
}
