using RamReimagined.Core;

namespace RamReimagined.UI;

internal sealed class VersionsPage : PageView
{
    private readonly RobloxVersionManager Manager = new();
    private readonly StackPanel Stack = new() { Dock = DockStyle.Fill, Gap = 12 };
    private readonly FlowLayoutPanel InstalledList = new();
    private readonly Label Current = new();
    private readonly PillButton RefreshAction = new("Refresh", IconKind.Refresh)
        { Kind = ButtonKind.Ghost, Width = 110, Height = 36 };
    private readonly Label LatestInfo = new();
    private readonly Label PreviousInfo = new();
    private readonly PillButton InstallLatest = new("Install", IconKind.Folder)
        { Kind = ButtonKind.Subtle, Width = 102, Height = 34 };
    private readonly PillButton InstallPrevious = new("Install", IconKind.Folder)
        { Kind = ButtonKind.Subtle, Width = 102, Height = 34 };
    private readonly TextField VersionHash = new("version-...");
    private readonly PillButton InstallHash = new("Install version", IconKind.Folder)
        { Kind = ButtonKind.Primary, Width = 160, Height = 38 };
    private readonly VersionProgressBar Progress = new();
    private readonly Label Status = new();
    private readonly PillButton Cancel = new("Cancel", IconKind.Close)
        { Kind = ButtonKind.Ghost, Width = 100, Height = 34 };
    private List<InstalledRobloxVersion> InstalledVersions = new();
    private RobloxDeployment? Latest;
    private RobloxDeployment? Previous;
    private CancellationTokenSource? InstallCts;
    private bool Busy;
    private bool Refreshing;

    public VersionsPage(AppState app) : base(app)
    {
        InstalledList.FlowDirection = FlowDirection.TopDown;
        InstalledList.WrapContents = false;
        InstalledList.AutoScroll = true;
        InstalledList.BackColor = Theme.Card;
        InstalledList.Margin = Padding.Empty;
        InstalledList.Resize += (_, _) => ResizeInstalledRows();

        Current.AutoSize = false;
        Current.Font = Theme.Font(9.2f);
        Current.ForeColor = Theme.Muted;
        Current.BackColor = Theme.Card;
        Current.TextAlign = ContentAlignment.MiddleLeft;

        LatestInfo.AutoSize = false;
        LatestInfo.Font = Theme.Font(9.2f);
        LatestInfo.ForeColor = Theme.Text;
        LatestInfo.BackColor = Theme.Card;
        LatestInfo.TextAlign = ContentAlignment.MiddleLeft;
        LatestInfo.AutoEllipsis = true;
        PreviousInfo.AutoSize = false;
        PreviousInfo.Font = Theme.Font(9.2f);
        PreviousInfo.ForeColor = Theme.Text;
        PreviousInfo.BackColor = Theme.Card;
        PreviousInfo.TextAlign = ContentAlignment.MiddleLeft;
        PreviousInfo.AutoEllipsis = true;

        Status.AutoSize = false;
        Status.Font = Theme.Font(9f);
        Status.ForeColor = Theme.Muted;
        Status.BackColor = Theme.Card;
        Status.TextAlign = ContentAlignment.MiddleLeft;
        Status.Text = "Ready.";

        InstallLatest.Click += async (_, _) => await InstallAsync(Latest?.VersionHash);
        InstallPrevious.Click += async (_, _) => await InstallAsync(Previous?.VersionHash);
        InstallHash.Click += async (_, _) => await InstallAsync(VersionHash.Text);
        Cancel.Click += (_, _) => InstallCts?.Cancel();
        Cancel.Visible = false;
        RefreshAction.Click += async (_, _) => await RefreshAllAsync();

        Stack.Controls.Add(BuildInstalledCard());
        Stack.Controls.Add(BuildQuickInstallCard());
        Stack.Controls.Add(BuildSpecificCard());
        Controls.Add(Stack);

        RefreshInstalled();
    }

    public override string NavLabel => "Versions";

    public override IconKind NavIcon => IconKind.Folder;

