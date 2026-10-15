using System.Windows.Forms;
using Xunit;

namespace AutoDNS.Tests;

public class ProviderMenuItemTests
{
    [Fact]
    public void NativeMenuRendering_DisplaysGreenAndRedStatusIcons()
    {
        OnUiThread(() =>
        {
            using var menu = new System.Windows.Forms.ContextMenuStrip
            {
                ShowCheckMargin = true,
                ShowImageMargin = false
            };
            var available = new ProviderMenuItem("dns.malw.link", "malw");
            var partial = new ProviderMenuItem("Comss.one", "comss");
            var unavailable = new ProviderMenuItem("Xbox DNS", "xbox");
            menu.Items.Add(available);
            menu.Items.Add(partial);
            menu.Items.Add(unavailable);
            available.UpdateState("malw", null);
            partial.UpdateState("malw", null);
            unavailable.UpdateState("malw", null);
            available.SetAvailability(ProviderAvailability.Available, "Все открываются");
            partial.SetAvailability(ProviderAvailability.Partial, "Не открывается: Gemini.");
            unavailable.SetAvailability(ProviderAvailability.Unavailable, "Не открываются все");
            Assert.Null(available.Image);
            menu.CreateControl();
            menu.PerformLayout();
            var preferred = menu.GetPreferredSize(System.Drawing.Size.Empty);
            var itemWidth = menu.Items.Cast<ToolStripItem>().Max(item => item.Width);
            menu.Size = new System.Drawing.Size(Math.Max(preferred.Width, itemWidth), Math.Max(preferred.Height, menu.Height));
            menu.PerformLayout();
            using var bitmap = new System.Drawing.Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            var greenLeft = 0;
            var greenRight = 0;
            var yellowRight = 0;
            var redRight = 0;
            var split = bitmap.Width * 2 / 3;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var green = pixel.G > pixel.R * 1.5 && pixel.G > pixel.B * 1.2;
                var yellow = pixel.R > 180 && pixel.G > 120 && pixel.B < 80;
                var red = pixel.R > pixel.G * 2 && pixel.R > pixel.B * 2;
                if (x < split)
                {
                    if (green) greenLeft++;
                }
                else
                {
                    if (green) greenRight++;
                    if (yellow) yellowRight++;
                    if (red) redRight++;
                }
            }
            var previewPath = Environment.GetEnvironmentVariable("AUTODNS_UI_PREVIEW_PATH");
            if (!string.IsNullOrWhiteSpace(previewPath))
                bitmap.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
            Assert.True(greenRight > 5, "Green status icon is missing from the right side.");
            Assert.True(yellowRight > 5, "Yellow status icon is missing from the right side.");
            Assert.True(redRight > 5, "Red status icon is missing from the right side.");
            Assert.True(greenRight > greenLeft, "Status icon is still on the left.");
        });
    }

    [Fact]
    public void ServerStatus_DoesNotChangeSelectionOrBlockConnecting()
    {
        OnUiThread(() =>
        {
            using var item = new ProviderMenuItem("Xbox DNS", "xbox");
            item.UpdateState("malw", null);
            item.SetAvailability(ProviderAvailability.Unavailable, "Сервис недоступен");
            Assert.False(item.Checked);
            Assert.True(item.Enabled);
            Assert.Equal(ProviderAvailability.Unavailable, item.Availability);
            Assert.Equal("Сервис недоступен", item.ToolTipText);
            item.UpdateState("xbox", null);
            item.SetAvailability(ProviderAvailability.Checking, "Проверка DNS…");
            Assert.True(item.Checked);
            Assert.False(item.IsConnecting);
            Assert.True(item.Enabled);
        });
    }

    [Fact]
    public void Connecting_ShowsSpinnerWithoutCheckingPendingProvider_AndBlocksFurtherSelection()
    {
        OnUiThread(() =>
        {
            using var active = new ProviderMenuItem("dns.malw.link", "malw");
            using var pending = new ProviderMenuItem("Xbox DNS", "xbox");
            active.UpdateState("malw", "xbox");
            pending.UpdateState("malw", "xbox");

            Assert.True(active.Checked);
            Assert.False(active.IsConnecting);
            Assert.False(pending.Checked);
            Assert.True(pending.IsConnecting);
            Assert.False(active.Enabled);
            Assert.False(pending.Enabled);
            Assert.False(pending.CheckOnClick);

            pending.PerformClick();
            Assert.False(pending.Checked);
        });
    }

    [Theory]
    [InlineData("malw")]
    [InlineData("xbox")]
    public void Completion_RestoresSingleCheckAndStopsSpinner_AfterSuccessOrFailure(string resultProvider)
    {
        OnUiThread(() =>
        {
            using var active = new ProviderMenuItem("dns.malw.link", "malw");
            using var pending = new ProviderMenuItem("Xbox DNS", "xbox");
            active.UpdateState("malw", "xbox");
            pending.UpdateState("malw", "xbox");
            active.UpdateState(resultProvider, null);
            pending.UpdateState(resultProvider, null);

            Assert.Equal(1, new[] { active, pending }.Count(item => item.Checked));
            Assert.Equal(resultProvider == "malw", active.Checked);
            Assert.Equal(resultProvider == "xbox", pending.Checked);
            Assert.All(new[] { active, pending }, item =>
            {
                Assert.False(item.IsConnecting);
                Assert.True(item.Enabled);
            });
        });
    }

    [Fact]
    public void RefreshActiveProvider_ReplacesItsCheckWithSpinner()
    {
        OnUiThread(() =>
        {
            using var item = new ProviderMenuItem("dns.malw.link", "malw");
            item.UpdateState("malw", "malw");
            Assert.True(item.IsConnecting);
            Assert.False(item.Checked);
            item.UpdateState("malw", null);
            Assert.False(item.IsConnecting);
            Assert.True(item.Checked);
        });
    }

    [Theory]
    [InlineData(ToolStripDropDownCloseReason.ItemClicked, false, true)]
    [InlineData(ToolStripDropDownCloseReason.ItemClicked, true, false)]
    [InlineData(ToolStripDropDownCloseReason.Keyboard, false, false)]
    [InlineData(ToolStripDropDownCloseReason.AppClicked, false, false)]
    public void Menu_StaysOpenAfterItemClick_UntilEscapeOrOutsideClick(
        ToolStripDropDownCloseReason reason, bool allowDismiss, bool cancel)
    {
        Assert.Equal(cancel, TrayAppContext.ShouldCancelMenuClose(reason, allowDismiss));
    }

    [Fact]
    public void AutoUpdateSubmenu_IsPlacedToTheLeftOfItsOwner()
    {
        var location = TrayAppContext.LeftSubmenuScreenLocation(new Point(500, 240), new Size(140, 80));
        Assert.Equal(new Point(360, 240), location);
        Assert.True(location.X < 500);
    }

    private static void OnUiThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI test timed out.");
        if (error is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
