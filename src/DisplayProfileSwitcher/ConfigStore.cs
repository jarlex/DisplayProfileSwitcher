using System.Text.Json;

namespace DisplayProfileSwitcher;

internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DisplayProfileSwitcher");

    public string ConfigPath => Path.Combine(ConfigDirectory, "profiles.json");
    public string BackupPath => ConfigPath + ".bak";
    public string CorruptCopyPath => ConfigPath + ".corrupt";

    public ConfigLoadResult Load()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(ConfigPath))
        {
            var config = CreateDefault();
            Save(config);
            return new ConfigLoadResult(config, ConfigLoadState.Created);
        }

        try
        {
            var config = Migrate(File.ReadAllText(ConfigPath), out var migrated);
            // Persist generated IDs and normalized names so legacy JSON becomes stable after one load.
            if (migrated) Save(config);
            return new ConfigLoadResult(config, ConfigLoadState.Loaded);
        }
        catch (Exception primaryError)
        {
            try
            {
                var recovered = Migrate(File.ReadAllText(BackupPath), out _);
                SaveAtomic(recovered, createBackup: false);
                return new ConfigLoadResult(recovered, ConfigLoadState.RecoveredFromBackup,
                    $"La configuración principal estaba dañada ({primaryError.Message}) y se recuperó desde el backup.");
            }
            catch (Exception backupError)
            {
                PreserveCorruptFile();
                return new ConfigLoadResult(CreateDefault(), ConfigLoadState.Corrupt,
                    $"La configuración está dañada y no se pudo recuperar desde el backup: {backupError.Message} Se conservó una copia en {CorruptCopyPath}.");
            }
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        SaveAtomic(config, createBackup: true);
    }

    private void SaveAtomic(AppConfig config, bool createBackup)
    {
        var temporaryPath = ConfigPath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(config, JsonOptions));
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(ConfigPath) && createBackup)
                File.Replace(temporaryPath, ConfigPath, BackupPath, ignoreMetadataErrors: true);
            else
            {
                if (File.Exists(ConfigPath))
                    File.Move(temporaryPath, ConfigPath, overwrite: true);
                else
                    File.Move(temporaryPath, ConfigPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static AppConfig Migrate(string json, out bool migrated)
    {
        migrated = false;
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
            ?? throw new JsonException("El documento JSON está vacío.");
        config.Profiles ??= new List<DisplayProfile>();
        var usedIds = new HashSet<Guid>();
        foreach (var profile in config.Profiles)
        {
            if (profile.Id == Guid.Empty || !usedIds.Add(profile.Id))
            {
                migrated = true;
                do profile.Id = Guid.NewGuid(); while (!usedIds.Add(profile.Id));
            }
            var normalizedName = string.IsNullOrWhiteSpace(profile.Name) ? "Perfil" : profile.Name.Trim();
            migrated |= profile.Name != normalizedName;
            profile.Name = normalizedName;
        }
        if (config.Profiles.Count == 0)
        {
            config.Profiles = CreateDefault().Profiles;
            migrated = true;
        }
        migrated |= EnsureUniqueNames(config.Profiles);
        if (!config.LastProfileId.HasValue && !string.IsNullOrWhiteSpace(config.LastProfileName))
        {
            config.LastProfileId = config.Profiles.FirstOrDefault(p =>
                p.Name.Equals(config.LastProfileName, StringComparison.OrdinalIgnoreCase))?.Id;
            migrated = true;
        }
        return config;
    }

    private void PreserveCorruptFile()
    {
        try
        {
            if (!File.Exists(CorruptCopyPath))
                File.Copy(ConfigPath, CorruptCopyPath);
        }
        catch
        {
            // The original file remains untouched when preservation is not possible.
        }
    }

    private static bool EnsureUniqueNames(IList<DisplayProfile> profiles)
    {
        var changed = false;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            var baseName = profile.Name;
            var name = baseName;
            var suffix = 2;
            while (!used.Add(name)) { name = $"{baseName} {suffix++}"; changed = true; }
            profile.Name = name;
        }
        return changed;
    }

    private static AppConfig CreateDefault() => new()
    {
        ApplyLastProfileOnStartup = true,
        Profiles =
        {
            new DisplayProfile
            {
                Name = "Normal",
                Gamma = 1.00m,
                Brightness = 50,
                Contrast = 50,
                Vibrance = 50,
                Hotkey = "Ctrl+Alt+1",
                ApplyToAllDisplays = true
            },
            new DisplayProfile
            {
                Name = "Claro",
                Gamma = 1.35m,
                Brightness = 55,
                Contrast = 55,
                Vibrance = 65,
                Hotkey = "Ctrl+Alt+2",
                ApplyToAllDisplays = true,
                ReapplySeconds = 5
            },
            new DisplayProfile
            {
                Name = "Noche",
                Gamma = 1.15m,
                Brightness = 42,
                Contrast = 50,
                Vibrance = 45,
                Hotkey = "Ctrl+Alt+3",
                ApplyToAllDisplays = true
            }
        }
    };
}
