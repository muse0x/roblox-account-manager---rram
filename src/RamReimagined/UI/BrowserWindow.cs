using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using RamReimagined.Core;

namespace RamReimagined.UI;

/// <summary>
/// Port of the original manager's CefBrowser: one disposable browser window.
/// Login mode clears every cookie, loads the Roblox login page, watches for the
/// .ROBLOSECURITY cookie and adds the account without asking for the value.
/// Browser mode seeds a stored cookie and opens Roblox as that account.
/// Closing the window hides it and wipes the cookies, exactly like the original.
/// </summary>
internal sealed class BrowserWindow : Form
{
    private const string LoginUrl = "https://www.roblox.com/login";
    private const string HomeUrl = "https://www.roblox.com/home";

    private readonly WebView2 _web;
    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 900 };

    private CoreWebView2? _core;
    private bool _ready;
    private bool _initializing;
    private bool _browserMode;
    private bool _capturing;
    private string? _pendingUrl;
    private string? _injectCookie;
    private bool _clearCookies;

    static BrowserWindow()
    {
        Instance = new BrowserWindow();
    }

    private BrowserWindow()
    {
        Text = "Roblox sign-in · RRAM";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(900, 760);
        MinimumSize = new Size(560, 480);
        BackColor = Theme.Backdrop;
        ShowInTaskbar = true;
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception)
        {
            // The window icon is cosmetic.
        }

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Backdrop,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Backdrop };

        _status = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = Theme.Font(9f),
            ForeColor = Theme.Muted,
            BackColor = Theme.Panel,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 14, 0),
            Text = "Sign in to Roblox. The account is added automatically, then this browser session is cleared."
        };

        layout.Controls.Add(_web, 0, 0);
        layout.Controls.Add(_status, 0, 1);
        Controls.Add(layout);

        FormClosing += (_, e) =>
        {
            // The original hides instead of closing, and clears the cookie jar.
            e.Cancel = true;
            Hide();
            _poll.Stop();
            _browserMode = false;
            ClearCookies();
        };

        _poll.Tick += async (_, _) =>
        {
            if (Visible && !_browserMode)
            {
                await TryCaptureAsync();
            }
        };
    }

    public static BrowserWindow Instance { get; }

    /// <summary>Raised after a session from the browser has been stored.</summary>
    public event EventHandler? AccountAdded;

    public void Login()
    {
        _browserMode = false;
        _injectCookie = null;
        _pendingUrl = LoginUrl;
        _clearCookies = true;
        _status.Text = "Sign in to Roblox. The account is added automatically, then this browser session is cleared.";
        Present();
    }

    public void EnterBrowserMode(Account account, string? url = null)
    {
        var cookie = AppState.Current.Store.CookieFor(account);
        if (string.IsNullOrWhiteSpace(cookie))
        {
            AppState.Current.Toast("That session is not readable on this Windows account.", ToastKind.Warning);
            return;
        }

        _browserMode = true;
        _injectCookie = cookie;
        _pendingUrl = string.IsNullOrWhiteSpace(url) ? HomeUrl : url;
        _clearCookies = true;
        _status.Text = $"Browsing as {account.Name}. Closing this window wipes the cookie.";
        Present();
    }

    private void Present()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        BringToFront();
        Activate();
        _ = EnsureCoreAsync();
        if (_ready)
        {
            ApplyPending();
        }
    }

    private async Task EnsureCoreAsync()
    {
        if (_ready || _initializing || IsDisposed)
        {
            return;
        }

        _initializing = true;
        try
        {
            var folder = Path.Combine(AppPaths.DataDirectory, "browser");
            Directory.CreateDirectory(folder);
            var environment = await CoreWebView2Environment.CreateAsync(null, folder);
            await _web.EnsureCoreWebView2Async(environment);

            _core = _web.CoreWebView2;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.AreDevToolsEnabled = true;
            _core.NavigationCompleted += OnNavigationCompleted;
            _ready = true;
            ApplyPending();
            _poll.Start();
        }
        catch (Exception ex)
        {
            _status.Text = "The embedded browser could not start. Install the Microsoft Edge WebView2 runtime. " + ex.Message;
        }
        finally
        {
            _initializing = false;
        }
    }

    private void ApplyPending()
    {
        if (!_ready || _core is null)
        {
            return;
        }

        if (_clearCookies)
        {
            _core.CookieManager.DeleteAllCookies();
            _clearCookies = false;
        }

        if (!string.IsNullOrWhiteSpace(_injectCookie))
        {
            InjectCookie(_injectCookie!);
            _injectCookie = null;
        }

        if (!string.IsNullOrWhiteSpace(_pendingUrl))
        {
            try
            {
                _web.Source = new Uri(_pendingUrl!);
            }
            catch (Exception)
            {
                _status.Text = "That address could not be opened.";
            }

            _pendingUrl = null;
        }
    }

    private void InjectCookie(string value)
    {
        if (_core is null)
        {
            return;
        }

        var manager = _core.CookieManager;
        var cookie = manager.CreateCookie(".ROBLOSECURITY", value, ".roblox.com", "/");
        cookie.Expires = DateTime.Now.AddYears(1);
        cookie.IsHttpOnly = true;
        cookie.IsSecure = true;
        manager.AddOrUpdateCookie(cookie);
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_ready || _browserMode || _capturing || !e.IsSuccess)
        {
            return;
        }

        var url = _web.Source?.ToString() ?? "";
        if (!url.Contains("roblox.com", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (url.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/signup", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await TryCaptureAsync();
    }

    private async Task TryCaptureAsync()
    {
        if (_core is null || _browserMode || _capturing || !Visible)
        {
            return;
        }

        try
        {
            var cookies = await _core.CookieManager.GetCookiesAsync("https://www.roblox.com");
            var token = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY")?.Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            _capturing = true;
            _status.Text = "Session found. Checking it with Roblox…";
            var added = await AddAccountAsync(token);
            _core.CookieManager.DeleteAllCookies();

            if (added)
            {
                _poll.Stop();
                Hide();
            }
            else
            {
                _browserMode = false;
                _status.Text = "Roblox rejected that session. Sign in again.";
            }
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _capturing = false;
        }
    }

    private async Task<bool> AddAccountAsync(string cookie)
    {
        var app = AppState.Current;
        try
        {
            var user = await app.Api.GetAuthenticatedAsync(cookie);
            if (user is null)
            {
                return false;
            }

            var existed = app.Store.FindByUserId(user.Id) is not null;
            app.Store.AddOrUpdate(user, cookie);
            app.Avatars.Forget(user.Id);
            app.NotifyAccountsChanged();
            app.Toast(existed ? $"Session refreshed for {user.DisplayName}." : $"Added {user.DisplayName}.",
                ToastKind.Success);
            AccountAdded?.Invoke(this, EventArgs.Empty);
            _ = app.RefreshPresenceAsync();
            return true;
        }
        catch (Exception ex)
        {
            app.Toast(ex.Message, ToastKind.Error);
            return false;
        }
    }

    private void ClearCookies()
    {
        try
        {
            _core?.CookieManager.DeleteAllCookies();
        }
        catch (Exception)
        {
            // A cookie jar that is already gone is fine.
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        }
        catch (Exception)
        {
            // Dark title bars are cosmetic; older Windows builds do not support the attribute.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _poll.Dispose();
        }

        base.Dispose(disposing);
    }
}