    public override string Heading => "Versions";

    public override string Subheading => "Run an installed build or install a downgrade straight from RDD.";

    public override IReadOnlyList<Control> Actions => new Control[] { RefreshAction };

    public override void Activated()
    {
        RefreshInstalled();
        if (Latest is null || Previous is null)
        {
            _ = RefreshAllAsync();
        }
    }

    private RoundedCard BuildInstalledCard()
    {
        var Card = new RoundedCard { Height = 266 };
        var Title = new SectionTitle("Installed versions") { Location = new Point(20, 18) };
        var Hint = new MutedLabel(
            "Click a build to use it for launches. Auto uses the newest installed.")
        { Location = new Point(20, 44), Size = new Size(620, 28) };

        Card.Controls.Add(Title);
        Card.Controls.Add(Hint);
        Card.Controls.Add(InstalledList);
        Card.Controls.Add(Current);

        Card.Resize += (_, _) =>
        {
            Hint.Width = Math.Max(200, Card.Width - 40);
            InstalledList.SetBounds(20, 74, Math.Max(200, Card.Width - 40), 138);
            Current.SetBounds(20, 222, Math.Max(200, Card.Width - 40), 28);
            ResizeInstalledRows();
        };

        return Card;
    }

    private RoundedCard BuildQuickInstallCard()
    {
        var Card = new RoundedCard { Height = 166 };
        var Title = new SectionTitle("RDD deployments") { Location = new Point(20, 18) };
        var Hint = new MutedLabel("Current and previous LIVE builds from weao.gg.")
        { Location = new Point(20, 44), Size = new Size(620, 28) };

        Card.Controls.Add(Title);
        Card.Controls.Add(Hint);
        Card.Controls.Add(LatestInfo);
        Card.Controls.Add(PreviousInfo);
        Card.Controls.Add(InstallLatest);
        Card.Controls.Add(InstallPrevious);

        Card.Resize += (_, _) =>
        {
            Hint.Width = Math.Max(200, Card.Width - 40);
            LatestInfo.SetBounds(20, 74, Math.Max(160, Card.Width - 160), 30);
            PreviousInfo.SetBounds(20, 112, Math.Max(160, Card.Width - 160), 30);
            InstallLatest.Location = new Point(Card.Width - InstallLatest.Width - 20, 72);
            InstallPrevious.Location = new Point(Card.Width - InstallPrevious.Width - 20, 110);
        };

        return Card;
    }

    private RoundedCard BuildSpecificCard()
    {
        var Card = new RoundedCard { Height = 214 };
        var Title = new SectionTitle("Specific version") { Location = new Point(20, 18) };
        var Hint = new MutedLabel("Paste a version hash; the app downloads and unpacks it.")
        { Location = new Point(20, 44), Size = new Size(620, 28) };

        Card.Controls.Add(Title);
        Card.Controls.Add(Hint);
        Card.Controls.Add(VersionHash);
        Card.Controls.Add(InstallHash);
        Card.Controls.Add(Progress);
        Card.Controls.Add(Status);
        Card.Controls.Add(Cancel);

        Card.Resize += (_, _) =>
        {
            Hint.Width = Math.Max(200, Card.Width - 40);
            VersionHash.SetBounds(20, 74, Math.Max(160, Card.Width - 220), 38);
            InstallHash.Location = new Point(Card.Width - InstallHash.Width - 20, 74);
            Progress.SetBounds(20, 128, Math.Max(120, Card.Width - 40), 8);
            Status.SetBounds(20, 146, Math.Max(160, Card.Width - 150), 28);
            Cancel.Location = new Point(Card.Width - Cancel.Width - 20, 168);
        };

        return Card;
    }

