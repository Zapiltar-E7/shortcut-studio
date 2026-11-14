using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using ShortcutStudio.Core;

namespace ShortcutStudio;

public partial class MainWindow : Window
{
    readonly AppSettings _settings = AppSettings.Load();
    readonly ObservableCollection<GameVm> _games = new();
    readonly List<GameVm> _deleted = new();
    ShortcutFile? _file;
    SteamUser? _user;
    GameVm? _current;
    bool _switchingAccount;

    const string ImageFilter = "Images|*.png;*.jpg;*.jpeg|All files|*.*";

    public MainWindow()
    {
        InitializeComponent();
        var view = CollectionViewSource.GetDefaultView(_games);
        view.SortDescriptions.Add(new SortDescription(nameof(GameVm.Name), ListSortDirection.Ascending));
        view.Filter = o => o is GameVm g && (FilterBox.Text.Length == 0 || g.Name.Contains(FilterBox.Text, StringComparison.OrdinalIgnoreCase));
        GameList.ItemsSource = view;
        ShowBanner(!_settings.HideBanner);

        Loaded += (_, _) => Init();
        Closing += OnClosing;
    }

    void Init()
    {
        if (Steam.InstallPath == null)
        {
            MessageBox.Show(this, "Steam isn't installed for this Windows user, so there's nothing to edit.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }
        var users = Steam.Users();
        AccountBox.ItemsSource = users;
        // The signed-in account comes first; only fall back to the last one used when nobody is signed in.
        var signedIn = users.FirstOrDefault();
        AccountBox.SelectedItem = signedIn is { PersonaName.Length: > 0 } ? signedIn
            : users.FirstOrDefault(u => u.AccountId == _settings.LastAccountId) ?? signedIn;
        UpdatePluginStatus();
    }

    // ---------- Accounts & list ----------

    bool AnyDirty => _games.Any(g => g.IsDirty) || _deleted.Count > 0;

    void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_switchingAccount || AccountBox.SelectedItem is not SteamUser user) return;
        if (_user != null && AnyDirty && !ConfirmDiscard())
        {
            _switchingAccount = true;
            AccountBox.SelectedItem = _user;
            _switchingAccount = false;
            return;
        }
        LoadAccount(user);
    }

    void LoadAccount(SteamUser user)
    {
        _user = user;
        _settings.LastAccountId = user.AccountId;
        _settings.Save();
        _deleted.Clear();
        _games.Clear();
        try
        {
            _file = ShortcutFile.Load(Steam.ShortcutsFile(user.AccountId));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't read this account's shortcuts.vdf:\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
            _file = null;
            return;
        }
        foreach (var s in _file.Items) _games.Add(Track(new GameVm(user.AccountId, s, false)));
        UpdateCount();
        Select(null);
        Status($"{_games.Count} non-Steam games in {user}'s library.");
    }

    GameVm Track(GameVm g)
    {
        g.PropertyChanged += Game_PropertyChanged;
        return g;
    }

