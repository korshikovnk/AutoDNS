using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoDNS;

internal sealed class TrayAppContext : ApplicationContext
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Form _hidden;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly EventWaitHandle _shutdownEvent;
    private readonly RegisteredWaitHandle _shutdownWait;
    private readonly Icon _iconOff;
    private readonly Icon _iconOn;
    private readonly Icon _iconPartial;
    private readonly Icon _iconDown;

    private readonly AppSettings _settings;
    private bool _busy;
    private bool _rebuilding;
    private bool _allowMenuClose;
    private ProviderAvailability _connectedSites = ProviderAvailability.Available;
    private bool _connectedCheckRunning;
    private string? _pendingProviderId;
    private string? _operationStatus;
    private string? _lastConnectionError;
    private bool _updateAvailable;
    private string _versionText = ReleaseCheck.CurrentNotice().Text;
    private bool _exitRequested;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _statusCheckRunning;
    private DateTimeOffset? _lastStatusCheckUtc;

    public TrayAppContext(AppSettings settings)
    {
        _settings = settings;

        _hidden = new Form
        {
            WindowState = FormWindowState.Minimized,
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            Opacity = 0,
            Size = new Size(1, 1),
            Text = AppConstants.ProductName
        };
        _hidden.Show();
        _hidden.Hide();

        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            ShowItemToolTips = true
        };
        _menu.Opening += (_, _) =>
        {
            UpdateLastUpdateText();
        };
        _menu.Closing += OnMenuClosing;

        _iconOff = IconFactory.CreateTrayIcon(Color.White);
        _iconOn = IconFactory.CreateTrayIcon(AppConstants.ActiveGreen);
        _iconPartial = IconFactory.CreateTrayIcon(AppConstants.AlertYellow);
        _iconDown = IconFactory.CreateTrayIcon(AppConstants.AlertRed);

        _notifyIcon = new NotifyIcon
        {
            Text = TooltipText(),
            Icon = _settings.DnsEnabled ? _iconOn : _iconOff,
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseClick += OnNotifyIconClick;

        _animationTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _animationTimer.Tick += (_, _) =>
        {
            foreach (var item in ProviderItems().Where(item => item.IsConnecting || item.Availability == ProviderAvailability.Checking))
            {
                item.SpinnerAngle = (item.SpinnerAngle + 30) % 360;
                item.Invalidate();
            }
        };

        BuildMenu();

        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += async (_, _) =>
        {
            await CheckAutoUpdateAsync().ConfigureAwait(true);
            await RefreshProviderStatusesAsync().ConfigureAwait(true);
            await CheckConnectedDnsAsync().ConfigureAwait(true);
        };
        _timer.Start();

        _shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppConstants.ShutdownEventName);
        _shutdownWait = ThreadPool.RegisterWaitForSingleObject(
            _shutdownEvent,
            (_, _) => Ui(ExitApp),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _hidden.BeginInvoke(async () => await StartupAsync().ConfigureAwait(true));
    }

    private async Task StartupAsync()
    {
        _ = RefreshProviderStatusesAsync(force: true);
        if (!_settings.DnsEnabled)
            return;

        if (!_settings.HasCompletedFirstRun || _settings.LastUpdateUtc is null || _settings.IsUpdateDue())
            await RefreshAndApplyAsync().ConfigureAwait(true);
    }

    private async Task CheckAutoUpdateAsync()
    {
        if (_busy || !_settings.DnsEnabled || !_settings.IsUpdateDue())
            return;

        await RefreshAndApplyAsync().ConfigureAwait(true);
    }

    private void Ui(Action action)
    {
        if (_hidden.IsHandleCreated && _hidden.InvokeRequired)
            _hidden.BeginInvoke(action);
        else
            action();
    }

    private void OnNotifyIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _menu.Show(Cursor.Position);
        if (_menu.Handle != IntPtr.Zero)
            SetForegroundWindow(_menu.Handle);
    }

    private async Task RefreshAndApplyAsync(string? selectedProviderId = null)
    {
        if (_busy)
            return;

        _busy = true;
        _ = CheckForAppUpdateAsync();
        var providerId = selectedProviderId ?? _settings.ProviderId;
        _pendingProviderId = providerId;
        _lastConnectionError = null;
        _operationStatus = "Подключение…";
        UpdateConnectionUi();
        _animationTimer.Start();
        try
        {
            if (AppSettings.IsAutomatic(providerId))
            {
                SetOperationStatus("Возврат к автоматическому DNS…");
                await Task.Run(WindowsDns.RestoreAutomatic).ConfigureAwait(true);
                _settings.ProviderId = AppConstants.AutomaticProviderId;
                _settings.DnsEnabled = false;
                _settings.HasCompletedFirstRun = true;
                _settings.LastUpdateUtc = DateTimeOffset.UtcNow;
                _settings.Save();
                _connectedSites = ProviderAvailability.Available;
                _notifyIcon.Text = TooltipText();
                ApplyTrayIcon();
                UpdateLastUpdateText();
                return;
            }

            var provider = DnsCatalog.Require(providerId);
            SetOperationStatus("Проверка статуса поставщика…");
            DnsEndpoints endpoints;
            try
            {
                endpoints = await DnsRefreshService.ResolveAsync(provider).ConfigureAwait(true);
            }
            catch (ProviderUnavailableException)
            {
                throw;
            }
            catch
            {
                endpoints = CachedOrFallback(provider);
            }

            SetOperationStatus("Проверка ответа DNS…");
            await DnsHealthCheck.EnsureAvailableAsync(endpoints).ConfigureAwait(true);
            SetOperationStatus("Применение настроек…");
            await Task.Run(() => WindowsDns.Apply(endpoints)).ConfigureAwait(true);

            _settings.ProviderId = provider.Id;
            _settings.CachedIpv4 = [.. endpoints.Ipv4];
            _settings.CachedIpv6 = [.. endpoints.Ipv6];
            _settings.HasCompletedFirstRun = true;
            _settings.DnsEnabled = true;
            _settings.LastUpdateUtc = DateTimeOffset.UtcNow;
            _settings.Save();
            _connectedSites = ProviderAvailability.Available;

            _notifyIcon.Text = TooltipText();
            ApplyTrayIcon();
            UpdateLastUpdateText();
        }
        catch (Exception ex)
        {
            _lastConnectionError = PendingProviderName() + ": "
                + (ex is ProviderUnavailableException ? "сервис недоступен" : "ошибка подключения");
            if (_settings.DnsEnabled
                && string.Equals(providerId, _settings.ProviderId, StringComparison.OrdinalIgnoreCase))
                _connectedSites = ProviderAvailability.Unavailable;
        }
        finally
        {
            _busy = false;
            if (!_statusCheckRunning)
                _animationTimer.Stop();
            _pendingProviderId = null;
            _operationStatus = null;
            UpdateConnectionUi();
            if (_exitRequested)
                ExitApp();
        }
    }

    private IEnumerable<ProviderMenuItem> ProviderItems() =>
        _menu.Items.OfType<ProviderMenuItem>();

    private async Task RefreshProviderStatusesAsync(bool force = false)
    {
        if (_lifetime.IsCancellationRequested || _statusCheckRunning
            || (!force && !_settings.IsUpdateDue(_lastStatusCheckUtc, DateTimeOffset.UtcNow)))
            return;

        _statusCheckRunning = true;
        _lastStatusCheckUtc = DateTimeOffset.UtcNow;
        _animationTimer.Start();
        try
        {
            var token = _lifetime.Token;
            await Task.WhenAll(DnsCatalog.All.Select(async provider =>
            {
                var item = ProviderItems().First(p => p.ProviderId == provider.Id);
                item.SetAvailability(ProviderAvailability.Checking, "Проверка DNS…");
                try
                {
                    var result = await ProviderAvailabilityCheck.CheckAsync(provider, token).ConfigureAwait(true);
                    if (!token.IsCancellationRequested)
                    {
                        item.SetAvailability(result.Status, result.Description + " Проверено в " + DateTime.Now.ToString("HH:mm"));
                        NoteConnectedStatus(provider.Id, result.Status);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            })).ConfigureAwait(true);
        }
        finally
        {
            _statusCheckRunning = false;
            if (!_lifetime.IsCancellationRequested && !_busy)
                _animationTimer.Stop();
        }
    }

    private async Task CheckConnectedDnsAsync()
    {
        if (_connectedCheckRunning || _busy || _statusCheckRunning || !_settings.DnsEnabled
            || _lifetime.IsCancellationRequested || AppSettings.IsAutomatic(_settings.ProviderId))
            return;

        var provider = DnsCatalog.Find(_settings.ProviderId);
        if (provider is null)
            return;

        _connectedCheckRunning = true;
        try
        {
            var result = await ProviderAvailabilityCheck.CheckAsync(provider, _lifetime.Token).ConfigureAwait(true);
            if (_lifetime.IsCancellationRequested)
                return;

            ProviderItems().FirstOrDefault(item => item.ProviderId == provider.Id)
                ?.SetAvailability(result.Status, result.Description + " Проверено в " + DateTime.Now.ToString("HH:mm"));
            NoteConnectedStatus(provider.Id, result.Status);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _connectedCheckRunning = false;
        }
    }

    private void NoteConnectedStatus(string providerId, ProviderAvailability status)
    {
        if (_busy || !_settings.DnsEnabled
            || !string.Equals(providerId, _settings.ProviderId, StringComparison.OrdinalIgnoreCase))
            return;

        if (status is not (ProviderAvailability.Available or ProviderAvailability.Partial or ProviderAvailability.Unavailable))
            return;

        _connectedSites = status;

        ApplyTrayIcon();
    }

    private void SetOperationStatus(string status)
    {
        _operationStatus = status;
        UpdateLastUpdateText();
    }

    private void UpdateConnectionUi()
    {
        foreach (var item in ProviderItems())
            item.UpdateState(_settings.ProviderId, _pendingProviderId);
        foreach (var name in new[] { "apply", "uninstall", "close" })
        {
            if (_menu.Items[name] is ToolStripMenuItem item)
                item.Enabled = !_busy;
        }
        _notifyIcon.Text = TooltipText();
        ApplyTrayIcon();
        UpdateLastUpdateText();
    }

    private DnsEndpoints CachedOrFallback(DnsProvider provider)
    {
        if (_settings.CachedIpv4.Count > 0 &&
            string.Equals(_settings.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
            return DnsCatalog.EndpointsFrom(provider, _settings.CachedIpv4, _settings.CachedIpv6);

        return DnsCatalog.EndpointsFrom(provider);
    }

    private void BuildMenu()
    {
        _rebuilding = true;
        _menu.Items.Clear();

        var version = new ToolStripMenuItem(_versionText) { Name = "versionStatus" };
        version.Click += OnVersionClicked;
        _menu.Items.Add(version);
        ApplyVersionMenuItem();

        var lastUpdate = new ToolStripMenuItem(FormatLastUpdate()) { Enabled = false, Name = "lastUpdate" };
        _menu.Items.Add(lastUpdate);

        var apply = new ToolStripMenuItem("Обновить") { Name = "apply" };
        apply.Click += async (_, _) => await Task.WhenAll(
            RefreshAndApplyAsync(), RefreshProviderStatusesAsync(force: true)).ConfigureAwait(true);
        _menu.Items.Add(apply);
        _menu.Items.Add(new ToolStripSeparator());

        var automatic = new ProviderMenuItem(AppConstants.AutomaticProviderName, AppConstants.AutomaticProviderId)
        {
            Checked = AppSettings.IsAutomatic(_settings.ProviderId),
            Tag = AppConstants.AutomaticProviderId
        };
        automatic.Click += OnProviderClicked;
        _menu.Items.Add(automatic);
        foreach (var provider in DnsCatalog.All)
        {
            var item = new ProviderMenuItem(provider.DisplayName, provider.Id)
            {
                Checked = string.Equals(provider.Id, _settings.ProviderId, StringComparison.OrdinalIgnoreCase),
                Tag = provider.Id
            };
            item.SetAvailability(ProviderAvailability.Unknown, "DNS ещё не проверен.");
            item.Click += OnProviderClicked;
            _menu.Items.Add(item);
        }

        _menu.Items.Add(new ToolStripSeparator());

        var autoStart = new ToolStripMenuItem("Автозапуск")
        {
            CheckOnClick = true,
            Checked = _settings.AutoStart
        };
        autoStart.Click += OnAutoStartClicked;
        _menu.Items.Add(autoStart);

        var autoUpdate = new ToolStripMenuItem("Автообновление");
        KeepSubmenuOnTheLeft(autoUpdate);
        autoUpdate.DropDown.Closing += OnMenuClosing;
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Выкл.", AutoUpdateInterval.Off));
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Каждый час", AutoUpdateInterval.Hour));
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Каждый день", AutoUpdateInterval.Day));
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Каждую неделю", AutoUpdateInterval.Week));
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Каждый месяц", AutoUpdateInterval.Month));
        _menu.Items.Add(autoUpdate);

        _menu.Items.Add(new ToolStripSeparator());

        var close = new ToolStripMenuItem("Закрыть программу") { Name = "close" };
        close.Click += (_, _) =>
        {
            _allowMenuClose = true;
            ExitApp();
        };
        _menu.Items.Add(close);

        var uninstall = new ToolStripMenuItem("Удалить программу") { Name = "uninstall" };
        uninstall.Click += (_, _) => OnUninstallClicked();
        _menu.Items.Add(uninstall);

        _rebuilding = false;
    }

    private static void KeepSubmenuOnTheLeft(ToolStripMenuItem item)
    {
        item.DropDownDirection = ToolStripDropDownDirection.Left;
        var placing = false;
        item.DropDownOpening += (_, _) => item.DropDownDirection = ToolStripDropDownDirection.Left;
        item.DropDown.LocationChanged += (_, _) =>
        {
            if (placing || item.DropDown.Width <= 0 || item.Owner is not ToolStrip parent)
                return;

            var ownerOnScreen = parent.PointToScreen(item.Bounds.Location);
            var location = LeftSubmenuScreenLocation(ownerOnScreen, item.DropDown.Size);
            if (item.DropDown.Location == location)
                return;

            placing = true;
            item.DropDown.Location = location;
            placing = false;
        };
    }

    internal static Point LeftSubmenuScreenLocation(Point ownerItemScreen, Size submenuSize) =>
        new(ownerItemScreen.X - submenuSize.Width, ownerItemScreen.Y);

    private void OnMenuClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        var allowDismiss = _allowMenuClose && ReferenceEquals(sender, _menu);
        e.Cancel = ShouldCancelMenuClose(e.CloseReason, allowDismiss);
    }

    internal static bool ShouldCancelMenuClose(ToolStripDropDownCloseReason reason, bool allowDismiss) =>
        reason == ToolStripDropDownCloseReason.ItemClicked && !allowDismiss;

    private ToolStripMenuItem CreateIntervalItem(string text, AutoUpdateInterval interval)
    {
        var item = new ToolStripMenuItem(text)
        {
            CheckOnClick = true,
            Checked = _settings.AutoUpdate == interval,
            Tag = interval
        };
        item.Click += OnIntervalClicked;
        return item;
    }

    private async void OnProviderClicked(object? sender, EventArgs e)
    {
        if (_rebuilding || _busy || sender is not ProviderMenuItem item)
            return;

        await RefreshAndApplyAsync(item.ProviderId).ConfigureAwait(true);
    }

    private void OnIntervalClicked(object? sender, EventArgs e)
    {
        if (_rebuilding || sender is not ToolStripMenuItem item || item.Tag is not AutoUpdateInterval interval)
            return;

        _settings.AutoUpdate = interval;
        _settings.Save();

        if (item.Owner is not null)
        {
            _rebuilding = true;
            foreach (ToolStripItem child in item.Owner.Items)
            {
                if (child is ToolStripMenuItem menuItem)
                    menuItem.Checked = ReferenceEquals(menuItem, item);
            }
            _rebuilding = false;
        }

        if (_settings.DnsEnabled && _settings.IsUpdateDue())
            _ = RefreshAndApplyAsync();
        _ = RefreshProviderStatusesAsync();
    }

    private void OnUninstallClicked()
    {
        _allowMenuClose = true;
        var answer = MessageBox.Show(
            "Удалить AutoDNS с компьютера?\nDNS вернётся к автоматическим адресам, программа и автозапуск будут удалены.",
            AppConstants.ProductName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            _allowMenuClose = false;
            return;
        }

        try
        {
            AppCleanup.UninstallAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось удалить приложение: " + ex.Message,
                AppConstants.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        ExitApp();
    }

    private void OnAutoStartClicked(object? sender, EventArgs e)
    {
        if (_rebuilding || sender is not ToolStripMenuItem item)
            return;

        _settings.AutoStart = item.Checked;
        _settings.Save();
        TaskSchedulerService.SetLogonEnabled(item.Checked);
    }

    private async Task CheckForAppUpdateAsync()
    {
        try
        {
            var notice = await ReleaseCheck.CheckAsync(_lifetime.Token).ConfigureAwait(true);
            _updateAvailable = notice.UpdateAvailable;
            _versionText = notice.Text;
            ApplyVersionMenuItem();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void ApplyVersionMenuItem()
    {
        if (_menu.Items["versionStatus"] is not ToolStripMenuItem item)
            return;

        item.Text = _versionText;
        item.Enabled = _updateAvailable;
    }

    private void OnVersionClicked(object? sender, EventArgs e)
    {
        if (!_updateAvailable)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(ReleaseCheck.ReleasesPageUrl) { UseShellExecute = true });
        }
        catch
        {
            // The browser could not be opened.
        }
    }

    private void UpdateLastUpdateText()
    {
        if (_menu.Items["lastUpdate"] is ToolStripMenuItem item)
            item.Text = _operationStatus ?? _lastConnectionError ?? FormatLastUpdate();
    }

    private string PendingProviderName() => AppSettings.IsAutomatic(_pendingProviderId)
        ? AppConstants.AutomaticProviderName
        : DnsCatalog.Require(_pendingProviderId).DisplayName;

    private string FormatProviderName() =>
        AppSettings.IsAutomatic(_settings.ProviderId)
            ? AppConstants.AutomaticProviderName
            : DnsCatalog.Require(_settings.ProviderId).DisplayName;

    private string FormatLastUpdate()
    {
        if (_settings.LastUpdateUtc is null)
            return "Ещё не обновлялось";

        var local = _settings.LastUpdateUtc.Value.ToLocalTime();
        return local.ToString("dd.MM.yyyy, HH:mm");
    }

    private string TooltipText()
    {
        var name = FormatProviderName();
        var state = !_settings.DnsEnabled ? "выключен"
            : _connectedSites == ProviderAvailability.Unavailable ? "не отвечает"
            : _connectedSites == ProviderAvailability.Partial ? "частично"
            : "включён";
        var text = _busy
            ? AppConstants.ProductName + " — подключение: " + PendingProviderName()
            : AppConstants.ProductName + " — " + name + " (" + state + ")";
        return text.Length <= 63 ? text : text[..63];
    }

    private void ApplyTrayIcon()
    {
        _notifyIcon.Icon = !_settings.DnsEnabled ? _iconOff
            : _connectedSites == ProviderAvailability.Unavailable ? _iconDown
            : _connectedSites == ProviderAvailability.Partial ? _iconPartial
            : _iconOn;
    }

    private void ExitApp()
    {
        if (_busy)
        {
            _exitRequested = true;
            return;
        }
        _timer.Stop();
        _lifetime.Cancel();
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _shutdownWait.Unregister(null);
            _shutdownEvent.Dispose();
            _timer.Dispose();
            _animationTimer.Dispose();
            _menu.Dispose();
            _notifyIcon.Dispose();
            _iconOff.Dispose();
            _iconOn.Dispose();
            _iconPartial.Dispose();
            _iconDown.Dispose();
            _hidden.Dispose();
        }

        base.Dispose(disposing);
    }
}
