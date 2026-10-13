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
                ShowImageMargin = true
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
            partial.SetAvailability(ProviderAvailability.Partial, "Не открывается: Cursor.");
            unavailable.SetAvailability(ProviderAvailability.Unavailable, "Не открываются все");
            Assert.NotNull(available.Image);
            Assert.NotNull(partial.Image);
            Assert.NotNull(unavailable.Image);
            menu.CreateControl();
            menu.PerformLayout();
            using var bitmap = new System.Drawing.Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            var green = 0;
            var yellow = 0;
            var red = 0;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.G > pixel.R * 1.5 && pixel.G > pixel.B * 1.2) green++;
                if (pixel.R > 180 && pixel.G > 120 && pixel.B < 80) yellow++;
                if (pixel.R > pixel.G * 2 && pixel.R > pixel.B * 2) red++;
            }
            var previewPath = Environment.GetEnvironmentVariable("AUTODNS_UI_PREVIEW_PATH");
            if (!string.IsNullOrWhiteSpace(previewPath))
                bitmap.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
            Assert.True(green > 5, "Green status icon is missing from the rendered menu.");
            Assert.True(yellow > 5, "Yellow status icon is missing from the rendered menu.");
            Assert.True(red > 5, "Red status icon is missing from the rendered menu.");
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
