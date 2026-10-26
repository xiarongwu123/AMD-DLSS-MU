namespace Mu.Recommendation;

public static class CompatibilityRuleEngine
{
    public static bool Match(SmartCompatibilityRule r, EnvironmentSnapshot e, DateTimeOffset now, bool includeDenied=false)
    {
        if(r.Disabled||r.ExpiresUtc<=now||r.VerifiedAt>now||r.GameId!=e.GameId||(!includeDenied&&r.Deny))return false;
        bool Fits(string[] values,string field)=>values.Length==0||e.Field(field).Known&&values.Contains(e.Field(field).Value,StringComparer.OrdinalIgnoreCase);
        if(!Fits(r.GameVersions,"gameVersion")||!Fits(r.BuildHashes,"gameBuild")||!Fits(r.GpuModels,"gpuModel")||
            !Fits(r.DeviceIds,"deviceId")||!Fits(r.Architectures,"architecture"))return false;
        if(e.Field("api").Known&&r.Api!=e.Field("api").Value)return false;
        if(e.Field("bitness").Known&&r.Bitness.ToString()!=e.Field("bitness").Value)return false;
        if(r.DriverMin!=null||r.DriverMax!=null)
        {
            if(!Version.TryParse(e.Field("driver").Value,out var v))return false;
            if(r.DriverMin!=null&&v<Version.Parse(r.DriverMin)||r.DriverMax!=null&&v>Version.Parse(r.DriverMax))return false;
        }
        return true;
    }
    public static bool Exact(SmartCompatibilityRule r,EnvironmentSnapshot e) =>
        (r.GameVersions.Length>0&&e.Field("gameVersion").Confirmed||r.BuildHashes.Length>0&&e.Field("gameBuild").Confirmed)&&
        (r.GpuModels.Length>0&&e.Field("gpuModel").Confirmed||r.DeviceIds.Length>0&&e.Field("deviceId").Confirmed)&&
        (r.DriverMin!=null||r.DriverMax!=null)&&e.Field("driver").Confirmed&&e.Field("api").Confirmed&&e.Field("bitness").Confirmed;
    public static int Priority(SmartCompatibilityRule r,EnvironmentSnapshot e) =>
        r.Deny?1000: Exact(r,e)?900:r.GameVersions.Length+r.BuildHashes.Length>0?800:r.GpuModels.Length+r.DeviceIds.Length>0?700:r.Validation=="community"?600:r.Architectures.Length>0?500:400;
}
