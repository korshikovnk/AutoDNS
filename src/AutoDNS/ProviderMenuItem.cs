using System.Drawing.Drawing2D;

namespace AutoDNS;

internal sealed class ProviderMenuItem : ToolStripMenuItem
{
    private const int StatusWidth = 16;
    private const int StatusRightMargin = 8;
    private Bitmap? _statusImage;
    private int _spinnerAngle;

    public ProviderMenuItem(string text, string providerId) : base(text)
    {
        ProviderId = providerId;
        Padding = new Padding(0, 0, StatusWidth + StatusRightMargin + 4, 0);
    }

    public string ProviderId { get; }
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
        var oldImage = _statusImage;
        _statusImage = AppSettings.IsAutomatic(ProviderId) && !IsConnecting
            ? null
            : CreateStatusImage(IsConnecting ? ProviderAvailability.Checking : Availability, SpinnerAngle);
        oldImage?.Dispose();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_statusImage is null)
            return;

        var visibleRight = Width;
        if (Parent is { ClientSize.Width: > 0 })
            visibleRight = Math.Min(Width, Parent.ClientSize.Width - Math.Max(Bounds.X, 0));

        var x = visibleRight - StatusWidth - StatusRightMargin;
        var y = Math.Max(0, (Height - _statusImage.Height) / 2);
        if (x < 0)
            return;

        e.Graphics.DrawImageUnscaled(_statusImage, x, y);
    }

    internal static Bitmap CreateStatusImage(ProviderAvailability status, int angle)
    {
        var image = new Bitmap(StatusWidth, StatusWidth);
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
            case ProviderAvailability.Partial:
                pen.Color = AppConstants.AlertYellow;
                graphics.DrawEllipse(pen, 2, 2, 12, 12);
                graphics.DrawLine(pen, 8, 5, 8, 9);
                using (var dot = new SolidBrush(AppConstants.AlertYellow))
                    graphics.FillEllipse(dot, 6.5f, 11, 3, 3);
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
            _statusImage?.Dispose();
            _statusImage = null;
        }
        base.Dispose(disposing);
    }
}