    private async Task RefreshAllAsync()
    {
        if (Busy || Refreshing)
        {
            return;
        }

        Refreshing = true;
        RefreshInstalled();
        RefreshAction.Busy = true;
        Status.Text = "Loading deployments";
        try
        {
            var LatestTask = Manager.GetLatestAsync(false);
            var PreviousTask = Manager.GetLatestAsync(true);
            await Task.WhenAll(LatestTask, PreviousTask);
            Latest = LatestTask.Result;
            Previous = PreviousTask.Result;
            LatestInfo.Text = Describe("Latest", Latest);
            PreviousInfo.Text = Describe("Previous", Previous);
            Status.Text = "Deployments loaded.";
        }
        catch (Exception Exception)
        {
            LatestInfo.Text = "Latest: unavailable";
            PreviousInfo.Text = "Previous: unavailable";
            Status.Text = Exception.Message;
            App.Toast($"Could not load RDD deployments: {Exception.Message}", ToastKind.Warning);
        }
        finally
        {
            Refreshing = false;
            RefreshAction.Busy = false;
            UpdateButtons();
        }
    }

    private void RefreshInstalled()
    {
        var SelectedFolder = App.Settings.LaunchVersion ?? "";
        InstalledVersions = Manager.GetInstalled().ToList();

        if (SelectedFolder.Length > 0 && !InstalledVersions.Any(Version =>
                string.Equals(Version.Folder, SelectedFolder, StringComparison.OrdinalIgnoreCase)))
        {
            App.Settings.LaunchVersion = "";
            App.Settings.Save();
        }

        RebuildInstalledRows();
        UpdateCurrentLabel();
    }

    private void RebuildInstalledRows()
    {
        InstalledList.SuspendLayout();
        foreach (Control Row in InstalledList.Controls)
        {
            Row.Dispose();
        }
        InstalledList.Controls.Clear();

        var SelectedFolder = App.Settings.LaunchVersion ?? "";
        InstalledList.Controls.Add(CreateInstalledRow("Auto (newest installed build)",
            SelectedFolder.Length == 0, null, "the newest installed build"));

        foreach (var Version in InstalledVersions)
        {
            var Folder = Version.Folder;
            var Label = Version.Display;
            InstalledList.Controls.Add(CreateInstalledRow(Label,
                string.Equals(Folder, SelectedFolder, StringComparison.OrdinalIgnoreCase), Folder, Label));
        }

        InstalledList.ResumeLayout();
        ResizeInstalledRows();
    }

    private PillButton CreateInstalledRow(string Text, bool Selected, string? Folder, string ToastLabel)
    {
        var Row = new PillButton(Text)
        {
            Kind = Selected ? ButtonKind.Primary : ButtonKind.Subtle,
            Height = 38,
            Width = Math.Max(200, InstalledList.ClientSize.Width - 4),
            Margin = new Padding(0, 0, 0, 6)
        };
        Row.Click += (_, _) => SelectVersion(Folder, ToastLabel);
        return Row;
    }

    private void ResizeInstalledRows()
    {
        var RowWidth = Math.Max(200, InstalledList.ClientSize.Width - 4);
        foreach (Control Row in InstalledList.Controls)
        {
            Row.Width = RowWidth;
        }
    }

    private void SelectVersion(string? Folder, string Label)
    {
        App.Settings.LaunchVersion = Folder ?? "";
        App.Settings.Save();
        RebuildInstalledRows();
        UpdateCurrentLabel();
        App.Toast($"Launching with {Label}.", ToastKind.Info);
    }

    private void UpdateCurrentLabel()
    {
        var SelectedFolder = App.Settings.LaunchVersion ?? "";
        var Selected = InstalledVersions.Find(Version =>
            string.Equals(Version.Folder, SelectedFolder, StringComparison.OrdinalIgnoreCase));
        Current.Text = Selected is null
            ? "Launch build: Auto (newest installed)"
            : $"Launch build: {Selected.Display}";
        Current.ForeColor = Selected is null ? Theme.Muted : Theme.Text;
    }

