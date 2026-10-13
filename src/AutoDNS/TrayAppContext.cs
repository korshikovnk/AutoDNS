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

    private readonly AppSettings _settings;
    private bool _busy;
    private bool _rebuilding;
    private bool _keepMenuOpen;
    private string? _pendingProviderId;
    private string? _operationStatus;
    private string? _lastConnectionError;
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
            ShowCheckMargin = true
        };
        _menu.Opening += (_, _) =>
        {
            UpdateLastUpdateText();
        };
        _menu.Closing += OnMenuClosing;

        _iconOff = IconFactory.CreateTrayIcon(Color.White);
        _iconOn = IconFactory.CreateTrayIcon(AppConstants.ActiveGreen);

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
            await RefreshAndApplyAsync(showBalloon: !_settings.HasCompletedFirstRun).ConfigureAwait(true);
    }

    private async Task CheckAutoUpdateAsync()
    {
        if (_busy || !_settings.DnsEnabled || !_settings.IsUpdateDue())
            return;

        await RefreshAndApplyAsync(showBalloon: false).ConfigureAwait(true);
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

    private async Task RefreshAndApplyAsync(bool showBalloon, string? selectedProviderId = null)
    {
        if (_busy)
            return;

        _busy = true;
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
                _notifyIcon.Text = TooltipText();
                ApplyTrayIcon();
                UpdateLastUpdateText();
                if (showBalloon)
                    ShowBalloon("DNS сброшен к автоматическим адресам.");
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

            _notifyIcon.Text = TooltipText();
            ApplyTrayIcon();
            UpdateLastUpdateText();

            if (showBalloon)
                ShowBalloon("Настройки DNS " + provider.DisplayName + " применены.");
        }
        catch (Exception ex)
        {
            _lastConnectionError = PendingProviderName() + ": "
                + (ex is ProviderUnavailableException ? "сервис недоступен" : "ошибка подключения");
            ShowBalloon("Не удалось настроить DNS: " + ex.Message);
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
        _menu.Items["providers"] is ToolStripMenuItem providers
            ? providers.DropDownItems.OfType<ProviderMenuItem>()
            : [];

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
                        item.SetAvailability(result.Status, result.Description + " Проверено в " + DateTime.Now.ToString("HH:mm"));
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

        var providerName = new ToolStripMenuItem(FormatProviderName()) { Enabled = false, Name = "providerName" };
        _menu.Items.Add(providerName);

        var lastUpdate = new ToolStripMenuItem(FormatLastUpdate()) { Enabled = false, Name = "lastUpdate" };
        _menu.Items.Add(lastUpdate);
        _menu.Items.Add(new ToolStripSeparator());

        var apply = new ToolStripMenuItem("Обновить DNS") { Name = "apply" };
        apply.Click += async (_, _) => await Task.WhenAll(
            RefreshAndApplyAsync(showBalloon: true), RefreshProviderStatusesAsync(force: true)).ConfigureAwait(true);
        _menu.Items.Add(apply);

        var providers = new ToolStripMenuItem("Поставщик DNS") { Name = "providers" };
        if (providers.DropDown is ToolStripDropDownMenu providerMenu)
        {
            providerMenu.ShowImageMargin = true;
            providerMenu.ShowCheckMargin = true;
        }
        providers.DropDown.Closing += OnMenuClosing;
        providers.DropDown.ShowItemToolTips = true;
        var automatic = new ProviderMenuItem(AppConstants.AutomaticProviderName, AppConstants.AutomaticProviderId)
        {
            Checked = AppSettings.IsAutomatic(_settings.ProviderId),
            Tag = AppConstants.AutomaticProviderId
        };
        automatic.Click += OnProviderClicked;
        providers.DropDownItems.Add(automatic);
        foreach (var provider in DnsCatalog.All)
        {
            var item = new ProviderMenuItem(provider.DisplayName, provider.Id)
            {
                Checked = string.Equals(provider.Id, _settings.ProviderId, StringComparison.OrdinalIgnoreCase),
                Tag = provider.Id
            };
            item.SetAvailability(ProviderAvailability.Unknown, "DNS ещё не проверен.");
            item.Click += OnProviderClicked;
            providers.DropDownItems.Add(item);
        }
        _menu.Items.Add(providers);

        var autoUpdate = new ToolStripMenuItem("Автообновление");
        autoUpdate.DropDown.Closing += OnMenuClosing;
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Выкл.", AutoUpdateInterval.Off));
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Каждый час", AutoUpdateInterval.Hour));
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Каждый день", AutoUpdateInterval.Day));
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Каждую неделю", AutoUpdateInterval.Week));
        autoUpdate.DropDownItems.Add(CreateIntervalItem("Каждый месяц", AutoUpdateInterval.Month));
        _menu.Items.Add(autoUpdate);

        var autoStart = new ToolStripMenuItem("Автозапуск")
        {
            CheckOnClick = true,
            Checked = _settings.AutoStart
        };
        autoStart.Click += OnAutoStartClicked;
        _menu.Items.Add(autoStart);

        _menu.Items.Add(new ToolStripSeparator());

        var close = new ToolStripMenuItem("Закрыть") { Name = "close" };
        close.Click += (_, _) => ExitApp();
        _menu.Items.Add(close);

        var uninstall = new ToolStripMenuItem("Удалить") { Name = "uninstall" };
        uninstall.Click += (_, _) => OnUninstallClicked();
        _menu.Items.Add(uninstall);

        _rebuilding = false;
    }

    private void OnMenuClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (_keepMenuOpen && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
        {
            e.Cancel = true;
            if (ReferenceEquals(sender, _menu))
                _hidden.BeginInvoke(() => _keepMenuOpen = false);
        }
    }

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

        _keepMenuOpen = true;
        await RefreshAndApplyAsync(showBalloon: true, selectedProviderId: item.ProviderId).ConfigureAwait(true);
    }

    private void OnIntervalClicked(object? sender, EventArgs e)
    {
        if (_rebuilding || sender is not ToolStripMenuItem item || item.Tag is not AutoUpdateInterval interval)
            return;

        _keepMenuOpen = true;
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
            _ = RefreshAndApplyAsync(showBalloon: false);
        _ = RefreshProviderStatusesAsync();
    }

    private void OnUninstallClicked()
    {
        var answer = MessageBox.Show(
            "Удалить AutoDNS с компьютера?\nDNS вернётся к автоматическим адресам, программа и автозапуск будут удалены.",
            AppConstants.ProductName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        try
        {
            AppCleanup.UninstallAll();
        }
        catch (Exception ex)
        {
            ShowBalloon("Не удалось удалить приложение: " + ex.Message);
            return;
        }

        ExitApp();
    }

    private void ShowBalloon(string text)
    {
        _notifyIcon.BalloonTipTitle = AppConstants.ProductName;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void OnAutoStartClicked(object? sender, EventArgs e)
    {
        if (_rebuilding || sender is not ToolStripMenuItem item)
            return;

        _keepMenuOpen = true;
        _settings.AutoStart = item.Checked;
        _settings.Save();
        TaskSchedulerService.SetLogonEnabled(item.Checked);
    }

    private void UpdateLastUpdateText()
    {
        if (_menu.Items["providerName"] is ToolStripMenuItem providerItem)
            providerItem.Text = _busy ? "Подключение: " + PendingProviderName() : FormatProviderName();
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
        var state = _settings.DnsEnabled ? "включён" : "выключен";
        var text = _busy
            ? AppConstants.ProductName + " — подключение: " + PendingProviderName()
            : AppConstants.ProductName + " — " + name + " (" + state + ")";
        return text.Length <= 63 ? text : text[..63];
    }

    private void ApplyTrayIcon()
    {
        _notifyIcon.Icon = _settings.DnsEnabled ? _iconOn : _iconOff;
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
            _hidden.Dispose();
        }

        base.Dispose(disposing);
    }
}
