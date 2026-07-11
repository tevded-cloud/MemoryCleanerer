using System;
using System.Threading.Tasks;
using Cleanerer.Interop;

namespace Cleanerer.Services;

/// <summary>
/// Hosts the WinForms <see cref="System.Windows.Forms.NotifyIcon"/> that lets Cleanerer keep
/// running "in the background": the tray icon offers Open / Trim now / Clear cache now / Exit,
/// its tooltip shows the current memory load, and it is what the window hides to when
/// <see cref="AppSettings.RunInBackground"/> is on (see <see cref="MainWindow.OnClosing"/>).
///
/// A process-wide singleton (<see cref="Instance"/>), matching the style of
/// <see cref="SchedulerService"/> / <see cref="SettingsService"/>. <see cref="Initialize"/> must
/// run once at startup after the main window exists (see App.xaml.cs); <see cref="Dispose"/>
/// tears the icon down on real shutdown.
///
/// WPF and WinForms are both in scope in this project (tray icon needs WinForms), so every
/// ambiguous type (<c>Application</c>, <c>Timer</c>, ...) is fully qualified here rather than
/// pulled in via <c>using</c>, matching the pattern already used in CleanerService/SchedulerService.
/// </summary>
public sealed class TrayService : IDisposable
{
    /// <summary>Shared instance used by the running app.</summary>
    public static TrayService Instance { get; } = new TrayService();

    private static readonly TimeSpan TooltipInterval = TimeSpan.FromSeconds(10);
    private const int BalloonTimeoutMs = 4000;

    private readonly CleanerService _cleaner = new();
    private readonly MemoryInfoService _memoryInfo = new();

    private MainWindow? _window;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Windows.Threading.DispatcherTimer? _tooltipTimer;
    private System.Drawing.Icon? _icon;
    private bool _busy;
    private bool _hasShownFirstHideBalloon;

    private TrayService()
    {
    }

    /// <summary>
    /// Builds the icon, context menu, and tooltip timer, and wires the "show window again if
    /// background mode is turned off while hidden" reaction. Idempotent — a second call is a
    /// no-op, since the app only ever has one main window.
    /// </summary>
    public void Initialize(MainWindow window)
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        _window = window;
        _icon = BuildIcon();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Trim now", null, (_, _) => RunCleanup(trim: true));
        menu.Items.Add("Clear cache now", null, (_, _) => RunCleanup(trim: false));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Cleanerer",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenWindow();

        // A DispatcherTimer (not a WinForms Timer) matches SchedulerService/MemoryViewModel and
        // keeps this on the same UI-thread message pump the NotifyIcon callbacks already use.
        _tooltipTimer = new System.Windows.Threading.DispatcherTimer { Interval = TooltipInterval };
        _tooltipTimer.Tick += (_, _) => RefreshTooltip();
        _tooltipTimer.Start();

        RefreshTooltip();

