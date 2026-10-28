using AmdNrAssistant;

// Runs real pinned staging files, never executes a Windows installer on the test
// host and never treats fixtures as evidence of GPU execution or completed setup.
internal static class Rx6000InstallValidation
{
    public static void Run(string installer, string modelSource, string destination)
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
        Core.ValidateInstaller(installer);
        Check(new FileInfo(installer).Length == Core.ReviewedInstallerSize && Core.Hash(installer) == Core.ReviewedInstallerSha256, "real Daniel 0.6.0 installer exact size and SHA256");
        Core.CheckPe(modelSource, true);
        if (OperatingSystem.IsWindows()) Core.ValidateDll(modelSource);
        else Console.WriteLine("SKIP Windows FileVersionInfo: this host cannot read native Windows PE version fields; exact pinned SHA256 is checked instead.");
        Check(Core.Hash(modelSource) == Core.BundledDllSha256, "real bundled NVIDIA 310.8.0 model source exact SHA256");
        Check(RenderSchemes.PremiumSelection(0, "rx6000") && Core.ReviewedTag == "v0.6.0", "RX6000 route requires premium and pins Daniel 0.6.0");
        var root = Path.Combine(Path.GetFullPath(destination), Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
        var exe = Path.Combine(game, "fixture.exe"); var pe = new byte[512]; pe[0] = 0x4d; pe[1] = 0x5a;
        BitConverter.GetBytes(0x80).CopyTo(pe, 0x3c); pe[0x80] = 0x50; pe[0x81] = 0x45;
        BitConverter.GetBytes((ushort)0x8664).CopyTo(pe, 0x84); File.WriteAllBytes(exe, pe);
        var before = Core.Hash(exe);
        var originalRoot = GameManagement.StorageOverride; GameManagement.StorageOverride = Path.Combine(root, "records");
        try
        {
            GameManagement.Begin(exe, 0);
            GameManagement.Save(GameManagement.Read(exe)! with { Scheme = "rx6000" });
            var state = Path.Combine(root, "stage");
            Core.Stage(game, installer, modelSource, state, Core.ReviewedTag);
            Check(Core.Hash(Path.Combine(game, Core.InstallerName)) == Core.ReviewedInstallerSha256, "pinned installer staged into selected game directory unchanged");
            Check(Core.Hash(Path.Combine(game, Core.DllName)) == Core.BundledDllSha256, "pinned model source staged unchanged");
            Check(GameManagement.Read(exe) is { Mode: 0, Scheme: "rx6000", Version: "v0.6.0", Phase: "installing" }, "dedicated scheme survives serialized installation record");
            var incomplete = Core.CheckInstalled(game);
            Check(!incomplete.Ready && !incomplete.HasConfig && !incomplete.HasWeights, "staging or closing installer without setup cannot count as installed");
            var start = UpstreamInstaller.CreateStartInfo(Path.Combine(game, Core.InstallerName), exe);
            Check(start.WorkingDirectory == game && start.ArgumentList.Count == 0 && !start.RedirectStandardInput && !start.CreateNoWindow,
                "official GUI is visible, targets selected folder and does not receive invented silent flags");
            Core.Rollback(state);
            GameManagement.Finish(exe, false);
            Check(GameManagement.Read(exe) is { Phase: "incomplete", Scheme: "rx6000" }, "interrupted preparation is not saved as installed");
            GameManagement.Restore(exe);
            Check(GameManagement.Read(exe) is { Phase: "restored", Scheme: "rx6000" }, "recovery preserves scheme identity");
            Check(Core.Hash(exe) == before && Directory.GetFiles(game).SequenceEqual(new[] { exe }), "recovery removes staged files and preserves game EXE exactly");
            Console.WriteLine($"RX6000 real-file preparation: {count} checks passed. Official GUI completion, HIP execution and game rendering NOT tested.");
        }
        finally { GameManagement.StorageOverride = originalRoot; }
    }
}
