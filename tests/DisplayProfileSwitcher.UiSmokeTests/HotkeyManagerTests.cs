using NUnit.Framework;

namespace DisplayProfileSwitcher.UiSmokeTests;

[TestFixture]
public sealed class HotkeyManagerTests
{
    [TestCase("control + alt + 1", "CTRL+ALT+1")]
    [TestCase("windows+f12", "WIN+F12")]
    [TestCase("CTRL+ESC", "CTRL+ESCAPE")]
    public void TryCanonicalize_normalizes_supported_hotkeys(string input, string expected)
    {
        Assert.That(HotkeyManager.TryCanonicalize(input, out var actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void TryCanonicalize_rejects_unregistrable_text()
    {
        Assert.That(HotkeyManager.TryCanonicalize("CTRL+NOT_A_KEY", out _), Is.False);
    }

    [TestCase("CTRL+1+2")]
    [TestCase("CTRL+A+B")]
    public void TryCanonicalize_rejects_multiple_non_modifier_keys(string input)
    {
        Assert.That(HotkeyManager.TryCanonicalize(input, out _), Is.False);
    }

    [Test]
    public void TryCanonicalize_keeps_duplicate_modifiers_without_overwriting_key()
    {
        Assert.That(HotkeyManager.TryCanonicalize("CTRL+CTRL+1", out var actual), Is.True);
        Assert.That(actual, Is.EqualTo("CTRL+1"));
    }
}
