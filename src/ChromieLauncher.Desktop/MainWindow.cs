using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChromieLauncher.Core;

namespace ChromieLauncher.Desktop;

public sealed class MainWindow : Window
{
    private static readonly IBrush Muted = Brush.Parse("#999CAF");
    private static readonly IBrush Surface = Brush.Parse("#20222D");
    private static readonly IBrush Line = Brush.Parse("#30323E");
    private readonly Storage _storage = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly Catalog _catalog = CatalogService.Load();
    private readonly Settings _settings;
    private readonly AddonService _addons;
    private readonly UpdateService _updater;
    private readonly Grid _root = new() { ColumnDefinitions = new("224,*"), Background = Brush.Parse("#181A23") };
    private readonly StackPanel _navigation = new() { Spacing = 8 };
    private readonly DockPanel _body = new() { LastChildFill = true };
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly TextBlock _heading = Text("Mes addons", 28, FontWeight.SemiBold);
    private readonly TextBlock _subtitle = Text("", 13, color: Muted);
    private readonly TextBlock _status = Text("Prêt", 12, color: Muted);
    private readonly TextBlock _count = Text("", 13, color: Muted);
    private readonly TextBox _search = new() { Watermark = "Rechercher un addon…", MinWidth = 260, CornerRadius = new(8) };
    private readonly ComboBox _category = new() { MinWidth = 180, CornerRadius = new(8) };
    private readonly Button _play = Button("Jouer à ChromieCraft", "primary");
    private readonly Button _refresh = Button("Actualiser");
    private readonly Button _update = Button("Mises à jour");
    private readonly Button _cancel = Button("Annuler");
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, IsVisible = false };
    private readonly List<string> _history = [];
    private readonly List<Button> _navButtons = [];
    private List<InstalledAddon> _installed = [];
    private ManagedState _managed = new();
    private string _page = "Mes addons";
    private bool _busy;
    private CancellationTokenSource? _operation;
    private string? _preparedUpdate;

    public MainWindow()
    {
        Title = "Chromie Launcher";
        Width = 1220; Height = 820; MinWidth = 980; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush.Parse("#181A23");
        FontFamily = new("Segoe UI");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ChromieLauncher/" + UpdateService.DisplayVersion);
        _settings = _storage.LoadSettings();
        if (string.IsNullOrEmpty(_settings.GamePath)) _settings.GamePath = DetectGame();
        _addons = new(_storage, _http, _catalog);
        _updater = new(_storage, _http);
        BuildLayout();
        Content = _root;
        _search.TextChanged += (_, _) => RenderList();
        _category.SelectionChanged += (_, _) => RenderList();
        _refresh.Click += async (_, _) => await Run("Analyse des addons", ReloadAsync);
        _play.Click += (_, _) => LaunchGame();
        _update.Click += async (_, _) => await CheckUpdates(false);
        _cancel.Click += (_, _) => _operation?.Cancel();
        Closing += (_, e) => { if (_busy) { e.Cancel = true; _status.Text = "Une opération est en cours. Annule le téléchargement ou attends sa fin."; } };
        Closed += (_, _) => { _http.Dispose(); _operation?.Dispose(); };
        Opened += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(_settings.GamePath)) await Run("Analyse des addons", ReloadAsync);
            else Navigate("Paramètres");
            if (_settings.CheckUpdatesAtStartup && !string.IsNullOrEmpty(_settings.UpdateFeedUrl)) await CheckUpdates(true);
            if (Program.SmokeTestOutput is not null)
                DispatcherTimer.RunOnce(() => _ = SmokeTestAsync(), TimeSpan.FromSeconds(2));
        };
        Navigate(_page);
    }

    private void BuildLayout()
    {
        var rail = new DockPanel { Margin = new(18, 28) };
        var brand = new StackPanel { Spacing = 5, Margin = new(12, 0, 0, 32) };
        brand.Children.Add(Text("◈  CHROMIE", 23, FontWeight.Bold, Brush.Parse("#C4B5FD")));
        brand.Children.Add(Text("      L A U N C H E R", 11, color: Muted));
        DockPanel.SetDock(brand, Dock.Top); rail.Children.Add(brand);
        var footer = new StackPanel { Spacing = 10, Margin = new(12, 20, 0, 0) };
        footer.Children.Add(Text("WOTLK  ·  3.3.5a", 12, FontWeight.SemiBold, Brush.Parse("#D7B778")));
        footer.Children.Add(Text("Version " + UpdateService.DisplayVersion, 12, color: Muted));
        footer.Children.Add(Text("Projet communautaire indépendant\nNon affilié à ChromieCraft", 10, color: Muted));
        DockPanel.SetDock(footer, Dock.Bottom); rail.Children.Add(footer);
        foreach (var (title, symbol) in new[] { ("Mes addons", "▤"), ("Bibliothèque", "▦"), ("Téléchargements", "↓"), ("Paramètres", "⚙") })
        {
            var button = Button(symbol + "   " + title, "nav");
            button.Tag = title;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => Navigate(title);
            _navButtons.Add(button); _navigation.Children.Add(button);
        }
        rail.Children.Add(_navigation);
        _root.Children.Add(new Border { Background = Brush.Parse("#12131A"), BorderBrush = Line, BorderThickness = new(0, 0, 1, 0), Child = rail });

        var content = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(32, 26, 32, 18) };
        Grid.SetColumn(content, 1); _root.Children.Add(content);
        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 0, 0, 24) };
        var titles = new StackPanel { Spacing = 6 }; titles.Children.Add(_heading); titles.Children.Add(_subtitle); header.Children.Add(titles);
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        headerActions.Children.Add(_update); headerActions.Children.Add(_play); Grid.SetColumn(headerActions, 1); header.Children.Add(headerActions);
        content.Children.Add(header);
        Grid.SetRow(_body, 1); content.Children.Add(_body);
        var statusPanel = new Grid { RowDefinitions = new("Auto,Auto"), Margin = new(0, 16, 0, 0) };
        statusPanel.Children.Add(_progress);
        var bottom = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 10, 0, 0) };
        _status.TextWrapping = TextWrapping.Wrap; bottom.Children.Add(_status);
        _cancel.IsVisible = false; Grid.SetColumn(_cancel, 1); bottom.Children.Add(_cancel);
        Grid.SetRow(bottom, 1); statusPanel.Children.Add(bottom);
        Grid.SetRow(statusPanel, 2); content.Children.Add(statusPanel);
    }

    private void Navigate(string page)
    {
        if (_busy) return;
        _page = page; _heading.Text = page;
        foreach (var button in _navButtons) button.Classes.Set("selected", (string?)button.Tag == page);
        _subtitle.Text = page switch
        {
            "Mes addons" => "Ton interface, tes addons. Tout au même endroit.",
            "Bibliothèque" => "Explore l’archive 3.3.5a référencée par ChromieCraft.",
            "Téléchargements" => "Opérations de cette session et accès aux sauvegardes.",
            _ => "Ton jeu et les préférences du launcher."
        };
        _body.Children.Clear();
        // Controls are reused between pages; detach them before constructing the next page.
        if (_search.Parent is Panel oldToolbar) oldToolbar.Children.Clear();
        if (_refresh.Parent is Panel oldRefreshParent) oldRefreshParent.Children.Clear();
        if (_list.Parent is ScrollViewer oldScroll) oldScroll.Content = null;
        if (page == "Paramètres") { RenderSettings(); return; }
        if (page == "Téléchargements") { RenderDownloads(); return; }
        RenderAddonPage();
    }

    private void RenderAddonPage()
    {
        var stats = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 12 };
        var items = new[] { (_installed.Count.ToString(), "addons locaux détectés"), (_catalog.Packages.Count.ToString(), "packs dans l’archive"), ("Protégés", "réglages WTF conservés") };
        for (var i = 0; i < items.Length; i++)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(Text(items[i].Item1, 23, FontWeight.SemiBold, i == 2 ? Brush.Parse("#7DD3B0") : Brushes.White));
            stack.Children.Add(Text(items[i].Item2, 12, color: Muted));
            var card = Card(stack); Grid.SetColumn(card, i); stats.Children.Add(card);
        }
        _body.Children.Add(stats);
        if (_page == "Bibliothèque")
            _body.Children.Add(Notice("ARCHIVE HISTORIQUE", "Cette source est liée par ChromieCraft, mais n’est pas une liste de recommandations individuelles. Compatibilité déclarée dans les fichiers TOC, non garantie en jeu. Aucun remplacement automatique de tes addons."));
        var toolbar = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 10 };
        _search.HorizontalAlignment = HorizontalAlignment.Stretch; toolbar.Children.Add(_search);
        _category.IsVisible = _page == "Bibliothèque";
        _category.ItemsSource = new[] { "Toutes les catégories" }.Concat(_catalog.Packages.Select(p => p.Category).Distinct().Order()).ToArray();
        _category.SelectedIndex = 0;
        Grid.SetColumn(_category, 1); toolbar.Children.Add(_category);
        Grid.SetColumn(_refresh, 2); toolbar.Children.Add(_refresh); _body.Children.Add(toolbar);
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
        if (_page is not ("Mes addons" or "Bibliothèque")) return;
        _list.Children.Clear();
        var query = _search.Text?.Trim() ?? "";
        if (_page == "Mes addons")
        {
            var addons = _installed.Where(a => Contains(a.Name, query) || Contains(a.Folder, query)).ToList();
            _count.Text = $"{addons.Count} dossiers · Les modules Blizzard sont masqués";
            foreach (var addon in addons) _list.Children.Add(InstalledCard(addon));
            if (addons.Count == 0) _list.Children.Add(Empty("Aucun addon à afficher", "Choisis ton dossier de jeu dans les paramètres, ou explore la bibliothèque."));
        }
        else
        {
            var category = _category.SelectedItem as string;
            var packages = _catalog.Packages.Where(p => (Contains(p.Name, query) || Contains(p.Description, query) || Contains(p.Id, query)) &&
                (category is null or "Toutes les catégories" || p.Category == category)).ToList();
            _count.Text = $"{packages.Count} packs · Source figée : {_catalog.Commit[..Math.Min(8, _catalog.Commit.Length)]}";
            foreach (var package in packages) _list.Children.Add(PackageCard(package));
            if (packages.Count == 0) _list.Children.Add(Empty("Aucun résultat", "Essaie un autre nom ou une autre catégorie."));
        }
    }

    private Border InstalledCard(InstalledAddon addon)
    {
        var row = new Grid { ColumnDefinitions = new("48,*,Auto"), ColumnSpacing = 14 };
        row.Children.Add(Monogram(addon.Name, addon.Managed));
        var text = new StackPanel { Spacing = 5 };
        text.Children.Add(Text(addon.Name, 15, FontWeight.SemiBold));
        text.Children.Add(Text($"{addon.Folder}  ·  Version {addon.Version}  ·  Interface {addon.Interface}", 11, color: Muted));
        text.Children.Add(Text(addon.Status, 11, color: addon.Managed ? Brush.Parse("#7DD3B0") : Brush.Parse("#D7B778")));
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var folder = Button("Ouvrir"); folder.Click += (_, _) => OpenFolder(SafePath.Child(AddonService.ValidateGame(_settings.GamePath), "Interface/AddOns/" + addon.Folder));
        var remove = Button("Retirer"); remove.Click += async (_, _) => await RemoveAddon(addon);
        actions.Children.Add(folder); actions.Children.Add(remove); Grid.SetColumn(actions, 2); row.Children.Add(actions);
        return Card(row);
    }

    private Border PackageCard(AddonPackage package)
    {
        var existing = package.Folders.Any(f => _installed.Any(a => a.Folder.Equals(f, StringComparison.OrdinalIgnoreCase)));
        var managed = _managed.Packages.FirstOrDefault(p => p.Id == package.Id);
        var current = managed is not null && managed.Commit == _catalog.Commit && package.Folders.All(f => _installed.Any(a => a.Folder.Equals(f, StringComparison.OrdinalIgnoreCase)));
        var row = new Grid { ColumnDefinitions = new("48,*,Auto"), ColumnSpacing = 14 };
        row.Children.Add(Monogram(package.Name, existing));
        var text = new StackPanel { Spacing = 5 };
        text.Children.Add(Text(package.Name, 15, FontWeight.SemiBold));
        var description = Text(package.Description, 12, color: Muted); description.TextWrapping = TextWrapping.Wrap; description.MaxLines = 2; description.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(description);
        text.Children.Add(Text($"{package.Category}  ·  {package.Version}  ·  {package.Folders.Count} dossier(s)", 11, color: Brush.Parse("#B8A4E9")));
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var action = Button(current ? "Installé" : managed is not null ? "Actualiser" : existing ? "Remplacer…" : "Installer", current ? null : "primary");
        action.IsEnabled = !current; action.MinWidth = 112; action.VerticalAlignment = VerticalAlignment.Center;
        action.Click += async (_, _) => await InstallAddon(package);
        Grid.SetColumn(action, 2); row.Children.Add(action);
        return Card(row);
    }

    private void RenderSettings()
    {
        var fields = new StackPanel { Spacing = 16 };
        fields.Children.Add(Text("Installation de World of Warcraft", 18, FontWeight.SemiBold));
        fields.Children.Add(Text("Sélectionne le dossier contenant Wow.exe. Aucun fichier WTF ne sera modifié.", 12, color: Muted));
        var path = new TextBox { Text = _settings.GamePath, Watermark = "C:\\Jeux\\ChromieCraft", CornerRadius = new(8) };
        var browse = Button("Parcourir…");
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Dossier de ChromieCraft", AllowMultiple = false });
            if (folders.Count > 0) path.Text = folders[0].TryGetLocalPath();
        };
        fields.Children.Add(path); fields.Children.Add(browse);
        fields.Children.Add(new Border { Height = 1, Background = Line, Margin = new(0, 8) });
        fields.Children.Add(Text("Mises à jour intégrées du launcher", 18, FontWeight.SemiBold));
        fields.Children.Add(Text("Détection automatique, téléchargement vérifié et redémarrage depuis cette application.", 12, color: Muted));
        var automatic = new CheckBox { Content = "Vérifier les mises à jour au démarrage", IsChecked = _settings.CheckUpdatesAtStartup };
        fields.Children.Add(automatic);
        var feed = new TextBox { Text = _settings.UpdateFeedUrl, Watermark = "https://…/update.json", CornerRadius = new(8) };
        fields.Children.Add(Text("Adresse du manifeste signé de l’éditeur", 12, color: Muted)); fields.Children.Add(feed);
        fields.Children.Add(Notice("VERSION DE DÉVELOPPEMENT", "Le mécanisme de mise à jour est intégré. Sa distribution nécessite un hébergement des releases et une clé publique de signature intégrée à la compilation. Aucun téléchargement de logiciel non signé n’est autorisé."));
        var save = Button("Enregistrer les paramètres", "primary");
        save.Click += async (_, _) => await Run("Enregistrement des paramètres", async () =>
        {
            var validated = AddonService.ValidateGame(path.Text ?? "");
            var feedUrl = feed.Text?.Trim() ?? "";
            if (feedUrl.Length > 0 && (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")) throw new IOException("Le flux de mise à jour doit utiliser HTTPS.");
            _settings.GamePath = validated; _settings.UpdateFeedUrl = feedUrl; _settings.CheckUpdatesAtStartup = automatic.IsChecked == true;
            _storage.SaveSettings(_settings); await ReloadAsync();
        });
        fields.Children.Add(save);
        var data = Button("Ouvrir les données et journaux"); data.Click += (_, _) => OpenFolder(_storage.Root); fields.Children.Add(data);
        _body.Children.Add(new ScrollViewer { Content = Card(fields) });
    }

    private void RenderDownloads()
    {
        var notice = Notice("SAUVEGARDES LOCALES", "Les dossiers remplacés ou retirés sont conservés dans Interface\\.chromie-launcher\\transactions. Les réglages dans WTF ne sont jamais supprimés. Les archives ne sont pas purgées automatiquement.");
        DockPanel.SetDock(notice, Dock.Top); notice.Margin = new(0, 0, 0, 16); _body.Children.Add(notice);
        var backups = Button("Ouvrir les sauvegardes");
        backups.Click += (_, _) =>
        {
            try
            {
                var path = AddonService.BackupDirectory(_settings.GamePath);
                if (Directory.Exists(path)) OpenFolder(path); else _status.Text = "Aucune sauvegarde pour le moment.";
            }
            catch (Exception e) { ReportError(e); }
        };
        DockPanel.SetDock(backups, Dock.Top); backups.Margin = new(0, 0, 0, 16); _body.Children.Add(backups);
        var history = new StackPanel { Spacing = 10 };
        if (_history.Count == 0) history.Children.Add(Empty("Aucun téléchargement pour le moment", "Les installations et mises à jour apparaîtront ici pendant cette session."));
        foreach (var entry in _history.AsEnumerable().Reverse().Take(50)) history.Children.Add(Card(Text(entry, 12, color: Muted)));
        _body.Children.Add(new ScrollViewer { Content = history });
    }

    private async Task ReloadAsync()
    {
        var path = _settings.GamePath;
        var result = await Task.Run(() => (_addons.Scan(path), _addons.GetState(path)));
        _installed = result.Item1; _managed = result.Item2;
        RenderList();
    }

    private async Task SmokeTestAsync()
    {
        var output = Path.GetFullPath(Program.SmokeTestOutput!);
        var directory = Path.GetDirectoryName(output)!;
        var stem = Path.GetFileNameWithoutExtension(output);
        Directory.CreateDirectory(directory);
        void Capture(string path)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
            bitmap.Render(_root); bitmap.Save(path);
        }
        try
        {
            Capture(output);
            foreach (var (page, suffix) in new[] { ("Bibliothèque", "library"), ("Paramètres", "settings"), ("Téléchargements", "downloads"), ("Mes addons", "installed") })
            {
                Navigate(page); await Task.Delay(200);
                Capture(Path.Combine(directory, stem + "-" + suffix + ".png"));
            }
            Navigate("Bibliothèque"); _search.Text = "Bagnon";
            await Task.Delay(100); // Avalonia delivers TextChanged on the next dispatcher pass.
            if (!_count.Text!.StartsWith("1 packs")) throw new InvalidOperationException("Le filtre de recherche du catalogue ne fonctionne pas : " + _count.Text);
            _search.Text = "__no_result__";
            await Task.Delay(100);
            if (!_count.Text!.StartsWith("0 packs")) throw new InvalidOperationException("Le filtre vide ne fonctionne pas.");
            _search.Text = ""; await Task.Delay(100); Navigate("Mes addons");
            File.WriteAllText(Path.Combine(directory, stem + "-smoke.txt"), $"OK — navigation 4 pages, recherche, {_installed.Count} addons détectés, {_catalog.Packages.Count} packs. Aucune modification du jeu.");
        }
        catch (Exception error)
        {
            Program.SmokeTestFailed = true;
            File.WriteAllText(Path.Combine(directory, stem + "-smoke-error.txt"), error.ToString());
        }
        finally { Close(); }
    }

    private async Task InstallAddon(AddonPackage package)
    {
        if (_busy) return;
        try
        {
            var game = AddonService.ValidateGame(_settings.GamePath);
            var resolved = CatalogService.Resolve(_catalog, package.Id);
            var replacements = resolved.SelectMany(p => p.Folders).Where(f => Directory.Exists(SafePath.Child(game, "Interface/AddOns/" + f))).ToList();
            var message = $"Installer {package.Name} depuis l’archive historique WoWGaming ?\n\nVersion proposée : {package.Version}\nPacks et dépendances : {string.Join(", ", resolved.Select(p => p.Name))}\n\n";
            message += replacements.Count > 0 ? "Dossiers déjà présents pouvant être remplacés :\n" + string.Join(", ", replacements) + "\n\nCette archive peut être plus ancienne que tes addons actuels. Une sauvegarde sera conservée." : "Les fichiers seront installés dans Interface\\AddOns. Tes réglages WTF seront conservés.";
            if (!await Confirm("Installer un addon", message, replacements.Count > 0 ? "Sauvegarder et installer" : "Installer")) return;
            await Run("Installation : " + package.Name, async () =>
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
        if (!await Confirm("Retirer un addon", "Déplacer vers les sauvegardes :\n" + string.Join(", ", folders) + "\n\nLes réglages WTF seront conservés. Les dépendances requises par d’autres addons bloquent la désinstallation.", "Retirer et sauvegarder")) return;
        await Run("Désinstallation : " + addon.Name, async () =>
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
            if (await Confirm("Mise à jour prête", "Le launcher va redémarrer pour appliquer la mise à jour. Tes addons et réglages ne seront pas modifiés.", "Redémarrer et installer"))
            {
                try { _updater.LaunchReplacement(_preparedUpdate); Close(); }
                catch (Exception error) { ReportError(error); }
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(_settings.UpdateFeedUrl))
        {
            if (!silent) { Navigate("Paramètres"); _status.Text = "Aucun flux de mise à jour publié. Consulte la section Mises à jour intégrées."; }
            return;
        }
        (SignedUpdate Envelope, UpdateRelease Release)? available = null;
        var checkedSuccessfully = false;
        await Run("Vérification des mises à jour", async () =>
        {
            available = await _updater.CheckAsync(_settings.UpdateFeedUrl);
            checkedSuccessfully = true;
        });
        if (available is null)
        {
            if (checkedSuccessfully) _status.Text = "Le launcher est à jour (version " + UpdateService.DisplayVersion + ").";
            return;
        }
        var update = available.Value;
        if (!await Confirm("Version " + update.Release.Version + " disponible", update.Release.Notes + "\n\nTélécharger cette mise à jour signée ? Elle sera appliquée uniquement après ton accord pour redémarrer.", "Télécharger")) return;
        await Run("Téléchargement du launcher", async () =>
        {
            _preparedUpdate = await _updater.PrepareAsync(update.Envelope, new Progress<string>(text => _status.Text = text), _operation!.Token);
            _update.Content = "Redémarrer pour mettre à jour";
        }, cancellable: true);
    }

    private async Task Run(string label, Func<Task> action, bool cancellable = false)
    {
        if (_busy) return;
        _busy = true; _body.IsEnabled = false; _navigation.IsEnabled = false;
        _play.IsEnabled = false; _refresh.IsEnabled = false; _update.IsEnabled = false;
        _progress.IsVisible = true; _cancel.IsVisible = cancellable;
        _operation?.Dispose(); _operation = new();
        _status.Text = label + "…"; Log(label);
        try { await action(); _status.Text = label + " — terminé"; Log(label + " — terminé"); }
        catch (OperationCanceledException) { _status.Text = "Opération annulée. Aucun remplacement effectué."; Log(label + " — annulé"); }
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
            _status.Text = "World of Warcraft lancé.";
        }
        catch (Exception error) { ReportError(error); }
    }

    private void ReportError(Exception error)
    {
        _status.Text = "Erreur : " + error.Message;
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

    private async Task<bool> Confirm(string title, string message, string action)
    {
        var dialog = new Window { Title = title, Width = 580, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush.Parse("#20222D") };
        var panel = new StackPanel { Spacing = 20, Margin = new(28) };
        panel.Children.Add(Text(title, 22, FontWeight.SemiBold));
        var copy = Text(message, 13, color: Muted); copy.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(new ScrollViewer { Content = copy, MaxHeight = 350 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button("Annuler"); cancel.Click += (_, _) => dialog.Close(false);
        var accept = Button(action, "primary"); accept.Click += (_, _) => dialog.Close(true);
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
    { Text = text, FontSize = size, FontWeight = weight ?? FontWeight.Normal, Foreground = color ?? Brushes.White };
    private static Button Button(string text, string? style = null)
    {
        var button = new Button { Content = text };
        if (style is not null) button.Classes.Add(style);
        return button;
    }
    private static Border Card(Control content) => new() { Background = Surface, BorderBrush = Line, BorderThickness = new(1), CornerRadius = new(12), Padding = new(18), Child = content };
    private static Border Notice(string title, string message)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(Text(title, 10, FontWeight.Bold, Brush.Parse("#D7B778")));
        var body = Text(message, 12, color: Muted); body.TextWrapping = TextWrapping.Wrap; stack.Children.Add(body);
        return new Border { Background = Brush.Parse("#27251F"), BorderBrush = Brush.Parse("#49402D"), BorderThickness = new(1), CornerRadius = new(10), Padding = new(16), Child = stack };
    }
    private static Border Empty(string title, string description)
    {
        var stack = new StackPanel { Spacing = 12, Margin = new(12, 25) };
        stack.Children.Add(Text(title, 18, FontWeight.SemiBold));
        stack.Children.Add(Text(description, 12, color: Muted));
        return Card(stack);
    }
    private static Border Monogram(string name, bool installed) => new()
    {
        Width = 46, Height = 46, CornerRadius = new(10), VerticalAlignment = VerticalAlignment.Top,
        Background = Brush.Parse(installed ? "#233B34" : "#332B48"),
        Child = new TextBlock { Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brush.Parse(installed ? "#7DD3B0" : "#C4B5FD"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
    };
}
