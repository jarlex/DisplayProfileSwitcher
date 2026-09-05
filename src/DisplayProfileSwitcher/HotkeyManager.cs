using System.Runtime.InteropServices;

namespace DisplayProfileSwitcher;

internal sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _handle;
    private readonly Dictionary<int, DisplayProfile> _registered = new();
    private int _nextId = 1000;

    public HotkeyManager(IntPtr handle) => _handle = handle;

    public IReadOnlyDictionary<int, DisplayProfile> Registered => _registered;

    public List<string> RegisterProfiles(IEnumerable<DisplayProfile> profiles)
    {
        UnregisterAll();
        var errors = new List<string>();

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Hotkey))
                continue;

            if (!TryParse(profile.Hotkey, out var modifiers, out var key))
            {
                errors.Add($"{profile.Name}: atajo inválido '{profile.Hotkey}'.");
                continue;
            }

            var id = _nextId++;
            if (!RegisterHotKey(_handle, id, modifiers | ModNoRepeat, key))
            {
                errors.Add($"{profile.Name}: Windows no pudo registrar '{profile.Hotkey}'. Puede estar usado por otra aplicación.");
                continue;
            }

            _registered[id] = profile;
        }

        return errors;
    }

    public DisplayProfile? Resolve(int id) => _registered.TryGetValue(id, out var profile) ? profile : null;

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys.ToArray())
            UnregisterHotKey(_handle, id);
        _registered.Clear();
    }

    public void Dispose() => UnregisterAll();

    private static bool TryParse(string value, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var raw in parts)
        {
            var part = raw.ToUpperInvariant();
            switch (part)
            {
                case "CTRL":
                case "CONTROL": modifiers |= ModControl; break;
                case "ALT": modifiers |= ModAlt; break;
                case "SHIFT": modifiers |= ModShift; break;
                case "WIN":
                case "WINDOWS": modifiers |= ModWin; break;
                default:
                    if (!TryParseKey(part, out key))
                        return false;
                    break;
            }
        }

        return key != 0;
    }

    private static bool TryParseKey(string part, out uint key)
    {
        key = 0;

        if (part.Length == 1)
        {
            var c = part[0];
            if (c is >= 'A' and <= 'Z' || c is >= '0' and <= '9')
            {
                key = c;
                return true;
            }
        }

        if (part.StartsWith('F') && int.TryParse(part[1..], out var f) && f is >= 1 and <= 24)
        {
            key = (uint)(0x70 + f - 1); // VK_F1
            return true;
        }

        return part switch
        {
            "UP" => Set(0x26, out key),
            "DOWN" => Set(0x28, out key),
            "LEFT" => Set(0x25, out key),
            "RIGHT" => Set(0x27, out key),
            "HOME" => Set(0x24, out key),
            "END" => Set(0x23, out key),
            "PGUP" => Set(0x21, out key),
            "PGDN" => Set(0x22, out key),
            _ => false
        };
    }

    private static bool Set(uint value, out uint target)
    {
        target = value;
        return true;
    }

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
