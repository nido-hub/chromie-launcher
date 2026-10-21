using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChromieLauncher.Core;
using static ChromieLauncher.Core.LauncherText;

namespace ChromieLauncher.Desktop;

public sealed class MainWindow : Window
{
    private static readonly IBrush Muted = LauncherTheme.Muted;
    private static readonly IBrush Surface = LauncherTheme.Surface;
    private static readonly IBrush Line = LauncherTheme.Border;
    private readonly Storage _storage = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly Catalog _catalog = CatalogService.Load();
    private readonly Settings _settings;
    private readonly AddonService _addons;
    private readonly UpdateService _updater;
    private readonly ClientPatchService _patches;
    private readonly AddonUpdateService _addonUpdates;
    private List<AddonUpdate> _availableAddonUpdates = [];
    private AddonUpdateCheck? _lastAddonCheck;
    private PatchStatus _patchStatus = new(false, null);
    private readonly Grid _root = new() { RowDefinitions = new("76,*"), Background = LauncherTheme.Background, ClipToBounds = true };
    private readonly StackPanel _navigation = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly DockPanel _body = new() { LastChildFill = true };
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly TextBlock _heading = Heading(Get("My addons"), 34);
    private readonly TextBlock _subtitle = Text("", 15, color: Muted);
    private readonly TextBlock _status = Text(Get("Ready"), 13, color: Muted);
    private readonly TextBlock _count = Text("", 13, color: Muted);
    private readonly TextBox _search = new() { Watermark = Get("Search addons…"), MinWidth = 260, CornerRadius = new(2) };
    private readonly ComboBox _category = new() { MinWidth = 180, CornerRadius = new(2) };
    private readonly Button _play = Button("Play ChromieCraft", "primary");
    private readonly Button _refresh = Button("Refresh");
    private readonly Button _updateAllAddons = Button("Update all addons", "primary");
    private readonly Button _update = Button("Updates");
    private readonly Button _cancel = Button("Cancel");
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, IsVisible = false };
    private readonly List<string> _history = [];
    private readonly List<Button> _navButtons = [];
    private List<InstalledAddon> _installed = [];
    private ManagedState _managed = new();
    private string _page = "My addons";
    private bool _busy;
    private CancellationTokenSource? _operation;
    private string? _preparedUpdate;
    private Grid? _stats;
    private TextBlock? _eyebrow;
    private TextBlock? _footer;
    private static readonly string[] LanguageCodes = ["en", "fr", "it"];

    public MainWindow()
    {
        Title = "Chromie Launcher";
        Icon = LauncherTheme.ApplicationIcon;
        Width = 1280; Height = 900; MinWidth = 1100; MinHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = LauncherTheme.Background;
        FontFamily = LauncherTheme.BodyFont;
        Foreground = LauncherTheme.Text;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ChromieLauncher/" + UpdateService.DisplayVersion);
        _settings = _storage.LoadSettings();
        _settings.Language = NormalizeLanguage(_settings.Language);
        SetLanguage(_settings.Language);
        if (string.IsNullOrEmpty(_settings.GamePath)) _settings.GamePath = DetectGame();
        _addons = new(_storage, _http, _catalog);
        _updater = new(_storage, _http);
        _patches = new(_storage, new MegaClient(_http));
        _addonUpdates = new(_http);
        BuildLayout();
        ApplyLanguage();
        Content = _root;
        _search.TextChanged += (_, _) => RenderList();
        _category.SelectionChanged += (_, _) => RenderList();
        _refresh.Click += async (_, _) => await CheckAddonUpdates();
        _updateAllAddons.Click += async (_, _) => await UpdateAllAddons();
        _play.Click += (_, _) => LaunchGame();
        _update.Click += async (_, _) => await CheckUpdates(false);
        _cancel.Click += (_, _) => _operation?.Cancel();
        Closing += (_, e) => { if (_busy) { e.Cancel = true; _status.Text = Get("An operation is in progress. Cancel the download or wait for it to finish."); } };
        Closed += (_, _) => { _http.Dispose(); _operation?.Dispose(); };
        Opened += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(_settings.GamePath)) await Run(Get("Scanning addons"), ReloadAsync);
            else Navigate("Settings");
            if (Program.SmokeTestOutput is null && !string.IsNullOrEmpty(_settings.GamePath)) await CheckAddonUpdates();
            if (_settings.CheckUpdatesAtStartup && !string.IsNullOrEmpty(_settings.UpdateFeedUrl)) await CheckUpdates(true);
            if (Program.SmokeTestOutput is not null)
                DispatcherTimer.RunOnce(() => _ = SmokeTestAsync(), TimeSpan.FromSeconds(2));
        };
        Navigate(_page);
    }

    private void BuildLayout()
    {
        var artwork = new Image { Source = LauncherTheme.Bitmap("lich-background.jpg"), Stretch = Stretch.UniformToFill, Opacity = 0.58 };
        Grid.SetRowSpan(artwork, 2); _root.Children.Add(artwork);
        var shade = new Border { Background = LauncherTheme.Shade((0, "#700E0E0E"), (0.35, "#700E0E0E"), (1, "#F20E0E0E")), IsHitTestVisible = false };
        Grid.SetRowSpan(shade, 2); _root.Children.Add(shade);

        var navbar = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new(28, 0) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 26, 0) };
        brand.Children.Add(LauncherTheme.Image("chromiecraft-emblem.png", 42, 48));
        var brandCopy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        brandCopy.Children.Add(Heading("Chromie Launcher", 19));
        brandCopy.Children.Add(Text("FOR CHROMIECRAFT  ·  3.3.5a", 10, color: LauncherTheme.Gold));
        brand.Children.Add(brandCopy); navbar.Children.Add(brand);
        foreach (var title in new[] { "My addons", "Library", "Client patches", "Downloads", "Settings" })
        {
            var button = Button(title, "nav");
            button.Tag = title;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => Navigate(title);
            _navButtons.Add(button); _navigation.Children.Add(button);
        }
        _navigation.HorizontalAlignment = HorizontalAlignment.Center;
        _navigation.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(_navigation, 1); navbar.Children.Add(_navigation);
        _update.VerticalAlignment = VerticalAlignment.Center; _update.Margin = new(20, 0, 0, 0);
        Grid.SetColumn(_update, 2); navbar.Children.Add(_update);
        _root.Children.Add(new Border { Background = Brush.Parse("#F20E0E0E"), BorderBrush = Brush.Parse("#685E4C"), BorderThickness = new(0, 0, 0, 1), Child = navbar });

        var content = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(38, 24, 38, 18) };
        Grid.SetRow(content, 1); _root.Children.Add(content);
        var header = new Grid { ColumnDefinitions = new("*,260"), MinHeight = 150, Margin = new(26, 16) };
        var titles = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var eyebrow = Text(Get("YOUR COMPANION IN AZEROTH"), 11, color: LauncherTheme.Gold);
        _eyebrow = eyebrow;
        titles.Children.Add(eyebrow);
        titles.Children.Add(_heading); _subtitle.TextWrapping = TextWrapping.Wrap; titles.Children.Add(_subtitle); header.Children.Add(titles);
        var headerActions = new StackPanel { Spacing = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var wordmark = LauncherTheme.Image("chromiecraft-wordmark.png", 230, 72);
        headerActions.Children.Add(wordmark);
        _play.HorizontalAlignment = HorizontalAlignment.Stretch; headerActions.Children.Add(_play);
        Grid.SetColumn(headerActions, 1); header.Children.Add(headerActions);
        content.Children.Add(new Border
        {
            Background = LauncherTheme.Shade((0, "#450E0E0E"), (1, "#D90E0E0E")),
            BorderBrush = Brush.Parse("#8A756044"), BorderThickness = new(1), CornerRadius = new(2),
            Margin = new(0, 0, 0, 20), Child = header
        });
        void UpdateDensity()
        {
            var compact = Bounds.Height > 0 ? Bounds.Height < 820 : Height < 820;
            header.MinHeight = compact ? 110 : 150;
            header.Margin = new(26, compact ? 10 : 16);
            wordmark.Height = compact ? 46 : 72;
            eyebrow.IsVisible = !compact;
            _heading.FontSize = compact ? 30 : 34;
            if (_stats is not null) _stats.IsVisible = !compact;
        }
        SizeChanged += (_, _) => UpdateDensity();
        UpdateDensity();
        Grid.SetRow(_body, 1); content.Children.Add(_body);
        var statusPanel = new Grid { RowDefinitions = new("Auto,Auto"), Margin = new(0, 16, 0, 0) };
        statusPanel.Children.Add(_progress);
        var bottom = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 20, Margin = new(0, 10, 0, 0) };
        _status.TextWrapping = TextWrapping.Wrap; bottom.Children.Add(_status);
        _cancel.IsVisible = false; Grid.SetColumn(_cancel, 1); bottom.Children.Add(_cancel);
        var footer = Text("", 11, color: Muted);
        _footer = footer;
        footer.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(footer, 2); bottom.Children.Add(footer);
        Grid.SetRow(bottom, 1); statusPanel.Children.Add(bottom);
        Grid.SetRow(statusPanel, 2); content.Children.Add(statusPanel);
    }

    private void ApplyLanguage()
    {
        foreach (var button in _navButtons) button.Content = Get((string)button.Tag!);
        _play.Content = Get("Play ChromieCraft");
        _refresh.Content = Get("Refresh");
        ToolTip.SetTip(_refresh, Get("Scan installed addons and check their sources online"));
        _update.Content = Get(_preparedUpdate is null ? "Updates" : "Restart");
        if (_preparedUpdate is not null) ToolTip.SetTip(_update, Get("Restart to apply the launcher update"));
        _cancel.Content = Get("Cancel");
        _search.Watermark = Get("Search addons…");
        _eyebrow!.Text = Get("YOUR COMPANION IN AZEROTH");
        _footer!.Text = "v" + UpdateService.DisplayVersion + "  ·  " + Get("Independent project, not affiliated with ChromieCraft");
        _status.Text = Get("Ready");
    }

    private void Navigate(string page)
    {
        if (_busy) return;
        _page = page; _heading.Text = Get(page);
        foreach (var button in _navButtons) button.Classes.Set("selected", (string?)button.Tag == page);
        _subtitle.Text = Get(page switch
        {
            "My addons" => "Your interface, your addons. All in one place.",
            "Library" => "Discover WoW 3.3.5a addons and ChromieCraft community projects.",
            "Client patches" => "Install the right Patch Y variant for your WoW 3.3.5a client.",
            "Downloads" => "Session activity and access to your backups.",
            _ => "Your game installation and launcher preferences."
        });
        _body.Children.Clear();
        // Controls are reused between pages; detach them before constructing the next page.
        if (_search.Parent is Panel oldToolbar) oldToolbar.Children.Clear();
        if (_refresh.Parent is Panel oldRefreshParent) oldRefreshParent.Children.Clear();
        if (_updateAllAddons.Parent is Panel oldUpdateParent) oldUpdateParent.Children.Clear();
        if (_list.Parent is ScrollViewer oldScroll) oldScroll.Content = null;
        if (page == "Settings") { RenderSettings(); return; }
        if (page == "Downloads") { RenderDownloads(); return; }
        if (page == "Client patches") { RenderClientPatches(); return; }
        RenderAddonPage();
    }

    private void RenderClientPatches()
    {
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(Notice("CHOOSE YOUR CLIENT TYPE", "Use the non-HD patch for a non-HD client, or the HD patch for an HD client. Client type is not detected automatically. Both variants install as Data\\patch-y.mpq; only one variant can be active. Close WoW before installation."));
        var installed = string.IsNullOrWhiteSpace(_settings.GamePath) ? "Select your game folder in Settings before installing a client patch."
            : _patchStatus.VariantId is { } id ? Format("{0} is installed.", Get(ClientPatchCatalog.All.First(p => p.Id == id).Name))
            : _patchStatus.Exists ? "An existing patch-y.mpq was found (unrecognized version). It will be backed up before replacement."
            : "No Patch Y detected in the selected client's Data folder.";
        panel.Children.Add(Text(Get(installed), 14, color: _patchStatus.Exists ? LauncherTheme.Gold : Muted));
        foreach (var patch in ClientPatchCatalog.All)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 24 };
            var copy = new StackPanel { Spacing = 9 };
            copy.Children.Add(Heading(Get(patch.Name), 21, LauncherTheme.Gold));
            var description = Text(Get(patch.Hd ? "For clients using HD graphics modifications only." : "For the standard client without HD graphics modifications."), 14, color: Muted);
            description.TextWrapping = TextWrapping.Wrap; copy.Children.Add(description);
            copy.Children.Add(Text(Format("MEGA download · {0:F2} MiB · authenticated + pinned SHA-256", patch.Size / 1048576d), 12, color: Muted));
            copy.Children.Add(Text(Format("Destination: {0}", "Data\\" + ClientPatchCatalog.FileName), 13));
            var source = Button("View MEGA source"); source.Click += (_, _) => OpenFolder(patch.ShareUrl); copy.Children.Add(source);
            row.Children.Add(copy);
            var current = _patchStatus.VariantId == patch.Id;
            var action = Button(current ? "Installed" : patch.Hd ? "Install HD" : "Install non-HD", current ? null : "primary");
            action.IsEnabled = !current && !string.IsNullOrWhiteSpace(_settings.GamePath); action.MinWidth = 160; action.VerticalAlignment = VerticalAlignment.Center;
            action.Click += async (_, _) => await InstallClientPatch(patch);
            Grid.SetColumn(action, 1); row.Children.Add(action); panel.Children.Add(Card(row));
        }
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        var refresh = Button("Refresh status"); refresh.Margin = new(0, 0, 10, 10);
        refresh.Click += async (_, _) => await Run(Get("Checking client patches"), ReloadAsync);
        var data = Button("Open Data folder"); data.Margin = new(0, 0, 10, 10);
        data.Click += (_, _) => { try { OpenFolder(ClientPatchService.DataDirectory(_settings.GamePath)); } catch (Exception e) { ReportError(e); } };
        var backups = Button("Open patch backups"); backups.Margin = new(0, 0, 0, 10);
        backups.Click += (_, _) =>
        {
            try
            {
                var folder = ClientPatchService.BackupDirectory(_settings.GamePath);
                if (Directory.Exists(folder)) OpenFolder(folder); else _status.Text = Get("No client patch backups yet.");
            }
            catch (Exception e) { ReportError(e); }
        };
        actions.Children.Add(refresh); actions.Children.Add(data); actions.Children.Add(backups); panel.Children.Add(actions);
        panel.Children.Add(Notice("SAFE INSTALLATION", "Existing Patch Y files are preserved in Interface\\.chromie-launcher\\patch-transactions. Other Data files, addons and WTF settings are not changed. Interrupted replacements are recovered before the next explicit installation."));
        _body.Children.Add(new ScrollViewer { Content = panel });
    }

    private async Task InstallClientPatch(ClientPatch patch)
    {
        if (_busy) return;
        try
        {
            var game = AddonService.ValidateGame(_settings.GamePath);
            var data = ClientPatchService.DataDirectory(game);
            var target = SafePath.Child(data, ClientPatchCatalog.FileName);
            var message = Format("Install {0}?\n\nDownload: {1:F2} MiB from MEGA\nDestination: {2}\n\n", Get(patch.Name), patch.Size / 1048576d, target);
            message += Get(File.Exists(target) ? "The existing patch-y.mpq will be backed up and replaced. The HD and non-HD variants cannot be installed together.\n\n" : "No other Data files will be changed.\n\n");
            message += Get("Close World of Warcraft first. Choosing the wrong client variant may cause game errors. This patch is user-provided, not an official launcher update.");
            if (!await Confirm("Install client patch", message, "Back up and install", patch.Hd ? "I confirm this is an HD client." : "I confirm this is a non-HD client.")) return;
            await Run(Format("Installing {0}", Get(patch.Name)), async () =>
            {
                await _patches.InstallAsync(game, patch, new Progress<string>(text => _status.Text = text), _operation!.Token);
                await ReloadAsync();
            }, cancellable: true);
        }
        catch (Exception error) { ReportError(error); }
    }

    private void RenderAddonPage()
    {
        var stats = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 12 };
        _stats = stats;
        stats.IsVisible = Bounds.Height > 0 ? Bounds.Height >= 820 : Height >= 820;
        var items = new[] { (_installed.Count.ToString(), "local addon folders detected"), (_catalog.Packages.Count.ToString(), "packs in the library"), ("Protected", "WTF settings preserved") };
        for (var i = 0; i < items.Length; i++)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(Heading(Get(items[i].Item1), 26, i == 2 ? LauncherTheme.Ice : LauncherTheme.Gold));
            stack.Children.Add(Text(Get(items[i].Item2), 12, color: Muted));
            var card = Card(stack); Grid.SetColumn(card, i); stats.Children.Add(card);
        }
        _body.Children.Add(stats);
        if (_page == "Library")
            _body.Children.Add(Notice("CATALOG SOURCES", "Historical addons come from the WoWGaming archive linked by ChromieCraft. Chromie Wishlist and LootRolls come directly from their GitHub repositories. Compatibility is declared, not guaranteed in game. Existing addons are never replaced automatically."));
        var toolbar = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 10 };
        _search.HorizontalAlignment = HorizontalAlignment.Stretch; toolbar.Children.Add(_search);
        _category.IsVisible = _page == "Library";
        _category.ItemsSource = new[] { Get("All categories") }.Concat(_catalog.Packages.Select(p => p.Category).Distinct().Order()).ToArray();
        _category.SelectedIndex = 0;
        Grid.SetColumn(_category, 1); toolbar.Children.Add(_category);
        Grid.SetColumn(_refresh, 2); toolbar.Children.Add(_refresh); _body.Children.Add(toolbar);
        var updateActions = new WrapPanel { Orientation = Orientation.Horizontal };
        _updateAllAddons.Content = Format("Update all addons ({0})", _availableAddonUpdates.Count);
        _updateAllAddons.IsVisible = _availableAddonUpdates.Count > 0;
        _updateAllAddons.Margin = new(0, 0, 14, 0);
        updateActions.Children.Add(_updateAllAddons);
        var updateSummary = Text(_lastAddonCheck is null ? Get("Addon updates have not been checked online.")
            : Format("{0} update(s) available · {1} pack(s) checked · {2} error(s)", _availableAddonUpdates.Count, _lastAddonCheck.CheckedPackages, _lastAddonCheck.Errors.Count), 13, color: Muted);
        updateSummary.VerticalAlignment = VerticalAlignment.Center; updateSummary.TextWrapping = TextWrapping.Wrap;
        ToolTip.SetTip(updateSummary, Get("Only known catalog sources are checked. Manual addons with unknown versions are not updated automatically.")
            + (_lastAddonCheck?.Errors.Count > 0 ? "\n" + string.Join("\n", _lastAddonCheck.Errors) : ""));
        updateActions.Children.Add(updateSummary); _body.Children.Add(updateActions);
        if (_count.Parent is Panel countParent) countParent.Children.Remove(_count);
        _body.Children.Add(_count);
        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        // Use a bounded Grid for the list: a StackPanel alone would measure it with infinite height.
        var wrapper = new Grid { RowDefinitions = new("Auto,*") };
        var upper = _body.Children.ToList(); _body.Children.Clear();
        var top = new StackPanel { Spacing = 16 };
        foreach (var child in upper) top.Children.Add(child);
        wrapper.Children.Add(top); Grid.SetRow(scroll, 1); scroll.Margin = new(0, 14, 0, 0); wrapper.Children.Add(scroll);
        _body.Children.Add(wrapper);
        RenderList();
    }

    private void RenderList()
    {
        if (_page is not ("My addons" or "Library")) return;
        _list.Children.Clear();
        var query = _search.Text?.Trim() ?? "";
        if (_page == "My addons")
        {
            var addons = _installed.Where(a => Contains(a.Name, query) || Contains(a.Folder, query)).ToList();
            _count.Text = Format("{0} folders · Blizzard modules are hidden", addons.Count);
            foreach (var addon in addons) _list.Children.Add(InstalledCard(addon));
            if (addons.Count == 0) _list.Children.Add(Empty("No addons to display", "Select your game folder in Settings, or explore the library."));
        }
        else
        {
            var category = _category.SelectedItem as string;
            var packages = _catalog.Packages.Where(p => (Contains(p.Name, query) || Contains(p.Description, query) || Contains(p.Id, query)) &&
                (_category.SelectedIndex <= 0 || p.Category == category)).ToList();
            _count.Text = Format(packages.Count == 1 ? "{0} pack · {1} pinned sources" : "{0} packs · {1} pinned sources", packages.Count, _catalog.Packages.Select(p => p.Repository).Distinct().Count());
            foreach (var package in packages) _list.Children.Add(PackageCard(package));
            if (packages.Count == 0) _list.Children.Add(Empty("No results", "Try a different name or category."));
        }
    }

    private Border InstalledCard(InstalledAddon addon)
    {
        var row = new Grid { ColumnDefinitions = new("48,*,Auto"), ColumnSpacing = 14 };
        row.Children.Add(Monogram(addon.Name, addon.Managed));
        var text = new StackPanel { Spacing = 5 };
        text.Children.Add(Text(addon.Name, 16, FontWeight.SemiBold));
        text.Children.Add(Text($"{addon.Folder}  ·  Version {addon.Version}  ·  Interface {addon.Interface}", 13, color: Muted));
        text.Children.Add(Text(Get(addon.Status), 12, color: addon.Managed ? LauncherTheme.Success : LauncherTheme.Gold));
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var available = _availableAddonUpdates.FirstOrDefault(u => u.Package.Folders.Contains(addon.Folder, StringComparer.OrdinalIgnoreCase));
        if (available is not null)
        {
            text.Children.Add(Text(Format("Update available: {0}", available.Package.Version), 12, color: LauncherTheme.Gold));
            var update = Button("Update", "primary"); update.Click += async (_, _) => await InstallAddon(available.Package); actions.Children.Add(update);
        }
        var folder = Button("Open"); folder.Click += (_, _) => OpenFolder(SafePath.Child(AddonService.ValidateGame(_settings.GamePath), "Interface/AddOns/" + addon.Folder));
        var remove = Button("Remove"); remove.Click += async (_, _) => await RemoveAddon(addon);
        actions.Children.Add(folder); actions.Children.Add(remove); Grid.SetColumn(actions, 2); row.Children.Add(actions);
        return Card(row);
    }

    private Border PackageCard(AddonPackage package)
    {
        var existing = package.Folders.Any(f => _installed.Any(a => a.Folder.Equals(f, StringComparison.OrdinalIgnoreCase)));
        var managed = _managed.Packages.FirstOrDefault(p => p.Id == package.Id);
        var current = managed is not null && !_availableAddonUpdates.Any(u => u.Package.Id == package.Id) && package.Folders.All(f => _installed.Any(a => a.Folder.Equals(f, StringComparison.OrdinalIgnoreCase)));
        var row = new Grid { ColumnDefinitions = new("48,*,Auto"), ColumnSpacing = 14 };
        row.Children.Add(Monogram(package.Name, existing));
        var text = new StackPanel { Spacing = 5 };
        text.Children.Add(Text(package.Name, 16, FontWeight.SemiBold));
        var description = Text(package.Description, 14, color: Muted); description.TextWrapping = TextWrapping.Wrap; description.MaxLines = 2; description.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(description);
        text.Children.Add(Text(Format("{0}  ·  {1}  ·  {2} folder(s)", package.Category, package.Version, package.Folders.Count), 12, color: LauncherTheme.Gold));
        text.Children.Add(Text(Format("Source: {0}", package.Repository), 12, color: Muted));
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var action = Button(current ? "Installed" : _availableAddonUpdates.Any(u => u.Package.Id == package.Id) ? "Update" : existing ? "Replace…" : "Install", current ? null : "primary");
        action.IsEnabled = !current; action.MinWidth = 112; action.VerticalAlignment = VerticalAlignment.Center;
        action.Click += async (_, _) => await InstallAddon(package);
        Grid.SetColumn(action, 2); row.Children.Add(action);
        return Card(row);
    }

    private void RenderSettings()
    {
        var fields = new StackPanel { Spacing = 16 };
        fields.Children.Add(Heading(Get("Application language"), 20));
        var language = new ComboBox { ItemsSource = new[] { "English", "Français", "Italiano" }, SelectedIndex = Array.IndexOf(LanguageCodes, _settings.Language), MinWidth = 220 };
        fields.Children.Add(language);
        fields.Children.Add(Text(Get("Applied when you save. No restart required."), 12, color: Muted));
        fields.Children.Add(Heading(Get("World of Warcraft installation"), 20));
        fields.Children.Add(Text(Get("Select the folder containing Wow.exe. No WTF files will be modified."), 12, color: Muted));
        var path = new TextBox { Text = _settings.GamePath, Watermark = "C:\\Games\\ChromieCraft", CornerRadius = new(2) };
        var browse = Button("Browse…");
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Get("ChromieCraft game folder"), AllowMultiple = false });
            if (folders.Count > 0) path.Text = folders[0].TryGetLocalPath();
        };
        fields.Children.Add(path); fields.Children.Add(browse);
        fields.Children.Add(new Border { Height = 1, Background = Line, Margin = new(0, 8) });
        fields.Children.Add(Heading(Get("Built-in launcher updates"), 20));
        fields.Children.Add(Text(Get("Automatic checks, verified downloads and restart from this application."), 12, color: Muted));
        var automatic = new CheckBox { Content = Get("Check for updates at startup"), IsChecked = _settings.CheckUpdatesAtStartup };
        fields.Children.Add(automatic);
        var feed = new TextBox { Text = _settings.UpdateFeedUrl, Watermark = "https://…/update.json", CornerRadius = new(2) };
        fields.Children.Add(Text(Get("Publisher's signed update manifest URL"), 12, color: Muted)); fields.Children.Add(feed);
        fields.Children.Add(Notice("DEVELOPMENT BUILD", "The updater is built in. Distribution requires hosted releases and a publisher signing key embedded at build time. Unsigned software updates are not allowed."));
        var save = Button("Save settings", "primary");
        save.Click += async (_, _) =>
        {
            var oldPath = _settings.GamePath;
            await Run("Saving settings", async () =>
            {
                // Language can be saved even before a game folder has been configured.
                var validated = string.IsNullOrWhiteSpace(path.Text) ? "" : AddonService.ValidateGame(path.Text);
                var feedUrl = feed.Text?.Trim() ?? "";
                if (feedUrl.Length > 0 && (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")) throw new IOException(Get("The update feed must use HTTPS."));
                _settings.GamePath = validated; _settings.UpdateFeedUrl = feedUrl; _settings.CheckUpdatesAtStartup = automatic.IsChecked == true;
                if (!string.Equals(oldPath, validated, StringComparison.OrdinalIgnoreCase))
                {
                    _availableAddonUpdates = []; _lastAddonCheck = null;
                    var original = CatalogService.Load(); _catalog.Packages = original.Packages; _catalog.Files = original.Files;
                }
                _settings.Language = LanguageCodes[Math.Max(0, language.SelectedIndex)];
                _storage.SaveSettings(_settings);
                SetLanguage(_settings.Language);
                ApplyLanguage();
                if (validated.Length > 0) await ReloadAsync();
                else { _installed = []; _managed = new(); _patchStatus = new(false, null); }
            });
            if (Program.SmokeTestOutput is null && _settings.GamePath.Length > 0 && !string.Equals(oldPath, _settings.GamePath, StringComparison.OrdinalIgnoreCase))
                await CheckAddonUpdates();
        };
        fields.Children.Add(save);
        var data = Button("Open data and logs"); data.Click += (_, _) => OpenFolder(_storage.Root); fields.Children.Add(data);
        _body.Children.Add(new ScrollViewer { Content = Card(fields) });
    }

    private void RenderDownloads()
    {
        var notice = Notice("LOCAL BACKUPS", "Replaced or removed folders are preserved in Interface\\.chromie-launcher\\transactions. WTF settings are never deleted. Backups are not cleaned up automatically.");
        DockPanel.SetDock(notice, Dock.Top); notice.Margin = new(0, 0, 0, 16); _body.Children.Add(notice);
        var backups = Button("Open backups");
        backups.Click += (_, _) =>
        {
            try
            {
                var path = AddonService.BackupDirectory(_settings.GamePath);
                if (Directory.Exists(path)) OpenFolder(path); else _status.Text = Get("No backups yet.");
            }
            catch (Exception e) { ReportError(e); }
        };
        DockPanel.SetDock(backups, Dock.Top); backups.Margin = new(0, 0, 0, 16); _body.Children.Add(backups);
        var history = new StackPanel { Spacing = 10 };
        if (_history.Count == 0) history.Children.Add(Empty("No downloads yet", "Installations and updates will appear here during this session."));
        foreach (var entry in _history.AsEnumerable().Reverse().Take(50)) history.Children.Add(Card(Text(entry, 12, color: Muted)));
        _body.Children.Add(new ScrollViewer { Content = history });
    }

    private async Task ReloadAsync()
    {
        var path = _settings.GamePath;
        var result = await Task.Run(() => (_addons.Scan(path), _addons.GetState(path)));
        _installed = result.Item1; _managed = result.Item2;
        _availableAddonUpdates.RemoveAll(u => !u.Package.Folders.All(f => _installed.Any(a => a.Folder.Equals(f, StringComparison.OrdinalIgnoreCase)))
            || _managed.Packages.Any(p => p.Id == u.Package.Id && p.Commit == u.Package.Commit));
        _patchStatus = Directory.Exists(Path.Combine(path, "Data")) ? await _patches.InspectAsync(path) : new(false, null);
        RenderList();
    }

    private async Task SmokeTestAsync()
    {
        var output = Path.GetFullPath(Program.SmokeTestOutput!);
        var directory = Path.GetDirectoryName(output)!;
        var stem = Path.GetFileNameWithoutExtension(output);
        var savedLanguage = _settings.Language;
        var savedAddonUpdates = _availableAddonUpdates;
        var savedAddonCheck = _lastAddonCheck;
        Directory.CreateDirectory(directory);
        void Capture(string path)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
            bitmap.Render(_root); bitmap.Save(path);
        }
        try
        {
            if (Icon is null) throw new InvalidOperationException("The window icon is missing.");
            if (LauncherText.Language != NormalizeLanguage(_settings.Language))
                throw new InvalidOperationException("The saved UI language was not applied.");
            Capture(output);
            foreach (var (page, suffix) in new[] { ("Library", "library"), ("Client patches", "patches"), ("Settings", "settings"), ("Downloads", "downloads"), ("My addons", "installed") })
            {
                Navigate(page); await Task.Delay(200);
                Capture(Path.Combine(directory, stem + "-" + suffix + ".png"));
            }
            // Check the safety acknowledgement without invoking any installer.
            var confirmation = Confirm("Client patch confirmation test", "UI validation only. No patch will be downloaded or installed.", "Back up and install", "I confirm this is an HD client.");
            await Task.Delay(100);
            var dialog = OwnedWindows.Single();
            var dialogPanel = (StackPanel)dialog.Content!;
            var accept = (Button)((StackPanel)dialogPanel.Children.Last()).Children.Last();
            var acknowledgement = dialogPanel.Children.OfType<CheckBox>().Single();
            if (accept.IsEnabled) throw new InvalidOperationException("Patch installation must require client-type acknowledgement.");
            acknowledgement.IsChecked = true; await Task.Delay(50);
            if (!accept.IsEnabled) throw new InvalidOperationException("Patch acknowledgement did not enable confirmation.");
            acknowledgement.IsChecked = false; await Task.Delay(50);
            if (accept.IsEnabled) throw new InvalidOperationException("Unchecked acknowledgement must disable installation.");
            dialog.Close(false);
            if (await confirmation) throw new InvalidOperationException("The test confirmation must not accept installation.");
            Navigate("Library"); _search.Text = "Bagnon";
            await Task.Delay(100); // Avalonia delivers TextChanged on the next dispatcher pass.
            if (_list.Children.Count != 1 || _list.Children[0] is not Border) throw new InvalidOperationException("Catalog search is not working: " + _count.Text);
            _search.Text = "__no_result__";
            await Task.Delay(100);
            if (_count.Text != Format("{0} packs · {1} pinned sources", 0, _catalog.Packages.Select(p => p.Repository).Distinct().Count())) throw new InvalidOperationException("Empty search results are not working.");
            foreach (var addon in new[] { "Chromie Wishlist", "LootRolls" })
            {
                _search.Text = addon; await Task.Delay(100);
                if (_count.Text != Format("{0} pack · {1} pinned sources", 1, _catalog.Packages.Select(p => p.Repository).Distinct().Count())) throw new InvalidOperationException("Community addon is missing: " + addon);
                Capture(Path.Combine(directory, stem + (addon == "LootRolls" ? "-lootrolls.png" : "-wishlist.png")));
            }
            _search.Text = ""; await Task.Delay(100);
            Width = MinWidth; Height = MinHeight;
            foreach (var code in LanguageCodes)
            {
                _settings.Language = code;
                SetLanguage(code); ApplyLanguage(); Navigate("Settings"); await Task.Delay(200);
                if (_heading.Text != Get("Settings") || _navButtons.Last().Content as string != Get("Settings"))
                    throw new InvalidOperationException("Language switching failed: " + code);
                Capture(Path.Combine(directory, stem + "-settings-" + code + ".png"));
                Navigate("Library"); await Task.Delay(200);
                if (_navigation.Bounds.Right > _update.Bounds.Left)
                    throw new InvalidOperationException("Navigation overlaps the update button at minimum window size: " + code);
                Capture(Path.Combine(directory, stem + "-compact-" + code + ".png"));
                // Inject a display-only proposal. Never check the network or invoke an installer in the smoke test.
                _availableAddonUpdates = [new(_catalog.Packages[0], [], "1.0", false)];
                _lastAddonCheck = new(_availableAddonUpdates, [], 1);
                Navigate("My addons"); await Task.Delay(150);
                if (!_updateAllAddons.IsVisible || _updateAllAddons.Content as string != Format("Update all addons ({0})", 1))
                    throw new InvalidOperationException("Bulk addon update button is missing or not translated: " + code);
                Capture(Path.Combine(directory, stem + "-addon-updates-" + code + ".png"));
                _availableAddonUpdates = []; Navigate("My addons"); await Task.Delay(50);
                if (_updateAllAddons.IsVisible) throw new InvalidOperationException("Bulk update button must be hidden without confirmed updates.");
            }
            File.WriteAllText(Path.Combine(directory, stem + "-smoke.txt"), $"OK — English/French/Italian UI, window icon, 5 pages including HD/non-HD patches, search, community addons, bulk addon update button visibility and translation, minimum size {MinWidth}×{MinHeight}, {_installed.Count} addons detected, {_catalog.Packages.Count} packs. No game files modified.");
        }
        catch (Exception error)
        {
            Program.SmokeTestFailed = true;
            File.WriteAllText(Path.Combine(directory, stem + "-smoke-error.txt"), error.ToString());
        }
        finally { _availableAddonUpdates = savedAddonUpdates; _lastAddonCheck = savedAddonCheck; _settings.Language = savedLanguage; SetLanguage(savedLanguage); Close(); }
    }

    private async Task CheckAddonUpdates()
    {
        if (_busy) return;
        await Run("Checking addon updates", async () =>
        {
            await ReloadAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_operation!.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            AddonUpdateCheck result;
            try { result = await _addonUpdates.CheckAsync(_catalog, _managed, _installed, timeout.Token); }
            catch (OperationCanceledException) when (!_operation.IsCancellationRequested)
            { throw new TimeoutException(Get("The addon update check timed out. Try Refresh again later.")); }
            _lastAddonCheck = result; _availableAddonUpdates = result.Updates;
            foreach (var update in result.Updates) AddonUpdateService.Apply(_catalog, update);
            foreach (var error in result.Errors) Log(error);
        }, cancellable: true);
    }

    private async Task UpdateAllAddons()
    {
        if (_busy || _availableAddonUpdates.Count == 0) return;
        try
        {
            var game = AddonService.ValidateGame(_settings.GamePath);
            var updates = _availableAddonUpdates.ToList();
            var message = string.Join("\n", updates.Select(u => $"{u.Package.Name}: {u.InstalledVersion} → {u.Package.Version} ({u.Package.Repository}@{u.Package.Commit[..8]})"));
            var extra = updates.SelectMany(u => CatalogService.Resolve(_catalog, u.Package.Id)).DistinctBy(p => p.Id)
                .Where(p => updates.All(u => u.Package.Id != p.Id) && !p.Folders.All(f => Directory.Exists(SafePath.Child(game, "Interface/AddOns/" + f)))).ToList();
            if (extra.Count > 0) message += "\n\n" + Format("Required dependencies to install: {0}", string.Join(", ", extra.Select(p => p.Name)));
            message += "\n\n" + Get("Close World of Warcraft first. All listed addons will be backed up and updated together. Manual addons in this list will become managed. WTF settings will be preserved.");
            if (!await Confirm("Update all addons", message, "Back up and update all")) return;
            await Run("Updating addons", async () =>
            {
                await _addons.InstallManyAsync(game, updates.Select(u => u.Package).ToList(), new Progress<string>(text => _status.Text = text), _operation!.Token);
                await ReloadAsync();
            }, cancellable: true);
        }
        catch (Exception error) { ReportError(error); }
    }

    private async Task InstallAddon(AddonPackage package)
    {
        if (_busy) return;
        try
        {
            var game = AddonService.ValidateGame(_settings.GamePath);
            var resolved = CatalogService.Resolve(_catalog, package.Id);
            var replacements = resolved.SelectMany(p => p.Folders).Where(f => Directory.Exists(SafePath.Child(game, "Interface/AddOns/" + f))).ToList();
            var message = Format("Install {0}?\n\nSource: {1}\nVersion: {2}\nPacks and dependencies: {3}\n\n", package.Name, package.Repository, package.Version, string.Join(", ", resolved.Select(p => p.Name)));
            message += replacements.Count > 0 ? Format("Existing folders that may be replaced:\n{0}\n\nThis version may be older than your current addons. A backup will be preserved.", string.Join(", ", replacements)) : Get("Files will be installed in Interface\\AddOns. Your WTF settings will be preserved.");
            if (!await Confirm("Install addon", message, replacements.Count > 0 ? "Back up and install" : "Install")) return;
            await Run(Format("Installing {0}", package.Name), async () =>
            {
                await _addons.InstallAsync(game, package, new Progress<string>(text => _status.Text = text), _operation!.Token);
                await ReloadAsync();
            }, cancellable: true);
        }
        catch (Exception error) { ReportError(error); }
    }

    private async Task RemoveAddon(InstalledAddon addon)
    {
        var managed = _managed.Packages.FirstOrDefault(p => p.Id == addon.PackageId);
        var folders = managed?.Folders ?? [addon.Folder];
        if (!await Confirm("Remove addon", Format("Move these folders to backups:\n{0}\n\nWTF settings will be preserved. Removal is blocked if other addons require these dependencies.", string.Join(", ", folders)), "Back up and remove")) return;
        await Run(Format("Removing {0}", addon.Name), async () =>
        {
            await Task.Run(() => _addons.Uninstall(_settings.GamePath, addon.Folder));
            await ReloadAsync();
        });
    }

    private async Task CheckUpdates(bool silent)
    {
        if (_busy) return;
        if (_preparedUpdate is not null)
        {
            if (await Confirm("Update ready", Get("The launcher will restart to apply the update. Your addons and settings will not be modified."), "Restart and install"))
            {
                try { _updater.LaunchReplacement(_preparedUpdate); Close(); }
                catch (Exception error) { ReportError(error); }
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(_settings.UpdateFeedUrl))
        {
            if (!silent) { Navigate("Settings"); _status.Text = Get("No update feed is configured. See the Built-in launcher updates section."); }
            return;
        }
        (SignedUpdate Envelope, UpdateRelease Release)? available = null;
        var checkedSuccessfully = false;
        await Run(Get("Checking for updates"), async () =>
        {
            available = await _updater.CheckAsync(_settings.UpdateFeedUrl);
            checkedSuccessfully = true;
        });
        if (available is null)
        {
            if (checkedSuccessfully) _status.Text = Format("The launcher is up to date (version {0}).", UpdateService.DisplayVersion);
            return;
        }
        var update = available.Value;
        if (!await Confirm(Format("Version {0} available", update.Release.Version), update.Release.Notes + Get("\n\nDownload this signed update? It will only be applied after you agree to restart."), "Download")) return;
        await Run(Get("Downloading launcher update"), async () =>
        {
            _preparedUpdate = await _updater.PrepareAsync(update.Envelope, new Progress<string>(text => _status.Text = text), _operation!.Token);
            _update.Content = Get("Restart");
            ToolTip.SetTip(_update, Get("Restart to apply the launcher update"));
        }, cancellable: true);
    }

    private async Task Run(string label, Func<Task> action, bool cancellable = false)
    {
        if (_busy) return;
        _busy = true; _body.IsEnabled = false; _navigation.IsEnabled = false;
        _play.IsEnabled = false; _refresh.IsEnabled = false; _update.IsEnabled = false;
        _progress.IsVisible = true; _cancel.IsVisible = cancellable;
        _operation?.Dispose(); _operation = new();
        _status.Text = Get(label) + "…"; Log(Get(label));
        try { await action(); _status.Text = Format("{0} — complete", Get(label)); Log(_status.Text); }
        catch (OperationCanceledException) { _status.Text = Get("Operation canceled. No files were replaced."); Log(Format("{0} — canceled", label)); }
        catch (Exception error) { ReportError(error); }
        finally
        {
            _busy = false; _body.IsEnabled = true; _navigation.IsEnabled = true;
            _play.IsEnabled = true; _refresh.IsEnabled = true; _update.IsEnabled = true;
            _progress.IsVisible = false; _cancel.IsVisible = false;
            Navigate(_page);
        }
    }

    private void LaunchGame()
    {
        try
        {
            var path = AddonService.ValidateGame(_settings.GamePath);
            Process.Start(new ProcessStartInfo(Path.Combine(path, "Wow.exe")) { WorkingDirectory = path, UseShellExecute = true })?.Dispose();
            _status.Text = Get("World of Warcraft launched.");
        }
        catch (Exception error) { ReportError(error); }
    }

    private void ReportError(Exception error)
    {
        _status.Text = Format("Error: {0}", Get(error.Message));
        Log(error.ToString());
    }

    private void Log(string text)
    {
        var entry = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        _history.Add(entry);
        try
        {
            var logs = Path.Combine(_storage.Root, "logs"); Directory.CreateDirectory(logs);
            File.AppendAllText(Path.Combine(logs, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), entry + Environment.NewLine);
        }
        catch (IOException) { /* A logging failure must not interrupt an installation. */ }
    }

    private void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch (Exception error) { ReportError(error); }
    }

    private async Task<bool> Confirm(string title, string message, string action, string? acknowledgement = null)
    {
        title = Get(title);
        var dialog = new Window { Title = title, Icon = Icon, Width = 620, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = LauncherTheme.Background, FontFamily = LauncherTheme.BodyFont };
        var panel = new StackPanel { Spacing = 20, Margin = new(28) };
        panel.Children.Add(Heading(title, 24, LauncherTheme.Gold));
        var copy = Text(message, 13, color: Muted); copy.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(new ScrollViewer { Content = copy, MaxHeight = 350 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button("Cancel"); cancel.Click += (_, _) => dialog.Close(false);
        var accept = Button(action, "primary"); accept.Click += (_, _) => dialog.Close(true);
        if (acknowledgement is not null)
        {
            var checkbox = new CheckBox { Content = Get(acknowledgement) };
            accept.IsEnabled = false;
            checkbox.IsCheckedChanged += (_, _) => accept.IsEnabled = checkbox.IsChecked == true;
            panel.Children.Add(checkbox);
        }
        actions.Children.Add(cancel); actions.Children.Add(accept); panel.Children.Add(actions); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }

    private static string DetectGame()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Wow.exe"))) return directory.FullName;
        return "";
    }

    private static bool Contains(string text, string query) => text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    private static TextBlock Text(string text, double size = 14, FontWeight? weight = null, IBrush? color = null) => new()
    { Text = text, FontFamily = LauncherTheme.BodyFont, FontSize = size, FontWeight = weight ?? FontWeight.Normal, Foreground = color ?? LauncherTheme.Text };
    private static TextBlock Heading(string text, double size, IBrush? color = null) => new()
    { Text = text, FontFamily = LauncherTheme.HeadingFont, FontSize = size, Foreground = color ?? LauncherTheme.Text };
    private static Button Button(string text, string? style = null)
    {
        var button = new Button { Content = Get(text) };
        if (style is not null) button.Classes.Add(style);
        return button;
    }
    private static Border Card(Control content) => new() { Background = Surface, BorderBrush = Line, BorderThickness = new(1), CornerRadius = new(3), Padding = new(18, 14), Child = content };
    private static Border Notice(string title, string message)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Heading(Get(title), 11, LauncherTheme.Gold));
        var body = Text(Get(message), 13, color: Muted); body.TextWrapping = TextWrapping.Wrap; stack.Children.Add(body);
        return new Border { Background = Brush.Parse("#EC191711"), BorderBrush = Brush.Parse("#6D5B3D"), BorderThickness = new(1, 0, 0, 0), CornerRadius = new(2), Padding = new(16, 12), Child = stack };
    }
    private static Border Empty(string title, string description)
    {
        var stack = new StackPanel { Spacing = 12, Margin = new(12, 25) };
        stack.Children.Add(Heading(Get(title), 20));
        stack.Children.Add(Text(Get(description), 12, color: Muted));
        return Card(stack);
    }
    private static Border Monogram(string name, bool installed) => new()
    {
        Width = 46, Height = 46, CornerRadius = new(2), VerticalAlignment = VerticalAlignment.Top,
        BorderBrush = Brush.Parse(installed ? "#536A60" : "#796745"), BorderThickness = new(1),
        Background = LauncherTheme.Shade((0, installed ? "#D926332F" : "#D9342F23"), (1, "#EE111212")),
        Child = new TextBlock { Text = name.TrimStart('<', ' ', '!', '_') is { Length: > 0 } clean ? clean[..1].ToUpperInvariant() : "?", FontFamily = LauncherTheme.HeadingFont, FontSize = 23, Foreground = installed ? LauncherTheme.Success : LauncherTheme.Gold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
    };
}
