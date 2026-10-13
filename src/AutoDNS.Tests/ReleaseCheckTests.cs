using Xunit;

namespace AutoDNS.Tests;

public class ReleaseCheckTests
{
    [Fact]
    public void CurrentVersion_IsShownWithoutANewerRelease()
    {
        var notice = ReleaseCheck.NoticeFor(new Version(2, 2, 0, 0), new Version(2, 2), "2.2");
        Assert.False(notice.UpdateAvailable);
        Assert.Equal("Версия: 2.2", notice.Text);
    }

    [Fact]
    public void NewerRelease_ShowsArrowAndIsClickable()
    {
        var notice = ReleaseCheck.NoticeFor(new Version(2, 2, 0, 0), new Version(2, 3), "2.3");
        Assert.True(notice.UpdateAvailable);
        Assert.Equal("Доступно обновление: 2.2->2.3", notice.Text);
    }

    [Fact]
    public void ShortVersion_IsNewerThanPreviousPatchNumbering()
    {
        Assert.True(ReleaseCheck.Compare(new Version(2, 2), new Version(2, 1, 10)) > 0);
        Assert.Equal("2.2", ReleaseCheck.Format(new Version(2, 2, 0, 0)));
        Assert.Equal("2.1.10", ReleaseCheck.Format(new Version(2, 1, 10, 0)));
    }

    [Fact]
    public void GitHubTag_IsParsedFromLatestReleaseJson()
    {
        Assert.True(ReleaseCheck.TryReadTag("""{"tag_name":"v2.3"}""", out var version, out var label));
        Assert.Equal(new Version(2, 3), version);
        Assert.Equal("2.3", label);
        Assert.False(ReleaseCheck.TryParseTag("not-a-version", out _, out _));
    }
}
