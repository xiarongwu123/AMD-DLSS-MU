using System.Diagnostics;
namespace AmdNrAssistant;

public sealed partial class MainForm
{
    async Task<bool> StartAmdMagpieAsync()
    {
        if(!await magpieOperation.WaitAsync(0))throw new IOException("大力喜鹊正在准备中，可在下载任务查看进度。");
        PublishReactState();
        try
        {
            if(!await RequireFeatureAsync("schemes.premium.configure"))return false;
            if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)||System.Runtime.InteropServices.RuntimeInformation.OSArchitecture!=System.Runtime.InteropServices.Architecture.X64)
                throw new IOException("AMD 大力喜鹊需要 Windows x64。");
            UpdateButtons();
            void Phase(string message){if(!busy&&!smartApplying)status.Text=message;magpieStatus.Text=message;}
            Phase("正在准备 AMD 大力喜鹊，首次自动下载完整包…");
            magpieProgress.Visible=true;magpieProgress.Indeterminate=true;
            using var session=BeginDownloadSession("AMD 大力喜鹊",StartAmdMagpieAsync,"schemes.premium.configure",independent:true);
            try
            {
                var running=Process.GetProcessesByName("Magpie");
                try{if(running.Length>0)throw new IOException("Magpie 已在运行，请切回工具窗口；如需切换 N卡版与 A卡版，请先从托盘退出已有 Magpie。");}
                finally{foreach(var item in running)item.Dispose();}
                var exe=await AmdMagpieIntegration.PrepareAsync(new Progress<string>(Phase),lifetime.Token);
                if(!await RequireFeatureAsync("schemes.premium.configure"))return false;
                Phase("正在打开 AMD 大力喜鹊…");
                using var process=Process.Start(new ProcessStartInfo(exe){WorkingDirectory=Path.GetDirectoryName(exe)!,UseShellExecute=true});
                if(process==null)throw new IOException("未能启动 AMD 大力喜鹊。");
                magpieProgress.Indeterminate=false;magpieProgress.Value=100;
                Phase("已打开 AMD 大力喜鹊，已准备 MU-AMD-DLSS5 效果组。请在工具内确认选择该组，切回窗口游戏按其主页快捷键启用；打开工具不代表效果已生效。");
                session.Report("已完成 · 已打开工具",100,AmdMagpieIntegration.Size,"完整包与配套组件已校验");
                return true;
            }
            catch(OperationCanceledException)
            {Phase("AMD 大力喜鹊准备已取消，再次点击可继续。");session.Report("已取消",session.Percent,AmdMagpieIntegration.Size,magpieStatus.Text);return false;}
            catch(UnauthorizedAccessException e)
            {
                var message="无法访问 AMD 大力喜鹊安装目录，请退出已有工具并检查该目录的写入权限或 Windows 安全中心的拦截记录："+AmdMagpieIntegration.Root;
                Phase(message);session.Report("下载失败",session.Percent,AmdMagpieIntegration.Size,message);throw new IOException(message,e);
            }
            catch(Exception e)
            {Phase("AMD 大力喜鹊开启失败："+e.Message);session.Report("下载失败",session.Percent,AmdMagpieIntegration.Size,e.Message);throw;}
            finally{magpieProgress.Indeterminate=false;magpieProgress.Visible=false;}
        }
        finally{magpieOperation.Release();UpdateButtons();PublishReactState();}
    }
}
