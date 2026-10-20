using System.Diagnostics;
namespace AmdNrAssistant;

public sealed partial class MainForm
{
    async Task<bool> StartAmdMagpieAsync()
    {
        if(busy)throw new IOException("请等待当前操作完成。");
        if(!await RequireFeatureAsync("schemes.premium.configure"))return false;
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)||System.Runtime.InteropServices.RuntimeInformation.OSArchitecture!=System.Runtime.InteropServices.Architecture.X64)
            throw new IOException("AMD 大力喜鹊需要 Windows x64。");
        var running=Process.GetProcessesByName("Magpie");
        try{if(running.Length>0)throw new IOException("请先从托盘退出正在运行的 Magpie，避免混用原版与 AMD 版配置。");}
        finally{foreach(var process in running)process.Dispose();}
        if(!await Task.Run(AmdMagpieIntegration.HasResources,lifetime.Token))
        {
            using var picker=new FolderBrowserDialog{Description="选择你有权使用的 AMD Magpie 配套资源：Daniel 0.5.0 Runtime 与模型。此版本不能替换为 0.8。",UseDescriptionForTitle=true};
            if(picker.ShowDialog(this)!=DialogResult.OK)return false;
            await Task.Run(()=>AmdMagpieIntegration.ImportResources(picker.SelectedPath),lifetime.Token);
        }
        busy=true;UpdateButtons();
        using var session=BeginDownloadSession("AMD 大力喜鹊",StartAmdMagpieAsync,"schemes.premium.configure");
        try
        {
            var exe=await AmdMagpieIntegration.PrepareAsync(new Progress<string>(s=>status.Text=s),lifetime.Token);
            if(!await RequireFeatureAsync("schemes.premium.configure"))return false;
            using var process=Process.Start(new ProcessStartInfo(exe){WorkingDirectory=Path.GetDirectoryName(exe)!,UseShellExecute=true});
            if(process==null)throw new IOException("未能启动 AMD 大力喜鹊。");
            status.Text="已启动 AMD 大力喜鹊实验版。切回窗口游戏，按 Alt + Shift + A 启用；效果仍需在工具中确认。";
            return true;
        }
        finally{busy=false;UpdateButtons();}
    }
}
