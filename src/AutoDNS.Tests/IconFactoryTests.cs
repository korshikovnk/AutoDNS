using System.Drawing;
using Xunit;

namespace AutoDNS.Tests;

public class IconFactoryTests
{
    [Fact]
    public void UpdateBadge_SitsAtTheTopRightInBlue()
    {
        using var plain = IconFactory.CreateBitmap(32, Color.White);
        using var badged = IconFactory.CreateBitmap(32, Color.White, updateAvailable: true);
        var bounds = IconFactory.UpdateBadgeBounds(32);

        Assert.True(bounds.X > 32 / 2);
        Assert.True(bounds.Bottom <= 32 / 2 + 2);
        Assert.Equal(0, CountBadgePixels(plain));
        Assert.True(CountBadgePixels(badged, bounds) > 0);
        Assert.Equal(0, CountBadgePixelsOutside(badged, bounds));

        var center = badged.GetPixel(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        Assert.InRange(center.R, 15, 40);
        Assert.InRange(center.G, 90, 120);
        Assert.InRange(center.B, 160, 200);
    }

    private static int CountBadgePixels(Bitmap bitmap, Rectangle? region = null)
    {
        var area = region ?? new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var count = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (IsBadge(bitmap.GetPixel(x, y)))
                    count++;
            }
        }

        return count;
    }

    private static int CountBadgePixelsOutside(Bitmap bitmap, Rectangle region)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (region.Contains(x, y))
                    continue;
                if (IsBadge(bitmap.GetPixel(x, y)))
                    count++;
            }
        }

        return count;
    }

    private static bool IsBadge(Color color) =>
        color.A > 180
        && Math.Abs(color.R - 25) <= 12
        && Math.Abs(color.G - 102) <= 12
        && Math.Abs(color.B - 179) <= 12;
}
