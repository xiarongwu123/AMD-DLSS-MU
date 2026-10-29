using System.Text.Json;

namespace AmdNrAssistant;

// Retain user confirmations separately from mutable UI fields so download retries
// can run the same preflight against a freshly detected environment.
public sealed record InstallationContext(string Scheme = "", string ConfirmedApi = "",
    string ConfirmedInput = "", string Windowed = "", string QualityRoute = "auto")
{
    public static InstallationContext Read(JsonElement args)
    {
        string Text(string key) => args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
        return new(Text("scheme"), Text("confirmedApi"), Text("confirmedInput"), Text("windowed"), Text("qualityRoute")==""?"auto":Text("qualityRoute"));
    }

    public JsonElement Preflight(int mode, NeuralOptions options, string bridgeApi)
    {
        var scheme = Scheme.Length == 0 || Scheme == "game"
            ? mode switch { 4 => "fps", 5 or 8 => "legacy", 2 => "standard", _ => "compat" }
            : Scheme;
        return JsonSerializer.SerializeToElement(new
        {
            mode, scheme, neural = options,
            confirmedApi = ConfirmedApi.Length > 0 ? ConfirmedApi
                : bridgeApi != "auto" ? bridgeApi : options.GraphicsApi != "auto" ? options.GraphicsApi : "",
            confirmedInput = ConfirmedInput, windowed = Windowed, qualityRoute = QualityRoute
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public NeuralOptions ConfirmNeuralApi(NeuralOptions options) => options.GraphicsApi == "auto" &&
        ConfirmedApi is "dx11" or "dx12" or "vulkan" ? options with { GraphicsApi = ConfirmedApi } : options;

    public string ConfirmBridgeApi(int mode, string requested) => BridgeApi(mode,
        requested == "auto" && ConfirmedApi is "dx8" or "dx9" or "dx11" or "opengl" ? ConfirmedApi : requested);

    public static string BridgeApi(int mode, string api)
    {
        // DX12/Vulkan belong to the modern routes, never the ReShade bridge.
        if (mode is not (5 or 8)) return "auto";
        if (api is not ("auto" or "dx8" or "dx9" or "dx11" or "opengl"))
            throw new IOException("未知老游戏桥接图形接口。");
        return api;
    }
}
