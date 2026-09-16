using NvAPIWrapper.Display;
using NvDisplay = NvAPIWrapper.Display.Display;
using System.Runtime.InteropServices;

namespace DisplayProfileSwitcher;

internal sealed class DisplayController
{
    public bool TryCaptureSnapshot(DisplayProfile profile, out DisplayPreviewSnapshot? snapshot, out string error)
    {
        GammaSnapshot[] gamma;
        try
        {
            var screens = SelectGammaTargets(Screen.AllScreens, profile.ApplyToAllDisplays);
            gamma = screens.Select(screen => new GammaSnapshot(screen, CaptureGammaRamp(screen))).ToArray();
        }
        catch (Exception ex)
        {
            Diagnostics.Log(DiagnosticCategory.Gamma, "capturar la gamma para la prueba", ex);
            snapshot = null;
            error = "No se puede probar este perfil de forma segura: Windows no permite capturar la gamma exacta.";
            return false;
        }

        try
        {
            var displays = NvDisplay.GetDisplays();
            if (displays.Length == 0)
                throw new InvalidOperationException("NVAPI no ha devuelto monitores NVIDIA.");
            var targets = profile.ApplyToAllDisplays ? displays : GetPrimaryNvidiaDisplay(displays);
            var nvidia = targets.Select(display => new NvidiaSnapshot(display, display.DigitalVibranceControl.CurrentLevel)).ToArray();
            snapshot = new DisplayPreviewSnapshot(gamma, nvidia);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log(DiagnosticCategory.Nvidia, "capturar Digital Vibrance para la prueba", ex);
            snapshot = null;
            error = "No se puede probar este perfil de forma segura: NVIDIA no permite capturar Digital Vibrance.";
            return false;
        }
    }

    public ApplyResult Restore(DisplayPreviewSnapshot snapshot)
    {
        var results = new List<ApplyComponentResult>();
        results.Add(RestoreGamma(snapshot.Gamma));
        results.Add(RestoreNvidia(snapshot.Nvidia));
        return new ApplyResult(results);
    }

    public ApplyResult Apply(DisplayProfile profile)
    {
        return new ApplyResult(new[] { ApplyGamma(profile), ApplyVibrance(profile) });
    }

    private static ApplyComponentResult ApplyGamma(DisplayProfile profile)
    {
        try
        {
            var displays = Screen.AllScreens;
            var targets = SelectGammaTargets(displays, profile.ApplyToAllDisplays);
            var ramp = CreateGammaRamp(profile);
            var errors = new List<string>();
            var applied = 0;
            for (var i = 0; i < targets.Length; i++)
            {
                try
                {
                    if (!ApplyGammaRamp(targets[i], ramp))
                        throw new InvalidOperationException("Windows no pudo aplicar la gamma al monitor.");
                    applied++;
                }
                catch (Exception ex)
                {
                    Diagnostics.Log(DiagnosticCategory.Gamma, "aplicar gamma a un monitor", ex);
                    errors.Add(Diagnostics.FormatApplyError(DiagnosticCategory.Gamma));
                }
            }
            return new ApplyComponentResult("Gamma", errors.Count == 0, applied, errors);
        }
        catch (Exception ex)
        {
            Diagnostics.Log(DiagnosticCategory.Gamma, "aplicar gamma", ex);
            return new ApplyComponentResult("Gamma", false, 0, new[] { Diagnostics.FormatApplyError(DiagnosticCategory.Gamma) });
        }
    }

    private static ApplyComponentResult ApplyVibrance(DisplayProfile profile)
    {
        try
        {
            var displays = NvDisplay.GetDisplays();
            if (displays.Length == 0)
                throw new InvalidOperationException("NVAPI no ha devuelto monitores NVIDIA.");
            var targets = profile.ApplyToAllDisplays ? displays : GetPrimaryNvidiaDisplay(displays);
            var errors = new List<string>();
            var applied = 0;
            var level = Math.Clamp(profile.Vibrance, 0, 100);
            for (var i = 0; i < targets.Length; i++)
            {
                try { targets[i].DigitalVibranceControl.CurrentLevel = level; applied++; }
                catch (Exception ex)
                {
                    Diagnostics.Log(DiagnosticCategory.Nvidia, "aplicar Digital Vibrance a un monitor", ex);
                    errors.Add(Diagnostics.FormatApplyError(DiagnosticCategory.Nvidia));
                }
            }
            return new ApplyComponentResult("Digital Vibrance", errors.Count == 0, applied, errors);
        }
        catch (Exception ex)
        {
            Diagnostics.Log(DiagnosticCategory.Nvidia, "aplicar Digital Vibrance", ex);
            return new ApplyComponentResult("Digital Vibrance", false, 0, new[] { Diagnostics.FormatApplyError(DiagnosticCategory.Nvidia) });
        }
    }

