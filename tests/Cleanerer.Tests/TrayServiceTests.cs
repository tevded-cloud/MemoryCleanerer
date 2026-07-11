using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Covers <see cref="TrayService.TooltipText"/>, the pure formatter for the tray icon's tooltip.
/// The NotifyIcon itself is UI-bound and not exercised here (see TrayService's remarks).
/// </summary>
public class TrayServiceTests
{
    [Fact]
    public void TypicalLoad_FormatsWithPercentSign()
    {
        Assert.Equal("Cleanerer: memory 47%", TrayService.TooltipText(47));
    }

    [Fact]
    public void ZeroLoad_FormatsAsZeroPercent()
    {
        Assert.Equal("Cleanerer: memory 0%", TrayService.TooltipText(0));
    }

    [Fact]
    public void FullLoad_FormatsAsOneHundredPercent()
    {
        Assert.Equal("Cleanerer: memory 100%", TrayService.TooltipText(100));
    }

    [Fact]
    public void Result_NeverExceedsNotifyIconTextLimit()
    {
        // NotifyIcon.Text has a hard 63-character limit; every plausible 0-100 input must stay
        // comfortably under it.
        for (int percent = 0; percent <= 100; percent++)
        {
            Assert.True(TrayService.TooltipText(percent).Length <= 63);
        }
    }
}
