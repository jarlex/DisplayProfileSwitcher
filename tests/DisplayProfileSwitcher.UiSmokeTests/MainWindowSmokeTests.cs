using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using NUnit.Framework;

namespace DisplayProfileSwitcher.UiSmokeTests;

[TestFixture]
public sealed class MainWindowSmokeTests
{
    private const string ExecutableEnvironmentVariable = "DISPLAY_PROFILE_SWITCHER_EXE";

    [Test]
    public void MainWindow_exposes_enabled_essential_controls()
    {
        Assume.That(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "The WinForms UI smoke test runs only on Windows.");

        var executablePath = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
        Assert.That(executablePath, Is.Not.Null.And.Not.Empty,
            $"{ExecutableEnvironmentVariable} must point to the published win-x64 executable.");
        executablePath = Path.GetFullPath(executablePath!);
        Assert.That(File.Exists(executablePath), Is.True,
            $"Published executable was not found at '{executablePath}'. " +
            $"Publish the win-x64 app before running this test.");

        Application? application = null;
        try
        {
            application = Application.Launch(executablePath);
            using var automation = new UIA3Automation();
            var window = application.GetMainWindow(automation, TimeSpan.FromSeconds(15));
            Assert.That(window, Is.Not.Null,
                $"The application launched from '{executablePath}', but no main window appeared " +
                $"within 15 seconds (process id {application.ProcessId}).");

            Assert.That(window!.Title, Is.EqualTo("Display Profile Switcher"),
                "The published application did not expose the expected main window title.");
            AssertReady(window, ControlType.List, "profiles list");

            foreach (var buttonName in new[] { "Añadir", "Borrar", "Guardar perfil", "Aplicar ahora" })
                AssertReady(window, ControlType.Button, $"button '{buttonName}'", buttonName);

            foreach (var label in new[] { "Nombre", "Gamma", "Brillo (50 = neutro)", "Contraste (50 = neutro)",
                                          "Digital Vibrance", "Atajo global", "Monitores", "Reaplicar", "Opciones", "Estado" })
                AssertReady(window, ControlType.Text, $"label '{label}'", label);

            AssertAtLeastReady(window, ControlType.Edit, 2, "text input controls");
            AssertAtLeastReady(window, ControlType.Spinner, 5, "numeric input controls");

            AssertReady(window, ControlType.CheckBox, "apply-last-profile option",
                "Aplicar el último perfil al iniciar");
            AssertReady(window, ControlType.CheckBox, "Windows startup option", "Iniciar con Windows");
        }
        finally
        {
            if (application is not null)
            {
                application.Close(killIfCloseFails: true);
                if (!application.HasExited)
                    application.Kill();
                application.Dispose();
            }
        }
    }

    private static void AssertReady(AutomationElement window, ControlType controlType, string description,
        string? name = null)
    {
        var control = window.FindFirstDescendant(cf =>
            name is null
                ? cf.ByControlType(controlType)
                : cf.ByControlType(controlType).And(cf.ByName(name)));

        Assert.That(control, Is.Not.Null, $"Required {description} was not found in the main window.");
        Assert.That(control!.IsEnabled, Is.True, $"Required {description} is disabled.");
        Assert.That(control.IsOffscreen, Is.False, $"Required {description} is not visible.");
    }

    private static void AssertAtLeastReady(AutomationElement window, ControlType controlType, int minimum,
        string description)
    {
        var controls = window.FindAllDescendants(cf => cf.ByControlType(controlType));
        Assert.That(controls.Length, Is.GreaterThanOrEqualTo(minimum),
            $"Expected at least {minimum} {description}, found {controls.Length}.");
        foreach (var control in controls)
        {
            Assert.That(control.IsEnabled, Is.True, $"A required {description} control is disabled.");
            Assert.That(control.IsOffscreen, Is.False, $"A required {description} control is not visible.");
        }
    }
}
