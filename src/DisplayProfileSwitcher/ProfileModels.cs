namespace DisplayProfileSwitcher;

public sealed class DisplayProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Nuevo perfil";
    public decimal Gamma { get; set; } = 1.00m;
    public int Brightness { get; set; } = 50; // 50 = neutral
    public int Contrast { get; set; } = 50;   // 50 = neutral
    public int Vibrance { get; set; } = 50;   // NVIDIA default
    public string Hotkey { get; set; } = string.Empty;
    public bool ApplyToAllDisplays { get; set; } = true;
    public int ReapplySeconds { get; set; } = 0;
}

public sealed class AppConfig
{
    public List<DisplayProfile> Profiles { get; set; } = new();
    public Guid? LastProfileId { get; set; }
    // Kept for compatibility with configurations written before stable profile IDs.
    public string? LastProfileName { get; set; }
    public bool ApplyLastProfileOnStartup { get; set; } = true;
    public bool RunAtWindowsStartup { get; set; } = false;
}

internal enum ConfigLoadState { Loaded, Created, RecoveredFromBackup, Corrupt }

internal sealed record ConfigLoadResult(AppConfig Config, ConfigLoadState State, string? Message = null);

internal sealed record ApplyComponentResult(
    string Component, bool Success, int AffectedMonitors, IReadOnlyList<string> Errors);

internal sealed record ApplyResult(IReadOnlyList<ApplyComponentResult> Components)
{
    public bool Success => Components.All(component => component.Success);
    public bool HasPartialFailure => Components.Any(component => component.Success)
        && Components.Any(component => !component.Success);
}
