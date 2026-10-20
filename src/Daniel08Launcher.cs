namespace AmdNrAssistant;

// The user's machine downloads Daniel's original installer from the official
// release. No bundled payload, extraction, repackaging or undocumented CLI.
public static class Daniel08Launcher
{
    public const string Version = "v0.8.0";
    public const string FileName = "NeuralHub_v0.8.0_setup.exe";
    public const long Size = 50335680;
    public const string Sha256 = "aae53654001bf7345dc5b2e621a3f9018f4a5735760a1572a7532180249b0834";
    public const string Url = "https://github.com/danielblnc/DLSS-NR-on-AMD/releases/download/v0.8.0/" + FileName;
    public static void Validate(string path)
    {
        Core.RejectLinks(path);
        if(!File.Exists(path)||new FileInfo(path).Length!=Size||Core.Hash(path)!=Sha256)
            throw new IOException("Daniel 0.8 官方安装器校验失败，未启动。");
        Core.CheckPeMachine(path,false,true);
    }
    public static async Task<string> PrepareAsync(IProgress<string> progress,CancellationToken token)
    {
        var directory=Path.Combine(Core.InstallerCacheDirectory,"NeuralHub",Version);Core.RejectLinks(directory);Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,FileName);Core.RejectLinks(file);
        using var gate=new FileStream(Path.Combine(directory,"download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(!File.Exists(file)||new FileInfo(file).Length!=Size||Core.Hash(file)!=Sha256)
        {
            using var http=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};
            await DownloadSources.DownloadAsync(http,Url,"https://api.github.com/repos/danielblnc/DLSS-NR-on-AMD/releases/assets/651284456",
                Sha256,Size,file,new Progress<int>(n=>progress.Report($"Daniel 0.8 官方安装器下载 {n}%")),token,progress.Report);
        }
        token.ThrowIfCancellationRequested();Validate(file);return file;
    }
}
