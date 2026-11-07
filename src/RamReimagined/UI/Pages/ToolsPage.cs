using System.Diagnostics;
using RamReimagined.Core;

namespace RamReimagined.UI;

internal sealed class ToolsPage : PageView
{
    private readonly StackPanel _stack = new() { Dock = DockStyle.Fill, Gap = 12 };
    private readonly ToggleSwitch _multi;
    private readonly Label _multiStatus = new();

    public ToolsPage(AppState app) : base(app)
    {
        _multi = new ToggleSwitch { Checked = app.Launcher.MultiInstance.Active };
        _multi.CheckedChanged += (_, _) => ToggleMultiInstance();

        _multiStatus.AutoSize = false;
        _multiStatus.Font = Theme.Font(8.5f);
        _multiStatus.ForeColor = Theme.Faint;
        _multiStatus.BackColor = Theme.Card;
        _multiStatus.Text = app.Launcher.MultiInstance.Active ? "Singleton objects held" : "Singleton objects released";
        _multiStatus.Size = new Size(184, 24);

        var lowGraphics = new PillButton("Lowest graphics", IconKind.Sliders) { Kind = ButtonKind.Subtle, Width = 182, Height = 36 };
        lowGraphics.Click += (_, _) => ApplyLowGraphics();
        var flags = new PillButton("Apply fast flags", IconKind.Bolt) { Kind = ButtonKind.Subtle, Width = 180, Height = 36 };
        flags.Click += (_, _) => ApplyFlags();
        var clear = new PillButton("Clear", IconKind.Trash) { Kind = ButtonKind.Ghost, Width = 92, Height = 36 };
        clear.Click += (_, _) => ClearFlags();

        var validate = new PillButton("Refresh presence", IconKind.Refresh) { Kind = ButtonKind.Subtle, Width = 194, Height = 36 };
        validate.Click += async (_, _) =>
        {
            await App.RefreshPresenceAsync();
            App.Toast("Presence refreshed.", ToastKind.Success);
        };
        var openData = new PillButton("Data folder", IconKind.Folder) { Kind = ButtonKind.Ghost, Width = 126, Height = 36 };
        openData.Click += (_, _) => OpenFolder(AppPaths.DataDirectory);

        var tile = new PillButton("Tile clients", IconKind.Grid) { Kind = ButtonKind.Subtle, Width = 136, Height = 36 };
        tile.Click += (_, _) =>
        {
            var count = WindowTiler.Tile();
            App.Toast(count == 0 ? "No running clients found." : $"Tiled {count} client windows.",
                count == 0 ? ToastKind.Warning : ToastKind.Success);
        };
        var openRoblox = new PillButton("Roblox folder", IconKind.Folder) { Kind = ButtonKind.Ghost, Width = 146, Height = 36 };
        openRoblox.Click += (_, _) => OpenFolder(AppPaths.LocalRobloxDirectory);

        _stack.Controls.Add(BuildCard("Multiple clients",
            "Holds the ROBLOX_singletonMutex and ROBLOX_singletonEvent kernel objects that a Windows session shares, so a second client can start. Nothing is injected into Roblox.",
            170, _multiStatus, _multi));
        _stack.Controls.Add(BuildCard("Performance flags",
            "Writes ClientAppSettings.json into the selected launch build. A backup of any previous file is kept beside it, and clears restore the vanilla behaviour.",
            170, lowGraphics, flags, clear));
        _stack.Controls.Add(BuildCard("Sessions and data",
            "Sessions are stored with DPAPI for this Windows user only. Presence is refreshed on a timer while the app is open.",
            170, validate, openData));
        _stack.Controls.Add(BuildCard("Windows",
            "Give every running client its own cell in a 16:9-ish grid, or jump straight to the folders the app uses.",
            170, tile, openRoblox));

        Controls.Add(_stack);
    }

    public override string NavLabel => "Tools";

    public override IconKind NavIcon => IconKind.Bolt;

    public override string Heading => "Tools";

    public override string Subheading => "Local controls that used to live in scattered dialogs, now in one place.";

    private RoundedCard BuildCard(string title, string description, int height, params Control[] actions)
    {
        var card = new RoundedCard { Height = height };

        var titleLabel = new SectionTitle(title) { Location = new Point(20, 18) };
        var descriptionLabel = new MutedLabel(description) { Location = new Point(20, 46), Size = new Size(620, 56) };

        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Card,
            Height = 40
        };

        foreach (var action in actions)
        {
            action.Margin = new Padding(8, 0, 0, 0);
            flow.Controls.Add(action);
        }

        card.Controls.Add(titleLabel);
        card.Controls.Add(descriptionLabel);
        card.Controls.Add(flow);

        card.Resize += (_, _) =>
        {
            descriptionLabel.Width = Math.Max(180, card.Width - 40);
            flow.Location = new Point(card.Width - flow.Width - 20, card.Height - flow.Height - 14);
        };

        return card;
    }

    private void ToggleMultiInstance()
    {
        var message = _multi.Checked ? App.Launcher.MultiInstance.Enable() : Release();
        _multiStatus.Text = App.Launcher.MultiInstance.Active ? "Singleton objects held" : "Singleton objects released";
        App.Settings.KeepMultiInstance = _multi.Checked;
        App.Settings.Save();
        App.Toast(message, _multi.Checked ? ToastKind.Success : ToastKind.Info);
    }

    private string Release()
    {
        App.Launcher.MultiInstance.Disable();
        return "Released the singleton objects.";
    }

    private void ApplyLowGraphics()
    {
        var ok = RobloxSettings.TrySetGraphicsQuality(1, out var message);
        App.Toast(message, ok ? ToastKind.Success : ToastKind.Warning);
    }

    private void ApplyFlags()
    {
        var path = RobloxSettings.ApplyFastFlags(RobloxSettings.Presets().ToDictionary(kv => kv.Key, kv => (string)kv.Value), out var message, App.Settings.LaunchVersion);
        App.Toast(path is null ? message : $"{message} Restart clients to apply.", path is null ? ToastKind.Warning : ToastKind.Success);
    }

    private void ClearFlags()
    {
        RobloxSettings.ApplyFastFlags(new Dictionary<string, string>(), out var message, App.Settings.LaunchVersion);
        App.Toast(message, ToastKind.Info);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening Explorer can fail if the path disappeared; the app stays usable.
        }
    }
}
