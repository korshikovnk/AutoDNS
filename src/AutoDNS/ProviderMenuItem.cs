using System.Drawing.Drawing2D;

namespace AutoDNS;

internal sealed class ProviderMenuItem(string text, string providerId) : ToolStripMenuItem(text)
{
    private int _spinnerAngle;
    public string ProviderId { get; } = providerId;
    public bool IsConnecting { get; private set; }
    public ProviderAvailability Availability { get; private set; }
    public int SpinnerAngle
    {
        get => _spinnerAngle;
        set
        {
            _spinnerAngle = value;
            if (IsConnecting || Availability == ProviderAvailability.Checking)
                RefreshImage();
        }
    }

    public void SetAvailability(ProviderAvailability status, string description)
    {
        Availability = status;
        ToolTipText = description;
        AccessibleDescription = description;
        RefreshImage();
    }

    public void UpdateState(string activeProviderId, string? pendingProviderId)
    {
        IsConnecting = string.Equals(ProviderId, pendingProviderId, StringComparison.OrdinalIgnoreCase);
        Checked = !IsConnecting && string.Equals(ProviderId, activeProviderId, StringComparison.OrdinalIgnoreCase);
        Enabled = pendingProviderId is null;
        RefreshImage();
    }

    private void RefreshImage()
    {
        var oldImage = Image;
        Image = AppSettings.IsAutomatic(ProviderId) && !IsConnecting
            ? null : CreateStatusImage(IsConnecting ? ProviderAvailability.Checking : Availability, SpinnerAngle);
        oldImage?.Dispose();
        Invalidate();
    }

    internal static Bitmap CreateStatusImage(ProviderAvailability status, int angle)
    {
        var image = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(SystemColors.Highlight, 2)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        switch (status)
        {
            case ProviderAvailability.Checking:
                graphics.DrawArc(pen, 2, 2, 12, 12, angle, 260);
                break;
            case ProviderAvailability.Available:
                pen.Color = AppConstants.ActiveGreen;
                graphics.DrawLines(pen, [new PointF(2, 8), new PointF(6, 12), new PointF(14, 3)]);
                break;
            case ProviderAvailability.Unavailable:
                pen.Color = Color.Firebrick;
                graphics.DrawLine(pen, 3, 3, 13, 13);
                graphics.DrawLine(pen, 13, 3, 3, 13);
                break;
            default:
                pen.Color = SystemColors.GrayText;
                graphics.DrawLine(pen, 3, 8, 13, 8);
                break;
        }
        return image;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var image = Image;
            Image = null;
            image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
