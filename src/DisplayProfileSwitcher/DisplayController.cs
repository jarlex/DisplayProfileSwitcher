using NvAPIWrapper.Display;
using NvDisplay = NvAPIWrapper.Display.Display;
using System.Runtime.InteropServices;

namespace DisplayProfileSwitcher;

internal sealed class DisplayController
{
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
                catch (Exception ex) { errors.Add($"Monitor {i + 1}: {ex.Message}"); }
            }
            return new ApplyComponentResult("Gamma", errors.Count == 0, applied, errors);
        }
        catch (Exception ex) { return new ApplyComponentResult("Gamma", false, 0, new[] { ex.Message }); }
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
                catch (Exception ex) { errors.Add($"Monitor NVIDIA {i + 1}: {ex.Message}"); }
            }
            return new ApplyComponentResult("Digital Vibrance", errors.Count == 0, applied, errors);
        }
        catch (Exception ex) { return new ApplyComponentResult("Digital Vibrance", false, 0, new[] { ex.Message }); }
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

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr initData);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRamp(IntPtr deviceContext, ushort[] ramp);

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
