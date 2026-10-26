using System.Diagnostics;
namespace AmdNrAssistant;

public sealed partial class MainForm
{
    async Task<bool> StartDaniel08Async()
    {
        if(busy)throw new IOException("请等待当前操作完成。");
        if(!await RequireFeatureAsync("dlss.configure"))return false;
        busy=true;UpdateButtons();
        using var session=BeginDownloadSession("Daniel 0.8 · NeuralHub 官方版",StartDaniel08Async,"dlss.configure");
        try
        {
            var installer=await Daniel08Launcher.PrepareAsync(new Progress<string>(s=>status.Text=s),lifetime.Token);
            if(!await RequireFeatureAsync("dlss.configure"))return false;
            using var process=Process.Start(new ProcessStartInfo(installer){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(installer)!});
            if(process==null)throw new IOException("未能打开 Daniel 0.8 官方安装器。");
            status.Text="已打开 Daniel 0.8 官方安装器。请在 NeuralHub 中导入你自己的 DLL 并配置游戏；MU 不将打开安装器视为游戏已配置。";
            return true;
        }
        finally{busy=false;UpdateButtons();}
    }
}