    private async Task InstallAsync(string? RawVersion)
    {
        if (Busy)
        {
            return;
        }

        var Version = RobloxVersionManager.NormalizeVersion(RawVersion);
        if (Version is null)
        {
            App.Toast("Enter a version hash such as version-f0ea5da9412f4e35.", ToastKind.Warning);
            VersionHash.FocusInput();
            return;
        }

        Busy = true;
        SetBusy(true);
        InstallCts = new CancellationTokenSource();
        Progress.Value = 0;
        Status.Text = "Preparing install";

        try
        {
            var Reporter = new Progress<VersionInstallProgress>(UpdateProgress);
            var Installed = await Manager.InstallAsync(Version, Reporter, InstallCts.Token);
            App.Settings.LaunchVersion = Installed.Folder;
            App.Settings.Save();
            RefreshInstalled();
            Status.Text = $"Installed {Installed.Folder}.";
            App.Toast($"Installed {Installed.Display}. It is now the launch build.", ToastKind.Success);
        }
        catch (OperationCanceledException)
        {
            Status.Text = "Install cancelled.";
            App.Toast("Version install cancelled.", ToastKind.Info);
        }
        catch (Exception Exception)
        {
            Status.Text = Exception.Message;
            App.Toast(Exception.Message, ToastKind.Error);
        }
        finally
        {
            Busy = false;
            InstallCts.Dispose();
            InstallCts = null;
            SetBusy(false);
        }
    }

    private void UpdateProgress(VersionInstallProgress ProgressValue)
    {
        if (IsDisposed)
        {
            return;
        }

        Progress.Value = ProgressValue.Fraction;
        Status.Text = ProgressValue.TotalBytes > 0
            ? $"{ProgressValue.Message}  {FormatBytes(ProgressValue.DownloadedBytes)} / {FormatBytes(ProgressValue.TotalBytes)}"
            : ProgressValue.Message;
    }

    private void SetBusy(bool Value)
    {
        InstalledList.Enabled = !Value;
        InstallLatest.Enabled = !Value && Latest is not null;
        InstallPrevious.Enabled = !Value && Previous is not null;
        InstallHash.Enabled = !Value;
        VersionHash.Enabled = !Value;
        RefreshAction.Enabled = !Value;
        Cancel.Visible = Value;
    }

    private void UpdateButtons()
    {
        if (Busy)
        {
            return;
        }

        InstallLatest.Enabled = Latest is not null;
        InstallPrevious.Enabled = Previous is not null;
        InstallHash.Enabled = true;
    }

    private static string Describe(string Label, RobloxDeployment? Deployment)
    {
        if (Deployment is null)
        {
            return $"{Label}: ";
        }

        var Date = Deployment.PublishedAt is null
            ? ""
            : $"  |  {Deployment.PublishedAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}";
        return $"{Label}: {Deployment.VersionNumber}  |  {Deployment.VersionHash}{Date}";
    }

    private static string FormatBytes(long Bytes)
    {
        if (Bytes <= 0)
        {
            return "0 B";
        }

        string[] Units = { "B", "KB", "MB", "GB" };
        var Index = 0;
        double Value = Bytes;
        while (Value >= 1024 && Index < Units.Length - 1)
        {
            Value /= 1024;
            Index++;
        }

        return $"{Value:0.#} {Units[Index]}";
    }

    protected override void Dispose(bool Disposing)
    {
        if (Disposing)
        {
            InstallCts?.Cancel();
            InstallCts?.Dispose();
        }

        base.Dispose(Disposing);
    }
}

internal sealed class VersionProgressBar : Control
{
    private double CurrentValue;

    public VersionProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 8;
    }

    public double Value
    {
        get => CurrentValue;
        set
        {
            CurrentValue = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var Graphics = e.Graphics;
        Theme.EnableQuality(Graphics);
        var Track = new Rectangle(0, 0, Width - 1, Height - 1);
        var Radius = Math.Max(1, Height / 2);
        Theme.FillRounded(Graphics, Track, Radius, Theme.CardHover);

        var FillWidth = (int)Math.Round(Track.Width * CurrentValue);
        if (FillWidth > 0)
        {
            Theme.FillRounded(Graphics, new Rectangle(Track.X, Track.Y, FillWidth, Track.Height),
                Radius, Theme.Accent);
        }
    }
}
