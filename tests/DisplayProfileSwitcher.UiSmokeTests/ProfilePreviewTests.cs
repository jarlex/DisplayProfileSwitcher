using NUnit.Framework;

namespace DisplayProfileSwitcher.UiSmokeTests;

[TestFixture]
public sealed class ProfilePreviewTests
{
    [Test]
    public void Tick_expires_after_duration()
    {
        var preview = new ProfilePreview(2);

        Assert.That(preview.Tick(), Is.True);
        Assert.That(preview.IsActive, Is.True);
        Assert.That(preview.RemainingSeconds, Is.EqualTo(1));
        Assert.That(preview.Tick(), Is.True);
        Assert.That(preview.State, Is.EqualTo(ProfilePreviewState.Expired));
        Assert.That(preview.Tick(), Is.False);
    }

    [Test]
    public void Confirm_and_revert_are_single_terminal_transitions()
    {
        var confirmed = new ProfilePreview(10);
        Assert.That(confirmed.Confirm(), Is.True);
        Assert.That(confirmed.Confirm(), Is.False);
        Assert.That(confirmed.Revert(), Is.False);

        var reverted = new ProfilePreview(10);
        Assert.That(reverted.Revert(), Is.True);
        Assert.That(reverted.State, Is.EqualTo(ProfilePreviewState.Reverted));
        Assert.That(reverted.Tick(), Is.False);
    }

    [Test]
    public void Preview_state_blocks_all_transitions_after_confirmation_or_revert()
    {
        var confirmed = new ProfilePreview(10);
        Assert.That(confirmed.Confirm(), Is.True);
        Assert.That(confirmed.IsActive, Is.False);
        Assert.That(confirmed.Tick(), Is.False);
        Assert.That(confirmed.Revert(), Is.False);

        var reverted = new ProfilePreview(10);
        Assert.That(reverted.Revert(), Is.True);
        Assert.That(reverted.IsActive, Is.False);
        Assert.That(reverted.Confirm(), Is.False);
    }
}