    private static Screen[] SelectGammaTargets(Screen[] displays, bool allDisplays)
    {
        if (displays.Length == 0)
            throw new InvalidOperationException("Windows no ha devuelto monitores activos.");
        if (allDisplays) return displays;
        return displays.Where(d => d.Primary).DefaultIfEmpty(displays[0]).ToArray();
    }

    private static ushort[] CreateGammaRamp(DisplayProfile profile)
    {
        var ramp = new ushort[768];
        var brightness = Math.Clamp(profile.Brightness / 100f, 0f, 1f) - 0.5f;
        var contrast = Math.Clamp(profile.Contrast / 50f, 0f, 2f);
        var gamma = Math.Clamp((float)profile.Gamma, 0.30f, 2.80f);

        for (var i = 0; i < 256; i++)
        {
            var value = ((i / 255f - 0.5f) * contrast + 0.5f + brightness);
            value = Math.Clamp(value, 0f, 1f);
            var corrected = MathF.Pow(value, 1f / gamma);
            var level = (ushort)Math.Round(corrected * ushort.MaxValue);
            ramp[i] = level;
            ramp[i + 256] = level;
            ramp[i + 512] = level;
        }

        return ramp;
    }

    private static bool ApplyGammaRamp(Screen screen, ushort[] ramp)
    {
        var deviceContext = CreateDC("DISPLAY", screen.DeviceName, null, IntPtr.Zero);
        if (deviceContext == IntPtr.Zero)
            throw new InvalidOperationException("Windows no pudo crear el contexto del monitor.");

        try
        {
            return SetDeviceGammaRamp(deviceContext, ramp);
        }
        finally
        {
            DeleteDC(deviceContext);
        }
    }

    private static ushort[] CaptureGammaRamp(Screen screen)
    {
        var deviceContext = CreateDC("DISPLAY", screen.DeviceName, null, IntPtr.Zero);
        if (deviceContext == IntPtr.Zero)
            throw new InvalidOperationException("Windows no pudo crear el contexto del monitor.");

        try
        {
            var ramp = new ushort[768];
            if (!GetDeviceGammaRamp(deviceContext, ramp))
                throw new InvalidOperationException("Windows no pudo leer la gamma del monitor.");
            return ramp;
        }
        finally
        {
            DeleteDC(deviceContext);
        }
    }

    private static ApplyComponentResult RestoreGamma(IReadOnlyList<GammaSnapshot> snapshots)
    {
        var errors = new List<string>();
        var restored = 0;
        foreach (var snapshot in snapshots)
        {
            try
            {
                if (!ApplyGammaRamp(snapshot.Screen, snapshot.Ramp))
                    throw new InvalidOperationException("Windows no pudo restaurar la gamma del monitor.");
                restored++;
            }
            catch (Exception ex)
            {
                Diagnostics.Log(DiagnosticCategory.Gamma, "restaurar la gamma", ex);
                errors.Add(Diagnostics.FormatApplyError(DiagnosticCategory.Gamma));
            }
        }
        return new ApplyComponentResult("Gamma", errors.Count == 0, restored, errors);
    }

    private static ApplyComponentResult RestoreNvidia(IReadOnlyList<NvidiaSnapshot> snapshots)
    {
        var errors = new List<string>();
        var restored = 0;
        foreach (var snapshot in snapshots)
        {
            try
            {
                snapshot.Display.DigitalVibranceControl.CurrentLevel = snapshot.Level;
                restored++;
            }
            catch (Exception ex)
            {
                Diagnostics.Log(DiagnosticCategory.Nvidia, "restaurar Digital Vibrance", ex);
                errors.Add(Diagnostics.FormatApplyError(DiagnosticCategory.Nvidia));
            }
        }
        return new ApplyComponentResult("Digital Vibrance", errors.Count == 0, restored, errors);
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr initData);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRamp(IntPtr deviceContext, ushort[] ramp);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDeviceGammaRamp(IntPtr deviceContext, ushort[] ramp);

    private static NvDisplay[] GetPrimaryNvidiaDisplay(NvDisplay[] displays)
    {
        try
        {
            var paths = NvAPIWrapper.Display.PathInfo.GetDisplaysConfig();
            for (var i = 0; i < paths.Length && i < displays.Length; i++)
            {
                if (paths[i].IsGDIPrimary)
                    return new[] { displays[i] };
            }
        }
        catch
        {
            // Fall back to first NVIDIA display.
        }

        return new[] { displays[0] };
    }
}
