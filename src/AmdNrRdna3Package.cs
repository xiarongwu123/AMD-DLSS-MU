using System.Text.Json;
using Mu.Neural;

namespace AmdNrAssistant;

// Keep AMDNR's host, runtime and authenticated pak together. This is a separate
// package from TheAutomatic's ABI; its DLL must never be mixed into that host.
public static class AmdNrRdna3Package
{
    internal static NeuralRenderInstaller.PackagePin? PinOverride { get; set; }

    public static NeuralRenderInstaller.PackagePin Pin()
    {
        if (PinOverride is { } fixture) return fixture;
        using var stream = typeof(AmdNrRdna3Package).Assembly.GetManifestResourceStream("Mu.AmdNrRdna3.Pins")
            ?? throw new IOException("RX 7000 神经渲染资源清单缺失。");
        return JsonSerializer.Deserialize<NeuralRenderInstaller.PackagePin>(stream, NeuralRules.Json)
            ?? throw new IOException("RX 7000 神经渲染资源清单无效。");
    }

    public static bool PackagePath(string name) =>
        name is "OptiScaler.dll" or "OptiScaler.ini" or "LmxxfNrRuntime.dll" or "LmxxfNrRuntime.pak" or
            "LICENSE" or "README.md" or "KNOWN-LIMITS.md" ||
        name.StartsWith("OptiScaler/", StringComparison.Ordinal) || name.StartsWith("Licenses/", StringComparison.Ordinal);

    public static string InstalledName(string name, string proxy) => name switch
    {
        "OptiScaler.dll" => proxy,
        "LICENSE" => "Licenses/AMDNR-LICENSE.txt",
        "README.md" => "Licenses/AMDNR-README.md",
        "KNOWN-LIMITS.md" => "Licenses/AMDNR-KNOWN-LIMITS.md",
        _ => name
    };
}
