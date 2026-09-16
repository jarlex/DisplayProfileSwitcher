namespace DisplayProfileSwitcher;

internal enum DiagnosticCategory
{
    Hotkey,
    Configuration,
    Gamma,
    Nvidia
}

internal static class Diagnostics
{
    private const int MaxLogMessageLength = 500;
    private const int MaxStatusLength = 220;

    public static string FormatException(DiagnosticCategory category, string action)
    {
        return category switch
        {
            DiagnosticCategory.Hotkey => $"Atajos: no se pudo {action}. Revisa que la combinación sea válida y no esté en uso.",
            DiagnosticCategory.Configuration => $"Configuración: no se pudo {action}. Comprueba los permisos de AppData e inténtalo de nuevo.",
            DiagnosticCategory.Gamma => $"Pantalla: no se pudo {action}. Comprueba que Windows detecte el monitor.",
            DiagnosticCategory.Nvidia => $"NVIDIA: no se pudo {action}. Comprueba el controlador y que NVIDIA esté disponible.",
            _ => $"No se pudo {action}."
        };
    }

    public static string FormatHotkeyStatus(IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
            return "Atajos: registrados correctamente.";

        var message = "Atajos: " + string.Join(" ", errors);
        return Limit(message, MaxStatusLength);
    }

    public static string FormatApplyError(DiagnosticCategory category)
    {
        return category switch
        {
            DiagnosticCategory.Gamma => "Pantalla: no se pudo aplicar la gamma; comprueba los monitores activos.",
            DiagnosticCategory.Nvidia => "NVIDIA: no se pudo aplicar Digital Vibrance; comprueba el controlador.",
            _ => "No se pudo aplicar este componente."
        };
    }

    public static void Log(DiagnosticCategory category, string operation, Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DisplayProfileSwitcher");
            Directory.CreateDirectory(directory);
            var detail = $"{DateTimeOffset.Now:O} [{category}] {operation}: {exception.GetType().Name}: {Clean(exception.Message)}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, "diagnostics.log"), detail);
        }
        catch
        {
            // Diagnostics must never change application behavior.
        }
    }

    public static string Limit(string value, int maxLength = MaxStatusLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static string Clean(string value)
    {
        var clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return Limit(clean, MaxLogMessageLength);
    }
}