        // If the window is currently hidden (backgrounded) and the user turns background mode
        // off from Options, show it back up — otherwise the app would become unreachable.
        SettingsService.Instance.SettingsChanged += (_, settings) =>
        {
            if (!settings.RunInBackground && _window is { IsVisible: false })
            {
                OpenWindow();
            }
        };
    }

    /// <summary>
    /// Shows the one-time "still running here" balloon the first time the window is hidden to
    /// the tray in this app session. Safe to call more than once; only the first call fires.
    /// </summary>
    public void NotifyHiddenToTray()
    {
        if (_hasShownFirstHideBalloon)
        {
            return;
        }

        _hasShownFirstHideBalloon = true;
        ShowBalloon("Cleanerer", "Cleanerer is still running here", System.Windows.Forms.ToolTipIcon.Info);
    }

    private void RefreshTooltip()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        int loadPercent = _memoryInfo.Read().LoadPercent;
        _notifyIcon.Text = TooltipText(loadPercent);
    }

    /// <summary>
    /// Builds the tray tooltip text. Pulled out as a pure static method so the 63-char
    /// <see cref="System.Windows.Forms.NotifyIcon.Text"/> limit is easy to reason about /
    /// unit test without a live NotifyIcon.
    /// </summary>
    public static string TooltipText(int loadPercent)
    {
        string text = $"Cleanerer: memory {loadPercent}%";
        return text.Length <= 63 ? text : text.Substring(0, 63);
    }

    private void OpenWindow()
    {
        MainWindow? window = _window;
        if (window is null)
        {
            return;
        }

        void Restore()
        {
            if (!window.IsVisible)
            {
                window.Show();
            }

            if (window.WindowState == System.Windows.WindowState.Minimized)
            {
                window.WindowState = System.Windows.WindowState.Normal;
            }

            window.Activate();
        }

        InvokeOnUiThread(Restore);
    }

    private void RunCleanup(bool trim)
    {
        // One cleanup at a time, matching SchedulerService's _busy guard.
        if (_busy)
        {
            return;
        }

        _busy = true;
        _ = Task.Run(() =>
        {
            try
            {
                CleanResult result = trim ? _cleaner.TrimWorkingSets() : _cleaner.ClearSystemCache();
                string title = trim ? "Trim complete" : "Cache clear complete";
                ShowBalloon(title, BuildBalloonMessage(result), ResultIcon(result));
            }
            finally
            {
                _busy = false;
            }
        });
    }

    private static System.Windows.Forms.ToolTipIcon ResultIcon(CleanResult result)
        => result.Success ? System.Windows.Forms.ToolTipIcon.Info : System.Windows.Forms.ToolTipIcon.Warning;

    private static string BuildBalloonMessage(CleanResult result)
    {
        if (!result.Success)
        {
            return result.Message;
        }

        return result.BytesFreed > 0
            ? $"{result.Message}, freed {ByteFormat.Megabytes(result.BytesFreed)}"
            : result.Message;
    }

    private void ShowBalloon(string title, string message, System.Windows.Forms.ToolTipIcon icon)
    {
        InvokeOnUiThread(() => _notifyIcon?.ShowBalloonTip(BalloonTimeoutMs, title, message, icon));
    }

    private void ExitApplication()
    {
        // Belt-and-suspenders: mark the window as really-exiting (in case Close() runs this
        // session for any other reason) but shut down directly rather than routing through
        // Window.Close() — robust regardless of whether the window is currently hidden.
        _window?.AllowRealExit();
        Dispose();
        System.Windows.Application.Current?.Shutdown();
    }

    /// <summary>Runs <paramref name="action"/> on the UI dispatcher thread, inline if already on it.</summary>
    private static void InvokeOnUiThread(Action action)
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    /// <summary>
    /// Loads the real app icon (Assets/app.ico, same one the exe and window use) from the WPF
    /// resource stream; falls back to the original drawn badge if the resource is unavailable.
    /// Built once at startup and cached.
    /// </summary>
    private static System.Drawing.Icon BuildIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico");
            using var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is not null)
            {
                // The size hint picks the closest embedded entry (the .ico carries 16-256px).
                return new System.Drawing.Icon(stream, 32, 32);
            }
        }
        catch
        {
            // Resource missing/corrupt — fall through to the drawn fallback below.
        }

        return DrawFallbackIcon();
    }

    /// <summary>
    /// Draws a small accent-colored rounded-square icon with a white "C" glyph, matching the
    /// title bar badge look. Only used if the packed app.ico resource cannot be loaded.
    /// </summary>
    private static System.Drawing.Icon DrawFallbackIcon()
    {
        const int size = 32;

        using var bitmap = new System.Drawing.Bitmap(size, size);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // Brush.Accent (#8286F5) from Themes/GameTev.xaml.
            using var accentBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0x82, 0x86, 0xF5));
            using System.Drawing.Drawing2D.GraphicsPath path = RoundedRect(new System.Drawing.Rectangle(1, 1, size - 2, size - 2), radius: 9);
            g.FillPath(accentBrush, path);

            using var glyphBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            using var font = new System.Drawing.Font("Segoe UI", size * 0.55f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            var format = new System.Drawing.StringFormat
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center,
            };
            g.DrawString("C", font, glyphBrush, new System.Drawing.RectangleF(0, 0, size, size), format);
        }

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            // Icon.FromHandle wraps the handle without owning it, so Clone() takes a managed
            // copy the Icon instance truly owns before the raw HICON is destroyed below —
            // otherwise the GDI handle backing `temp` would leak.
            using System.Drawing.Icon temp = System.Drawing.Icon.FromHandle(hIcon);
            return (System.Drawing.Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(System.Drawing.Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Tears down the tray icon and its timer. Safe to call more than once.</summary>
    public void Dispose()
    {
        _tooltipTimer?.Stop();
        _tooltipTimer = null;

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _icon?.Dispose();
        _icon = null;
    }
}
