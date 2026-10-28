using AmdNrAssistant;
internal static class RenderSchemeTests
{
    public static void Run()
    {
        var count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
        void Reject(Action action,string name){try{action();}catch(IOException){count++;return;}throw new Exception(name);}
        Check(!RenderSchemes.PremiumMode(0)&&!RenderSchemes.PremiumMode(4),"default configuration modes must stay free");
        Check(RenderSchemes.PremiumMode(5)&&RenderSchemes.PremiumMode(6)&&RenderSchemes.PremiumMode(8),"bridge and multi-backend modes must require premium on direct RPC as well");
        Check(RenderSchemes.Paid.Count(s=>s!="game")==6,"catalog must have six premium schemes plus the protected game recommendation action");
        Check(RenderSchemes.PremiumSelection(0,"rx6000"),"RX6000 shared installer must require premium despite original mode ID");
        Check(!RenderSchemes.PremiumSelection(0,"base")&&!RenderSchemes.PremiumSelection(0,""),"original free Daniel routes changed");
        RenderSchemes.Validate("rx6000",0,false);
        Reject(()=>RenderSchemes.Validate("rx6000",0,true),"RX6000 accepts an incompatible x86 game before installation");
        Reject(()=>RenderSchemes.Validate("rx6000",6,false),"RX6000 uses multi-backend installer instead of fixed Daniel 0.6");
        RenderSchemes.Validate("fps",4,false);RenderSchemes.Validate("legacy",5,true);RenderSchemes.Validate("legacy",8,false);
        RenderSchemes.Validate("base",0,true);RenderSchemes.Validate("fps",4,true);RenderSchemes.Validate("compat",6,true);
        Reject(()=>RenderSchemes.Validate("fps",6,false),"premium mode disguised as free FPS scheme");
        Reject(()=>RenderSchemes.Validate("legacy",8,true),"x64 ReShade routed to a 32-bit game");
        Reject(()=>RenderSchemes.Validate("unknown",0,false),"unknown scheme accepted");
        Reject(()=>RenderSchemes.Validate("amd-magpie",6,false),"external Magpie incorrectly deployed into a game");
        Check(LegacyBridge.Proxy("dx9")=="d3d9.dll"&&LegacyBridge.Proxy("dx11")=="dxgi.dll"&&LegacyBridge.Proxy("opengl")=="opengl32.dll","legacy API proxy selection incorrect");
        Reject(()=>LegacyBridge.Proxy("dx12"),"legacy route must not guess DX12 compatibility");
        Check(GameManagement.IsTracked("amd-nr.addon64")&&GameManagement.IsTracked("ReShade.ini"),"legacy files absent from recovery ownership");
        Check(!GameManagement.IsTracked("../amd-nr.addon64"),"legacy ownership permits path traversal");
        Check(AmdMagpieIntegration.Root!=MagpieIntegration.Root&&AmdMagpieIntegration.RuntimeSha256!=X86Bridge.ReadPins().Single(p=>p.Name=="dlssnr_amd_pass1.dll").Sha256,"AMD Magpie ABI or installation mixed with standard runtime");
        Reject(()=>AmdMagpieIntegration.ValidateResources(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))),"AMD Magpie launched without matched user resources");
        Reject(()=>Daniel08Launcher.Validate(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))),"Daniel launcher accepted missing/unreviewed installer");
        Check(RenderSchemes.PreflightBackend("author",6,"rdna3","daniel")=="daniel","manual backend override ignored by author preflight");
        Check(RenderSchemes.PreflightBackend("author",6,"rdna4","auto")=="mochizuki","author default must remain Mochizuki");
        Check(RenderSchemes.PreflightBackend("compat",6,"rdna3","auto")==Mu.Neural.NeuralBackends.LmxxfRdna3,"compat RDNA3 default changed");
        Console.WriteLine($"Render scheme policies: {count} assertions passed.");
    }
}
