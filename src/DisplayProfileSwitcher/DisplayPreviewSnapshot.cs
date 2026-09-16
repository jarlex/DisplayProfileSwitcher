using NvDisplay = NvAPIWrapper.Display.Display;

namespace DisplayProfileSwitcher;

internal sealed record GammaSnapshot(Screen Screen, ushort[] Ramp);
internal sealed record NvidiaSnapshot(NvDisplay Display, int Level);

internal sealed class DisplayPreviewSnapshot
{
    public DisplayPreviewSnapshot(IReadOnlyList<GammaSnapshot> gamma, IReadOnlyList<NvidiaSnapshot> nvidia)
    {
        Gamma = gamma;
        Nvidia = nvidia;
    }

    public IReadOnlyList<GammaSnapshot> Gamma { get; }
    public IReadOnlyList<NvidiaSnapshot> Nvidia { get; }
}