    void Game_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == _current && e.PropertyName is nameof(GameVm.LogoPinned) or nameof(GameVm.LogoWidth) or nameof(GameVm.LogoHeight) or nameof(GameVm.LogoImage))
            UpdateLogo();
        if (e.PropertyName == nameof(GameVm.Name)) CollectionViewSource.GetDefaultView(_games).Refresh();
        if (e.PropertyName == nameof(GameVm.IsDirty)) UpdateButtons();
    }

    void UpdateCount()
    {
        int shown = CollectionViewSource.GetDefaultView(_games).Cast<object>().Count();
        CountText.Text = shown == _games.Count ? $"NON-STEAM GAMES ({_games.Count})" : $"NON-STEAM GAMES ({shown} OF {_games.Count})";
    }

    void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        FilterHint.Visibility = FilterBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CollectionViewSource.GetDefaultView(_games).Refresh();
        UpdateCount();
    }

    void GameList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (GameList.SelectedItem is GameVm g) Select(g);
    }

    void Select(GameVm? g)
    {
        _current = g;
        Editor.DataContext = g;
        Editor.Visibility = g == null ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = g == null ? Visibility.Visible : Visibility.Collapsed;
        if (g != null && GameList.SelectedItem != g) GameList.SelectedItem = g;
        GroupAchievements(g);
        UpdateLogo();
        UpdateButtons();
    }

    void UpdateButtons()
    {
        SaveBtn.IsEnabled = AnyDirty;
        RevertBtn.IsEnabled = AnyDirty;
    }

    void Status(string text) => StatusText.Text = text;

    // ---------- Logo preview ----------

    void LogoArea_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLogo();

    /// <summary>Fits the logo inside Width% × Height% of the hero and pins it, the way Steam's library page does.</summary>
    void UpdateLogo()
    {
        if (_current == null) return;
        LogoPreview.MaxWidth = Math.Max(1, LogoArea.ActualWidth * _current.LogoWidth / 100);
        LogoPreview.MaxHeight = Math.Max(1, LogoArea.ActualHeight * _current.LogoHeight / 100);
        var p = _current.LogoPinned;
        LogoPreview.HorizontalAlignment = p.EndsWith("Left") ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        LogoPreview.VerticalAlignment = p.StartsWith("Bottom") ? VerticalAlignment.Bottom : p.StartsWith("Upper") ? VerticalAlignment.Top : VerticalAlignment.Center;
    }

    // ---------- Adding / removing games ----------

    void AddGame_Click(object sender, RoutedEventArgs e)
    {
        if (_file == null || _user == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Choose the game's program",
            Filter = "Programs|*.exe;*.bat;*.cmd;*.lnk;*.url;*.ps1|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        var path = dlg.FileName;
        var name = GuessName(path);
        var s = Shortcut.Create(name, path);
        // Two shortcuts with the same name and target would collide; nudge the id until it's free.
        while (_file.Items.Any(x => x.AppId == s.AppId) || _games.Any(x => x.AppId == s.AppId)) s.AppId = (s.AppId + 1) | 0x80000000;

        var g = Track(new GameVm(_user.AccountId, s, true));
        _games.Add(g);
        UpdateCount();
        Select(g);
        Tabs.SelectedIndex = 0;
        Status($"Added \"{name}\". Use Import from Steam to copy an official game's look, then Save to Steam.");
    }

    static string GuessName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var product = info.ProductName?.Trim();
            if (!string.IsNullOrEmpty(product) && product.Length < 60 && !product.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                return product;
        }
        catch { }
        return Path.GetFileNameWithoutExtension(path);
    }

    void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (MessageBox.Show(this, $"Remove \"{_current.Name}\" from Steam when you save?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var g = _current;
        _games.Remove(g);
        if (!g.IsNew) { g.IsDeleted = true; _deleted.Add(g); }
        UpdateCount();
        Select(null);
        UpdateButtons();
        Status($"\"{g.Name}\" will be removed when you save.");
    }

    // ---------- Details ----------

    void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var dlg = new OpenFileDialog { Title = "Choose the program", Filter = "Programs|*.exe;*.bat;*.cmd;*.lnk;*.url;*.ps1|All files|*.*" };
        if (File.Exists(_current.Exe)) dlg.InitialDirectory = Path.GetDirectoryName(_current.Exe);
        if (dlg.ShowDialog(this) != true) return;
        _current.Exe = dlg.FileName;
        _current.StartDir = Path.GetDirectoryName(dlg.FileName) ?? "";
    }

    void BrowseDir_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var dlg = new OpenFolderDialog { Title = "Start in" };
        if (Directory.Exists(_current.StartDir)) dlg.InitialDirectory = _current.StartDir;
        if (dlg.ShowDialog(this) == true) _current.StartDir = dlg.FolderName;
    }

    void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        try
        {
            var psi = new ProcessStartInfo(_current.Exe, _current.LaunchOptions) { UseShellExecute = true };
            if (Directory.Exists(_current.StartDir)) psi.WorkingDirectory = _current.StartDir;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't start the game:\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Artwork ----------

    static ArtSlotVm? SlotOf(object sender) => (sender as FrameworkElement)?.Tag as ArtSlotVm;

    void ArtFile_Click(object sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is not { } slot) return;
        var dlg = new OpenFileDialog { Title = slot.Spec.Label, Filter = ImageFilter };
        if (dlg.ShowDialog(this) == true) SetArtFromFile(slot, dlg.FileName);
    }

    void Art_Drop(object sender, DragEventArgs e)
    {
        if (SlotOf(sender) is not { } slot) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) SetArtFromFile(slot, files[0]);
    }

    void SetArtFromFile(ArtSlotVm slot, string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (Images.Load(bytes) == null)
        {
            MessageBox.Show(this, "That file isn't a PNG or JPEG image this app can read.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        slot.SetImage(bytes);
    }

    void ArtRemove_Click(object sender, RoutedEventArgs e) => SlotOf(sender)?.Remove();

    void ArtSgdb_Click(object sender, RoutedEventArgs e)
    {
        if (SlotOf(sender) is { } slot) OpenSgdb(slot.Kind);
    }

    void SgdbAll_Click(object sender, RoutedEventArgs e) => OpenSgdb(ArtKind.Capsule);

    void OpenSgdb(ArtKind start)
    {
        if (_current == null) return;
        if (string.IsNullOrWhiteSpace(_settings.SteamGridDbKey))
        {
            if (MessageBox.Show(this, "SteamGridDB needs a free API key. Add one in Settings now?", Title, MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;
            OpenSettings();
            if (string.IsNullOrWhiteSpace(_settings.SteamGridDbKey)) return;
        }
        var win = new SgdbWindow(_settings.SteamGridDbKey, _current.Name, _current.SourceAppId, start) { Owner = this };
        win.ShowDialog();
        foreach (var (kind, bytes) in win.Chosen) _current.Slot(kind).SetImage(bytes);
        if (win.Chosen.Count > 0) Status($"Picked {win.Chosen.Count} image(s) from SteamGridDB. Save to Steam to apply.");
    }

    // ---------- Import from Steam ----------

    async void ImportSteam_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var game = _current;
        var dlg = new ImportWindow(game.Name, game.SourceAppId) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected == null) return;

        var o = dlg.Options;
        await RunBusy($"Importing {dlg.Selected.Name}…", async () =>
        {
            var store = await SteamStore.GetGameAsync(dlg.Selected.AppId) ?? throw new InvalidOperationException("Steam didn't return that game.");
            if (o.Name) game.Name = store.Name;
            if (o.Details)
            {
                game.ShortDescription = store.ShortDescription;
                game.Description = store.Description;
                game.Developer = store.Developer;
                game.Publisher = store.Publisher;
                game.ReleaseDate = store.ReleaseDate;
                game.Genres = string.Join(", ", store.Genres);
            }
            game.SourceAppId = store.AppId;

            if (o.Art)
            {
                var missing = new List<string>();
                foreach (var (kind, urls) in store.Art)
                {
                    SetBusy($"Downloading {Artwork.Spec(kind).Label.ToLower()}…");
                    var bytes = await SteamStore.DownloadFirstAsync(urls);
                    if (bytes != null) game.Slot(kind).SetImage(bytes);
                    else missing.Add(Artwork.Spec(kind).Label);
                }
                // Official library logos sit bottom-left at about this size.
                game.LogoPinned = "BottomLeft";
                game.LogoWidth = 50;
                game.LogoHeight = 50;
                if (missing.Count > 0) Status("Steam has no " + string.Join(", ", missing).ToLower() + " for this game; try SteamGridDB for those.");
            }

            if (o.Achievements)
            {
                SetBusy("Reading achievements…");
                var list = await SteamStore.GetAchievementsAsync(store.AppId);
                await ImportAchievements(game, list);
            }
        });
        Tabs.SelectedIndex = o.Art ? 1 : o.Details ? 2 : o.Achievements ? 3 : 0;
        if (!StatusText.Text.StartsWith("Steam has no")) Status($"Imported from {dlg.Selected.Name}. Check everything, then Save to Steam.");
    }

    async Task ImportAchievements(GameVm game, List<AchievementSchema> list)
    {
        // Keep the unlock state of achievements that are being replaced by ones with the same name.
        var previous = game.Achievements.Where(a => a.Unlocked).ToDictionary(a => a.Name, a => a.UnlockedAt, StringComparer.OrdinalIgnoreCase);
        game.Achievements.Clear();

        using var gate = new SemaphoreSlim(8);
        int done = 0;
        var vms = await Task.WhenAll(list.Select(async s =>
        {
            await gate.WaitAsync();
            try
            {
                var color = string.IsNullOrEmpty(s.IconUrl) ? null : await SteamStore.DownloadFirstAsync(new[] { s.IconUrl });
                var gray = string.IsNullOrEmpty(s.IconGrayUrl) ? null : await SteamStore.DownloadFirstAsync(new[] { s.IconGrayUrl });
                var n = Interlocked.Increment(ref done);
                Dispatcher.Invoke(() => SetBusy($"Downloading achievement icons ({n}/{list.Count})…"));
                return (s, color, gray);
            }
            finally { gate.Release(); }
        }));

        foreach (var (s, color, gray) in vms)
        {
            var model = new Achievement
            {
                Id = MakeId(s.ApiName, game),
                Name = s.Name,
                Description = s.Description,
                Hidden = s.Hidden,
                GlobalPercent = s.Percent,
            };
            var vm = new AchievementVm(game, model, previous.TryGetValue(s.Name, out var t) ? t : null);
            vm.SetIcon(color, false);
            vm.SetIcon(gray, true);
            game.Achievements.Add(vm);
        }
        game.MarkDirty();
    }

    static string MakeId(string apiName, GameVm game)
    {
        var id = string.Concat(apiName.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
        if (id.Length == 0) id = Guid.NewGuid().ToString("N")[..12];
        var unique = id;
        for (int i = 2; game.Achievements.Any(a => a.Id == unique); i++) unique = $"{id}_{i}";
        return unique;
    }

    // ---------- Launch menu ----------

    void LaunchAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var g = _current;
        // The first option is the shortcut as it is, so it stays available once there's a menu.
        if (g.LaunchChoices.Count == 0)
            g.LaunchChoices.Add(new LaunchOptionVm(g, new LaunchOption { Name = $"Play {g.Name}", Args = g.LaunchOptions }));
        g.LaunchChoices.Add(new LaunchOptionVm(g, new LaunchOption { Name = $"Play {g.Name} (option {g.LaunchChoices.Count + 1})" }));
        g.LaunchChoicesChanged();
    }

    void LaunchRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || (sender as FrameworkElement)?.Tag is not LaunchOptionVm o) return;
        _current.LaunchChoices.Remove(o);
        // A single option is just the normal launch; drop the menu.
        if (_current.LaunchChoices.Count == 1) _current.LaunchChoices.Clear();
        _current.LaunchChoicesChanged();
    }

    void LaunchForget_Click(object sender, RoutedEventArgs e) => _current?.ForgetLaunchChoice();

    // ---------- Fetch info (description and details) ----------

    async void FetchInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var game = _current;
        var dlg = new FetchInfoWindow(game.Name) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } pick) return;

        await RunBusy($"Fetching {pick.Title}…", async () =>
        {
            if (pick.SteamAppId > 0)
            {
                var s = await SteamStore.GetGameAsync(pick.SteamAppId) ?? throw new InvalidOperationException("Steam didn't return that game.");
                Fill(game, s.ShortDescription, s.Description, s.Developer, s.Publisher, s.ReleaseDate, s.Genres);
                game.SourceAppId = s.AppId;
            }
            else
            {
                var w = await Wikipedia.GetAsync(pick.WikiTitle);
                Fill(game, w.ShortDescription, w.Description, w.Developer, w.Publisher, w.ReleaseDate, w.Genres);
            }
        });
        Tabs.SelectedIndex = 2;
        Status($"Filled the store page from {(pick.SteamAppId > 0 ? "Steam" : "Wikipedia")} ({pick.Title}). Check it, then Save to Steam.");
    }

    /// <summary>Copies fetched details in, keeping whatever the source didn't have.</summary>
    static void Fill(GameVm g, string shortDesc, string desc, string dev, string pub, string date, List<string> genres)
    {
        if (shortDesc != "") g.ShortDescription = shortDesc;
        if (desc != "") g.Description = desc;
        if (dev != "") g.Developer = dev;
        if (pub != "") g.Publisher = pub;
        if (date != "") g.ReleaseDate = date;
        if (genres.Count > 0) g.Genres = string.Join(", ", genres);
    }

    // ---------- Emulator trophies ----------

    async void EmulatorTrophies_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var game = _current;
        var dlg = new TrophyWindow(game.Exe, game.StartDir, game.LaunchOptions, game.Name) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } set) return;

        if (game.Achievements.Count > 0 && game.TrophySource == null &&
            MessageBox.Show(this, $"Replace this game's {game.Achievements.Count} achievements with {set.Title}'s {set.Trophies.Count} trophies?",
                Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        await RunBusy("Importing trophies…", async () =>
        {
            var unlocks = set.Unlocks;
            var manual = game.Achievements.Where(a => a.Unlocked && !a.FromEmulator).ToDictionary(a => a.Id, a => a.UnlockedAt);
            game.Achievements.Clear();
            foreach (var t in set.Trophies)
            {
                var model = new Achievement
                {
                    Id = EmulatorTrophies.AchievementId(t.Id),
                    Name = t.Name,
                    Description = t.Detail,
                    Hidden = t.Hidden,
                    Grade = t.Grade,
                    TrophyId = t.Id,
                    Dlc = t.Dlc,
                };
                var vm = unlocks.TryGetValue(t.Id, out var ts)
                    ? new AchievementVm(game, model, ts, fromEmulator: true)
                    : new AchievementVm(game, model, manual.GetValueOrDefault(model.Id));
                // Trophy icons are 120–240 px; Steam shows 64, so keep a 128 px copy. Locked ones are greyed automatically.
                if (t.Icon != null) vm.SetIcon(await Task.Run(() => IconResize.To64(t.Icon)), false);
                game.Achievements.Add(vm);
            }
            game.TrophySource = new TrophySource
            {
                Emulator = set.Emulator,
                UnlockFile = set.UnlockFile,
                NpCommId = set.NpCommId,
                Title = set.Title,
                User = set.User,
            };
            game.MarkDirty();
        });
        Status($"Imported {set.Trophies.Count} trophies from {set.Title} ({set.UnlockedCount} already unlocked). Save to Steam to start syncing.");
    }

    async void RetroAchievements_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var game = _current;
        if (string.IsNullOrWhiteSpace(_settings.RaUser) || string.IsNullOrWhiteSpace(_settings.RaKey))
        {
            if (MessageBox.Show(this, "RetroAchievements needs your username and Web API key. Add them in Settings now?", Title, MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;
            OpenSettings();
            if (string.IsNullOrWhiteSpace(_settings.RaUser) || string.IsNullOrWhiteSpace(_settings.RaKey)) return;
        }

        var dlg = new RaWindow(_settings.RaKey, game.Name, RetroAchievements.GuessConsole(game.Exe, game.LaunchOptions)) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } pick) return;
        if (game.Achievements.Count > 0 && game.TrophySource?.Emulator != "retroachievements" &&
            MessageBox.Show(this, $"Replace this game's {game.Achievements.Count} achievements with {pick.Title}'s {pick.NumAchievements}?",
                Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        RaGame? ra = null;
        await RunBusy($"Importing {pick.Title}…", async () =>
        {
            ra = await RetroAchievements.GameProgressAsync(_settings.RaKey, _settings.RaUser, pick.Id);
            game.Achievements.Clear();
            int done = 0;
            using var gate = new SemaphoreSlim(6);
            var icons = await Task.WhenAll(ra.Achievements.Select(async a =>
            {
                await gate.WaitAsync();
                try
                {
                    var color = await SteamStore.DownloadFirstAsync(new[] { a.BadgeUrl });
                    var gray = await SteamStore.DownloadFirstAsync(new[] { a.LockedBadgeUrl });
                    var n = Interlocked.Increment(ref done);
                    Dispatcher.Invoke(() => SetBusy($"Downloading badges ({n}/{ra.Achievements.Count})…"));
                    return (a, color, gray);
                }
                finally { gate.Release(); }
            }));
            foreach (var (a, color, gray) in icons)
            {
                var model = new Achievement
                {
                    Id = $"ra-{a.Id}",
                    Name = a.Title,
                    Description = a.Description,
                    Points = a.Points,
                    TrophyId = a.Id,
                    GlobalPercent = ra.NumDistinctPlayers > 0 ? Math.Round(100.0 * a.NumAwarded / ra.NumDistinctPlayers, 1) : null,
                };
                var vm = new AchievementVm(game, model, a.EarnedMs, fromEmulator: a.EarnedMs != null);
                vm.SetIcon(color, false);
                vm.SetIcon(gray, true);
                game.Achievements.Add(vm);
            }
            RetroAchievements.WriteUnlockCache(ra);
            game.TrophySource = new TrophySource
            {
                Emulator = "retroachievements",
                UnlockFile = RetroAchievements.UnlockCacheFile(ra.Id),
                NpCommId = ra.Id.ToString(),
                Title = ra.Title,
                User = _settings.RaUser,
            };
            // Fill empty store-page fields from RA's game details.
            if (game.Developer == "") game.Developer = ra.Developer;
            if (game.Publisher == "") game.Publisher = ra.Publisher;
            if (game.ReleaseDate == "") game.ReleaseDate = ra.Released;
            if (game.Genres == "") game.Genres = ra.Genre;
            game.MarkDirty();
        });
        if (ra != null)
            Status($"Imported {ra.Achievements.Count} achievements from {ra.Title} ({ra.Achievements.Count(a => a.EarnedMs != null)} already earned). Save to Steam to start syncing.");
    }

    void StopSync_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        _current.TrophySource = null;
        Status("Trophies stay as achievements, but new emulator unlocks won't be picked up after you save.");
    }

    // ---------- Achievements ----------

    AchievementVm? SelectedAch => AchList.SelectedItem as AchievementVm;

    void AchAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var vm = new AchievementVm(_current, new Achievement { Name = "New achievement" }, null);
        _current.Achievements.Add(vm);
        _current.MarkDirty();
        AchList.SelectedItem = vm;
        AchList.ScrollIntoView(vm);
    }

    // ---- Achievement set fields: they act on every selected achievement, not just the one shown ----

    List<AchievementVm> SelectedAchs => AchList.SelectedItems.Cast<AchievementVm>().ToList();

    void ApplyDlc(string name, List<AchievementVm>? targets = null)
    {
        var picked = targets ?? SelectedAchs;
        if (picked.Count == 0) return;
        foreach (var a in picked) a.SetDlc(name);
        name = name.Trim();
        if (picked.Count > 1) Status(name == "" ? $"Moved {picked.Count} achievements to the base game." : $"Moved {picked.Count} achievements to DLC: {name}.");
    }

    void AchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int n = AchList.SelectedItems.Count;
        DlcMulti.Text = $"Set changes apply to all {n} selected achievements.";
        DlcMulti.Visibility = n > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    void DlcCheck_Click(object sender, RoutedEventArgs e)
    {
        // The binding has already updated the shown achievement (and picked a name for it); copy that to the rest.
        if (SelectedAch is not { } shown) return;
        ApplyDlc(shown.Dlc);
        if (shown.IsDlc) { DlcName.Focus(); DlcName.SelectAll(); }
    }

    // A name edit goes to the achievements that were selected when editing began, and only if the name changed,
    // so clicking in and out of the box never moves a mixed selection into the shown achievement's set.
    List<AchievementVm> _dlcTargets = new();
    string _dlcStart = "";

    void DlcName_GotFocus(object sender, RoutedEventArgs e)
    {
        _dlcTargets = SelectedAchs;
        _dlcStart = DlcName.Text;
    }

    void CommitDlcName()
    {
        if (DlcName.Text.Trim() == _dlcStart.Trim()) return;
        ApplyDlc(DlcName.Text, _dlcTargets);
        _dlcStart = DlcName.Text;
    }

    void DlcName_LostFocus(object sender, RoutedEventArgs e) => CommitDlcName();

    void DlcName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        DlcName.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        CommitDlcName();
        e.Handled = true;
    }

    /// <summary>The ▾ next to the name: pick one of the game's DLC sets, or go back to the base game.</summary>
    void DlcPick_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || sender is not Button btn) return;
        var menu = new ContextMenu { PlacementTarget = btn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Add(string header, string name)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => ApplyDlc(name);
            menu.Items.Add(item);
        }
        foreach (var n in _current.DlcNames) Add("DLC: " + n.Replace("_", "__"), n);
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        Add("Base game", "");
        menu.IsOpen = true;
    }

    /// <summary>Right-click menu: move the selected achievements to the base game, an existing DLC set, or a new one.</summary>
    void AchSetMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.Items[0] is not MenuItem root) return;
        root.Items.Clear();
        var picked = AchList.SelectedItems.Cast<AchievementVm>().ToList();
        root.IsEnabled = _current != null && picked.Count > 0;
        if (!root.IsEnabled) return;
        root.Header = picked.Count == 1 ? "Move to set" : $"Move {picked.Count} achievements to set";

        void Add(string header, Func<string?> name, bool check)
        {
            var item = new MenuItem { Header = header, IsCheckable = false, IsChecked = check };
            item.Click += (_, _) =>
            {
                if (name() is not { } n) return;
                foreach (var a in picked) a.SetDlc(n);
                Status(n == "" ? $"Moved {picked.Count} to the base game." : $"Moved {picked.Count} to DLC: {n}.");
            };
            root.Items.Add(item);
        }
        Add("Base game", () => "", picked.All(a => !a.IsDlc));
        var names = _current!.DlcNames;
        if (names.Count > 0) root.Items.Add(new Separator());
        foreach (var n in names) Add("DLC: " + n.Replace("_", "__"), () => n, picked.All(a => a.Dlc == n));
        root.Items.Add(new Separator());
        Add("New DLC set…", () => AskText("New DLC set", "DLC name, as Steam would show it after \"DLC:\"", ""), false);
    }

    /// <summary>A small one-line prompt. Returns null when cancelled or left blank.</summary>
    string? AskText(string title, string prompt, string value)
    {
        var box = new TextBox { Text = value, MinWidth = 320, Margin = new Thickness(0, 8, 0, 14) };
        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 0, 8, 0) };
        if (TryFindResource("Primary") is Style primary) ok.Style = primary;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 6, 14, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = title, Content = panel, Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)FindResource("Panel"), FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 13,
        };
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true && box.Text.Trim() is { Length: > 0 } t ? t : null;
    }

    void AchRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        foreach (var a in AchList.SelectedItems.Cast<AchievementVm>().ToList()) _current.Achievements.Remove(a);
        _current.MarkDirty();
    }

    void AchClear_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || _current.Achievements.Count == 0) return;
        if (MessageBox.Show(this, $"Remove all {_current.Achievements.Count} achievements?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _current.Achievements.Clear();
        _current.MarkDirty();
    }

    void Move(int delta)
    {
        if (_current == null || SelectedAch is not { } a) return;
        var list = _current.Achievements;
        int i = list.IndexOf(a), j = i + delta;
        // While the list is grouped by set, step past other sets' achievements so the move is visible in this one.
        if (_current.DlcNames.Count > 0)
            while (j >= 0 && j < list.Count && list[j].Dlc != a.Dlc) j += delta;
        if (j < 0 || j >= list.Count) return;
        list.Move(i, j);
        AchList.SelectedItem = a;
        _current.MarkDirty();
    }

    void AchUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    void AchDown_Click(object sender, RoutedEventArgs e) => Move(1);

    // ---------- Achievement sets in the list ----------

    GameVm? _groupedGame;
    /// <summary>Collapsed set headers, as "appid|set name" ("" is the base game), kept while the app runs.</summary>
    readonly HashSet<string> _collapsedSets = new();

    /// <summary>Groups the game's achievement list under Base Game / DLC set headers once it has a DLC set.</summary>
    void GroupAchievements(GameVm? g)
    {
        if (_groupedGame != null)
        {
            _groupedGame.PropertyChanged -= GroupedGame_Changed;
            _groupedGame.Achievements.CollectionChanged -= GroupedAchs_Changed;
        }
        _groupedGame = g;
        if (g == null) return;
        g.PropertyChanged += GroupedGame_Changed;
        g.Achievements.CollectionChanged += GroupedAchs_Changed;
        UpdateGrouping();
    }

    void GroupedGame_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameVm.DlcNames)) UpdateGrouping();
    }

    void GroupedAchs_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateGrouping();

    void UpdateGrouping()
    {
        if (_groupedGame is not { } g || CollectionViewSource.GetDefaultView(g.Achievements) is not ListCollectionView view) return;
        bool want = g.DlcNames.Count > 0;
        if (want == view.GroupDescriptions.Count > 0) return;
        var selected = AchList.SelectedItems.Cast<object>().ToList();
        using (view.DeferRefresh())
        {
            view.GroupDescriptions.Clear();
            view.LiveGroupingProperties.Clear();
            view.IsLiveGrouping = want;
            if (want)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AchievementVm.Dlc)) { CustomSort = new SetOrder(g.Achievements) });
                view.LiveGroupingProperties.Add(nameof(AchievementVm.Dlc));
            }
        }
        foreach (var o in selected) AchList.SelectedItems.Add(o);
    }

    string SetKey(CollectionViewGroup group) => $"{_current?.AppId}|{group.Name}";

    void SetExpander_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander x && x.DataContext is CollectionViewGroup group) x.IsExpanded = !_collapsedSets.Contains(SetKey(group));
    }

    void SetExpander_Changed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender || sender is not Expander { DataContext: CollectionViewGroup group } x) return;
        if (x.IsExpanded) _collapsedSets.Remove(SetKey(group));
        else _collapsedSets.Add(SetKey(group));
    }

    // ---------- Banner ----------

    void Banner_Click(object sender, RoutedEventArgs e)
    {
        _settings.HideBanner = Banner.Visibility == Visibility.Visible;
        ShowBanner(!_settings.HideBanner);
        _settings.Save();
    }

    void ShowBanner(bool show)
    {
        Banner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BannerBtn.Content = show ? "▲ Hide banner" : "▼ Show banner";
    }

    void AchUnlockAll_Click(object sender, RoutedEventArgs e) { foreach (var a in _current?.Achievements ?? new()) a.Unlocked = true; }
    void AchLockAll_Click(object sender, RoutedEventArgs e) { foreach (var a in _current?.Achievements ?? new()) a.Unlocked = false; }

    void AchIcon_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAch is not { } a) return;
        bool gray = (sender as FrameworkElement)?.Tag as string == "gray";
        var dlg = new OpenFileDialog { Title = gray ? "Locked icon" : "Unlocked icon", Filter = ImageFilter };
        if (dlg.ShowDialog(this) != true) return;
        var bytes = File.ReadAllBytes(dlg.FileName);
        if (Images.Load(bytes) == null) return;
        a.SetIcon(IconResize.To64(bytes), gray);
    }

    // ---------- Saving ----------

    void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        if (_user != null) LoadAccount(_user);
    }

    bool ConfirmDiscard() =>
        MessageBox.Show(this, "Discard all unsaved changes?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);

    /// <summary>Saves everything. Steam must be closed to change shortcuts.vdf, so it's closed and restarted around the write.</summary>
    async Task<bool> SaveAsync(bool installPlugin)
    {
        if (_file == null || _user == null) return false;
        var dirty = _games.Where(g => g.IsDirty).ToList();
        installPlugin |= PluginInstaller.NeedsInstall;

        bool needsClosedSteam = _deleted.Count > 0 || dirty.Any(g => g.TouchesSteamFiles) || installPlugin;

        // With the plugin running inside Steam, apply the changes through Steam's API instead of restarting it.
        if (needsClosedSteam && Steam.IsRunning && !installPlugin && !dirty.Any(g => g.NeedsRestart) && LiveBridge.IsAlive)
        {
            bool? live = null;
            await RunBusy("Saving to Steam…", async () => live = await SaveLiveAsync(dirty));
            UpdateButtons();
            if (live == true)
            {
                Status("Saved. Steam updated without a restart.");
                return true;
            }
            if (live == false) return false; // errors were shown; nothing else to do
            // No answer from the plugin: fall back to the restart below.
        }

        bool restart = false;
        if (needsClosedSteam && Steam.IsRunning)
        {
            var msg = "Steam has to be closed while its shortcut list is written.\n\nClose Steam, save, and start it again?";
            if (installPlugin && !dirty.Any() && _deleted.Count == 0) msg = "Steam has to be closed to install the Shortcut Studio plugin.\n\nClose Steam, install, and start it again?";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return false;
            restart = true;
        }

        bool ok = false;
        await RunBusy("Saving…", async () =>
        {
            if (restart)
            {
                SetBusy("Closing Steam…");
                if (!await Steam.ShutdownAsync(TimeSpan.FromSeconds(45)))
                    throw new InvalidOperationException("Steam didn't close in time. Close it yourself and save again.");
            }

            SetBusy("Writing…");
            // Re-read the file Steam just wrote on exit, so changes made in Steam meanwhile (playtime, new shortcuts) aren't lost.
            var fresh = ShortcutFile.Load(_file.Path);
            foreach (var g in _deleted)
            {
                fresh.Items.RemoveAll(s => s.AppId == g.AppId);
                foreach (var spec in Artwork.Specs) Artwork.Delete(_user.AccountId, g.AppId, spec.Kind);
                Artwork.WriteLogoPosition(_user.AccountId, g.AppId, null);
                ExtrasStore.Delete(g.AppId);
            }
            foreach (var g in dirty)
            {
                g.ApplyToShortcut();
                g.ApplyFiles();
                var existing = fresh.Items.FirstOrDefault(s => s.AppId == g.AppId);
                if (existing != null) g.CopyEditsTo(existing);
                else fresh.Items.Add(g.Shortcut);
            }
            if (needsClosedSteam) fresh.Save();
            _file = fresh;
            _deleted.Clear();

            if (installPlugin && Steam.HasMillennium && !Steam.IsRunning)
            {
                SetBusy("Installing the Steam plugin…");
                PluginInstaller.Install();
            }

            if (restart)
            {
                SetBusy("Starting Steam…");
                Steam.Start();
            }
            ok = true;
            await Task.CompletedTask;
        });

        UpdatePluginStatus();
        UpdateButtons();
        if (ok)
        {
            Status(restart ? "Saved. Steam is restarting with your changes."
                 : needsClosedSteam ? "Saved. Start Steam to see your changes."
                 : "Saved. Reopen the game's page in Steam to see the new description and achievements.");
        }
        return ok;
    }

/// <summary>
    /// Saves through the plugin: new shortcuts are added by Steam (which picks their appid), then names, paths,
    /// artwork and logo positions are set with Steam's own calls. Steam writes shortcuts.vdf itself later.
    /// Returns true when done, false on errors, null if the plugin didn't answer (nothing was changed then).
    /// </summary>
    async Task<bool?> SaveLiveAsync(List<GameVm> dirty)
    {
        var timeout = TimeSpan.FromSeconds(20);

        // 1. New shortcuts: Steam assigns the appid, and everything else is keyed on it.
        var added = dirty.Where(g => g.IsNew).ToList();
        if (added.Count > 0)
        {
            SetBusy("Adding to Steam…");
            // AddShortcut takes plain paths and adds the quotes itself (quoted ones make Steam read the folder as arguments).
            var ops = added.Select(g => LiveBridge.Op("add", 0,
                ("name", g.Name.Trim()), ("exe", g.Exe.Trim().Trim('"')), ("startDir", g.StartDir.Trim().Trim('"')), ("launchOptions", g.LaunchOptions)));
            var res = await LiveBridge.SendAsync(ops, timeout);
            if (res == null) return null;
            for (int i = 0; i < added.Count && i < res.Results.Count; i++)
            {
                var newId = (long?)res.Results[i]["newAppid"];
                if (newId is null or 0)
                {
                    MessageBox.Show(this, $"Steam couldn't add \"{added[i].Name}\": {(string?)res.Results[i]["error"] ?? "no appid returned"}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                // Data saved in the app before now (none for a new game) would be under the old id; move it.
                var oldId = added[i].Shortcut.AppId;
                added[i].Shortcut.AppId = unchecked((uint)newId.Value);
                ExtrasStore.Rename(oldId, added[i].Shortcut.AppId);
                added[i].IsNew = false;
                added[i].RaiseIdentity();
            }
        }

        // 2. Everything else in one request.
        SetBusy("Updating Steam…");
        var ops2 = new List<System.Text.Json.Nodes.JsonObject>();
        foreach (var g in dirty)
        {
            bool hiddenChanged = g.Hidden != g.Shortcut.IsHidden;
            g.ApplyToShortcut();
            g.ApplyFiles();
            var id = g.AppId;
            ops2.Add(LiveBridge.Op("update", id, ("name", g.Shortcut.Name), ("exe", g.Shortcut.Exe), ("startDir", g.Shortcut.StartDir), ("launchOptions", g.Shortcut.LaunchOptions)));
            if (hiddenChanged) ops2.Add(LiveBridge.Op("hidden", id, ("hidden", g.Hidden)));
            foreach (var (kind, path) in g.LastArtChanges)
            {
                if (kind == ArtKind.Icon) { ops2.Add(LiveBridge.Op("icon", id, ("path", path ?? ""))); continue; }
                var k = kind.ToString().ToLowerInvariant(); // capsule, wide, hero, logo
                ops2.Add(path != null ? LiveBridge.Op("art", id, ("kind", k), ("path", path)) : LiveBridge.Op("artClear", id, ("kind", k)));
            }
            if (g.LastLogoChanged)
                ops2.Add(LiveBridge.Op("logo", id, ("pinned", g.LogoPinned), ("widthPct", g.LogoWidth), ("heightPct", g.LogoHeight)));
        }
        foreach (var g in _deleted)
        {
            ops2.Add(LiveBridge.Op("remove", g.AppId));
            foreach (var spec in Artwork.Specs) Artwork.Delete(_user!.AccountId, g.AppId, spec.Kind);
            Artwork.WriteLogoPosition(_user!.AccountId, g.AppId, null);
            ExtrasStore.Delete(g.AppId);
        }

        var result = ops2.Count == 0 ? new LiveBridge.Result(true, new()) : await LiveBridge.SendAsync(ops2, timeout);
        if (result == null)
        {
            MessageBox.Show(this, "The Steam plugin stopped answering partway through. Your changes are saved in the app; save again to finish (Steam will be restarted if needed).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            foreach (var g in dirty) g.MarkDirty();
            return false;
        }
        _deleted.Clear();
        var errors = result.Results.Where(r => r["error"] != null).Select(r => $"{(string?)r["op"]}: {(string?)r["error"]}").ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, "Some changes couldn't be applied in Steam:\n\n" + string.Join("\n", errors.Distinct()), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!AnyDirty) return;
        var r = MessageBox.Show(this, "You have unsaved changes. Save them to Steam before closing?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) e.Cancel = true;
        else if (r == MessageBoxResult.Yes)
        {
            e.Cancel = true;
            _ = SaveAsync(false).ContinueWith(t => { if (t.Result) Dispatcher.Invoke(() => { _games.Clear(); _deleted.Clear(); Close(); }); });
        }
    }

    // ---------- Settings & plugin ----------

    void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    async void OpenSettings()
    {
        var win = new SettingsWindow(_settings) { Owner = this };
        if (win.ShowDialog() == true && win.InstallPluginRequested) await SaveAsync(true);
        UpdatePluginStatus();
    }

    void UpdatePluginStatus()
    {
        if (!Steam.HasMillennium)
        {
            PluginText.Text = "Millennium not found";
            PluginPill.ToolTip = "Descriptions and achievements show in Steam only with Millennium installed.";
        }
        else if (PluginInstaller.InstalledVersion == null)
        {
            PluginText.Text = "Steam plugin: not installed (installs on save)";
            PluginPill.ToolTip = null;
        }
        else if (PluginInstaller.NeedsInstall)
        {
            PluginText.Text = "Steam plugin: update ready (installs on save)";
            PluginPill.ToolTip = null;
        }
        else
        {
            PluginText.Text = $"Steam plugin v{PluginInstaller.InstalledVersion} active";
            PluginPill.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x35, 0x22));
            PluginText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8F, 0xD0, 0x8F));
            return;
        }
        PluginPill.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x2A, 0x1A));
        PluginText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xB0, 0x70));
    }

    void OpenGrid_Click(object sender, RoutedEventArgs e)
    {
        if (_user == null) return;
        var dir = Steam.GridDir(_user.AccountId);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    // ---------- Busy overlay ----------

    void SetBusy(string text) => BusyText.Text = text;

    async Task RunBusy(string text, Func<Task> work)
    {
        BusyText.Text = text;
        Busy.Visibility = Visibility.Visible;
        try { await work(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { Busy.Visibility = Visibility.Collapsed; }
    }
}

public sealed class CountToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Achievement-set headers: the set's name ("" is "Base Game"), parameter "tag" → the DLC tag's visibility,
/// parameter "count" → "12 achievements".
/// </summary>
public sealed class AchSetText : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => (p as string) switch
    {
        "tag" => value is string s && s.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
        "count" => value is int n ? (n == 1 ? "1 achievement" : $"{n} achievements") : "",
        _ => value is string name && name.Length > 0 ? name : "Base Game",
    };
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Set headers in Steam's order: the base game first, then each DLC where it first appears in the list.</summary>
sealed class SetOrder(IList<AchievementVm> achievements) : System.Collections.IComparer
{
    public int Compare(object? x, object? y) => Rank(x).CompareTo(Rank(y));

    int Rank(object? o)
    {
        var name = (o as CollectionViewGroup)?.Name as string ?? "";
        if (name.Length == 0) return -1;
        for (int i = 0; i < achievements.Count; i++) if (achievements[i].Dlc == name) return i;
        return int.MaxValue;
    }
}

public sealed class NullToVisibility : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type t, object? p, CultureInfo c) => (value == null) != Invert ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
