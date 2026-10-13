using Xunit;

namespace AutoDNS.Tests;

public class UpdateIntervalTests
{
    [Theory]
    [InlineData(AutoUpdateInterval.Hour, 1)]
    [InlineData(AutoUpdateInterval.Day, 24)]
    [InlineData(AutoUpdateInterval.Week, 168)]
    [InlineData(AutoUpdateInterval.Month, 720)]
    internal void StatusUpdates_FollowChosenInterval(AutoUpdateInterval interval, int hours)
    {
        var settings = new AppSettings { AutoUpdate = interval };
        var last = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        Assert.False(settings.IsUpdateDue(last, last.AddHours(hours).AddSeconds(-1)));
        Assert.True(settings.IsUpdateDue(last, last.AddHours(hours)));
        Assert.True(settings.IsUpdateDue(null, last));
    }

    [Fact]
    public void DisabledAutoUpdate_DisablesScheduledStatusChecks()
    {
        var settings = new AppSettings { AutoUpdate = AutoUpdateInterval.Off };
        Assert.False(settings.IsUpdateDue(null, DateTimeOffset.UtcNow));
        Assert.False(settings.IsUpdateDue(DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow));
    }
}
