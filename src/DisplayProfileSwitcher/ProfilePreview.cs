namespace DisplayProfileSwitcher;

internal enum ProfilePreviewState
{
    Inactive,
    Active,
    Confirmed,
    Reverted,
    Expired
}

internal sealed class ProfilePreview
{
    public ProfilePreview(int durationSeconds)
    {
        if (durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));

        RemainingSeconds = durationSeconds;
        State = ProfilePreviewState.Active;
    }

    public ProfilePreviewState State { get; private set; }
    public int RemainingSeconds { get; private set; }
    public bool IsActive => State == ProfilePreviewState.Active;

    public bool Tick()
    {
        if (!IsActive)
            return false;

        RemainingSeconds--;
        if (RemainingSeconds <= 0)
        {
            RemainingSeconds = 0;
            State = ProfilePreviewState.Expired;
        }
        return true;
    }

    public bool Confirm()
    {
        if (!IsActive)
            return false;
        State = ProfilePreviewState.Confirmed;
        return true;
    }

    public bool Revert()
    {
        if (!IsActive)
            return false;
        State = ProfilePreviewState.Reverted;
        RemainingSeconds = 0;
        return true;
    }
}
