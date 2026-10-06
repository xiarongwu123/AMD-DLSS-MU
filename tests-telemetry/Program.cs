using AmdNrAssistant;

var path = Path.Combine(Path.GetTempPath(), "mu-telemetry-test-" + Guid.NewGuid().ToString("N") + ".csv");
try
{
    File.WriteAllText(path, "Application,PresentRuntime,MsBetweenDisplayChange,MsBetweenPresents,MsGPUBusy,MsCPUBusy\n" +
        "game.exe,DXGI,10,8,6,4\n" +
        "game.exe,DXGI,20,8,8,5\n" +
        "game.exe,DXGI,NA,8,8,5\n");
    var result = PresentMonCsv.Summarize(path);
    if (result.FrameCount != 2 || Math.Abs(result.DurationMs - 30) > .001 ||
        result.Runtime != "DXGI" || result.FpsSource != "display_change" || Math.Abs(result.AverageGpuBusyMs!.Value - 7) > .001 ||
        Math.Abs(result.AverageCpuBusyMs!.Value - 4.5) > .001)
        throw new Exception("PresentMon display-frame parsing failed");
    File.WriteAllText(path, "Application,MsBetweenPresents\ngame.exe,16.6667\ngame.exe,16.6667\n");
    result = PresentMonCsv.Summarize(path);
    if (result.FrameCount != 2 || result.FpsSource != "present_interval" || Math.Abs(result.DurationMs - 33.3334) > .001)
        throw new Exception("PresentMon present-frame fallback failed");
    Console.WriteLine("Telemetry parser passed: 2 scenarios.");
}
finally { File.Delete(path); }
