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

    public static bool TryFormat(Keys modifiers, Keys key, out string value)
    {
        value = string.Empty;
        if (IsModifierKey(key))
            return false;

        var keyName = key switch
        {
            >= Keys.A and <= Keys.Z => key.ToString().ToUpperInvariant(),
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + ((int)key - (int)Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => $"NUMPAD{(int)key - (int)Keys.NumPad0}",
            >= Keys.F1 and <= Keys.F24 => key.ToString().ToUpperInvariant(),
            Keys.Up => "UP",
            Keys.Down => "DOWN",
            Keys.Left => "LEFT",
            Keys.Right => "RIGHT",
            Keys.Home => "HOME",
            Keys.End => "END",
            Keys.PageUp => "PGUP",
            Keys.PageDown => "PGDN",
            Keys.Escape => "ESCAPE",
            Keys.Delete => "DELETE",
            Keys.Insert => "INSERT",
            Keys.Space => "SPACE",
            _ => null
        };

        if (keyName is null)
            return false;

        var parts = new List<string>();
        if (modifiers.HasFlag(Keys.Control)) parts.Add("CTRL");
        if (modifiers.HasFlag(Keys.Alt)) parts.Add("ALT");
        if (modifiers.HasFlag(Keys.Shift)) parts.Add("SHIFT");
        if (modifiers.HasFlag(Keys.LWin) || modifiers.HasFlag(Keys.RWin)) parts.Add("WIN");
        parts.Add(keyName);
        value = string.Join('+', parts);
        return true;
    }

    public static bool IsModifierKey(Keys key) => key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
        or Keys.LWin or Keys.RWin or Keys.Control or Keys.Shift or Keys.Alt;

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
            "ESC" or "ESCAPE" => Set(0x1B, out key),
            "DEL" or "DELETE" => Set(0x2E, out key),
            "INS" or "INSERT" => Set(0x2D, out key),
            "SPACE" => Set(0x20, out key),
            "NUMPAD0" => Set(0x60, out key),
            "NUMPAD1" => Set(0x61, out key),
            "NUMPAD2" => Set(0x62, out key),
            "NUMPAD3" => Set(0x63, out key),
            "NUMPAD4" => Set(0x64, out key),
            "NUMPAD5" => Set(0x65, out key),
            "NUMPAD6" => Set(0x66, out key),
            "NUMPAD7" => Set(0x67, out key),
            "NUMPAD8" => Set(0x68, out key),
            "NUMPAD9" => Set(0x69, out key),
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
