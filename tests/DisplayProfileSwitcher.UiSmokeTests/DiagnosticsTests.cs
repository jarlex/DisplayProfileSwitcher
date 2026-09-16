using NUnit.Framework;

namespace DisplayProfileSwitcher.UiSmokeTests;

[TestFixture]
public sealed class DiagnosticsTests
{
    [TestCase("Configuration", "guardar el perfil", "Configuración:")]
    [TestCase("Gamma", "aplicar gamma", "Pantalla:")]
    [TestCase("Nvidia", "aplicar Digital Vibrance", "NVIDIA:")]
    [TestCase("Hotkey", "registrar los atajos", "Atajos:")]
    public void FormatException_uses_actionable_category(string categoryName, string action, string prefix)
    {
        var category = Enum.Parse<DiagnosticCategory>(categoryName);
        var message = Diagnostics.FormatException(category, action);

        Assert.That(message, Does.StartWith(prefix));
        Assert.That(message, Does.Contain(action));
        Assert.That(message.Length, Is.LessThan(220));
    }

    [Test]
    public void FormatHotkeyStatus_limits_long_registration_errors()
    {
        var message = Diagnostics.FormatHotkeyStatus(new[] { new string('x', 500) });

        Assert.That(message.Length, Is.LessThanOrEqualTo(220));
        Assert.That(message, Does.StartWith("Atajos:"));
    }

    [Test]
    public void Log_is_best_effort_and_does_not_throw()
    {
        Assert.DoesNotThrow(() => Diagnostics.Log(
            DiagnosticCategory.Configuration, "test", new InvalidOperationException("safe diagnostic")));
    }
}
