using NvAPIWrapper;

namespace DisplayProfileSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(true, "Local\\DisplayProfileSwitcher.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Display Profile Switcher ya está ejecutándose.", "Display Profile Switcher",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            NVIDIA.Initialize();
        }
        catch
        {
            // Gamma/brightness/contrast still work without NVAPI.
        }

        Application.Run(new MainForm());
    }
}
