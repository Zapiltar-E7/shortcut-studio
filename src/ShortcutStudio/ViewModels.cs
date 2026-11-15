using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShortcutStudio.Core;

namespace ShortcutStudio;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        OnChanged(name);
        return true;
    }

    protected virtual void OnChanged(string? name) { }
}

public static class Images
{
    public static BitmapImage? Load(byte[]? bytes, int decodeWidth = 0)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0) img.DecodePixelWidth = decodeWidth;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    public static BitmapImage? LoadFile(string? path, int decodeWidth = 0) =>
        path != null && File.Exists(path) ? Load(File.ReadAllBytes(path), decodeWidth) : null;

    public static ImageSource? Gray(ImageSource? src)
    {
        if (src is not BitmapSource b) return null;
        try
        {
            var g = new FormatConvertedBitmap(b, PixelFormats.Gray8, null, 0);
            g.Freeze();
            return g;
        }
        catch { return null; }
    }
}

/// <summary>One art slot for a game: what's on disk, plus an unsaved replacement or removal.</summary>
public sealed class ArtSlotVm : Observable
{
    readonly GameVm _game;
    public ArtSpec Spec { get; }
    public ArtKind Kind => Spec.Kind;

    ArtImage? _pending;
    bool _removed;

    public ArtSlotVm(GameVm game, ArtSpec spec)
    {
        _game = game;
        Spec = spec;
    }

    public bool IsDirty => _pending != null || _removed;

    /// <summary>The bytes to show (pending, or what is saved), or null.</summary>
    public byte[]? CurrentBytes
    {
        get
        {
            if (_pending != null) return _pending.Bytes;
            if (_removed) return null;
            var f = _game.SavedArtFile(Kind);
            return f != null ? File.ReadAllBytes(f) : null;
        }
    }

    ImageSource? _image;
    bool _imageLoaded;
    public ImageSource? Image
    {
        get
        {
            if (!_imageLoaded)
            {
                _image = Images.Load(CurrentBytes, Kind == ArtKind.Hero ? 1600 : 0);
                _imageLoaded = true;
            }
            return _image;
        }
    }

    public bool HasImage => Image != null;

    /// <summary>The image's real size (the preview may be decoded smaller).</summary>
    public string SizeText
    {
        get
        {
            var bytes = CurrentBytes;
            if (bytes == null || Image == null) return "None";
            try
            {
                var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                return $"{frame.PixelWidth}×{frame.PixelHeight}";
            }
            catch { return ""; }
        }
    }

    public void SetImage(byte[] bytes)
    {
        _pending = new ArtImage(bytes, Artwork.ExtensionFor(bytes));
        _removed = false;
        Refresh();
    }

    public void Remove()
    {
        _pending = null;
        _removed = true;
        Refresh();
    }

    public void Revert()
    {
        _pending = null;
        _removed = false;
        Refresh();
    }

    public void Refresh()
    {
        _imageLoaded = false;
        Raise(nameof(Image));
        Raise(nameof(HasImage));
        Raise(nameof(SizeText));
        Raise(nameof(IsDirty));
        _game.ArtChanged(this);
    }

    /// <summary>Writes the change to the grid folder. Returns the file path when an image was written.</summary>
    public string? Apply(uint accountId, uint appId)
    {
        string? path = null;
        if (_pending != null) path = Artwork.Write(accountId, appId, Kind, _pending);
        else if (_removed) Artwork.Delete(accountId, appId, Kind);
        _pending = null;
        _removed = false;
        return path;
    }
}

public sealed class AchievementVm : Observable
{
    readonly GameVm _game;
    public Achievement Model { get; }

    public AchievementVm(GameVm game, Achievement model, long? unlockedAt, bool fromEmulator = false)
    {
        _game = game;
        Model = model;
        _unlockedAt = unlockedAt;
        FromEmulator = fromEmulator && unlockedAt != null;
    }

    /// <summary>The unlock was read from the emulator's own trophy file (not set by hand).</summary>
    public bool FromEmulator { get; private set; }

    public string Id => Model.Id;
    public string Name { get => Model.Name; set { Model.Name = value; Raise(); _game.MarkDirty(); } }
    public string Description { get => Model.Description; set { Model.Description = value; Raise(); _game.MarkDirty(); } }
    public bool Hidden { get => Model.Hidden; set { Model.Hidden = value; Raise(); Raise(nameof(Subtitle)); _game.MarkDirty(); } }

    // ---- Achievement set: base game, or a named DLC (Steam's "DLC: …" sets) ----
    string _lastDlc = "";

    public bool IsDlc
    {
        get => Model.Dlc != "";
        set
        {
            if (value == IsDlc) return;
            if (!value) _lastDlc = Model.Dlc;
            SetDlc(value ? (_lastDlc != "" ? _lastDlc : _game.DlcNames.FirstOrDefault() ?? "DLC") : "");
        }
    }

    /// <summary>The DLC's name. Clearing it moves the achievement back to the base game.</summary>
    public string Dlc { get => Model.Dlc; set => SetDlc(value); }

    public void SetDlc(string? name)
    {
        name = (name ?? "").Trim();
        if (name == Model.Dlc) return;
        Model.Dlc = name;
        Raise(nameof(Dlc)); Raise(nameof(IsDlc)); Raise(nameof(Subtitle));
        _game.RaiseDlcNames();
        _game.MarkDirty();
    }

    // ---- Steam-style progress ("12 / 900") for save-file achievements whose rule counts several steps ----
    SaveRuleSet? _rules;
    int _progressMax;
    bool _showProgress;

    public void AttachRules(SaveRuleSet? rules)
    {
        _rules = rules;
        _progressMax = rules != null && Model.TrophyId is int tid ? rules.MaxProgress(tid) : 0;
        _showProgress = _progressMax > 1 && rules!.ShowsProgress(Model.TrophyId!.Value);
        Raise(nameof(ShowProgress)); Raise(nameof(ProgressVisibility)); Raise(nameof(ProgressLabel));
    }

    /// <summary>The tracked rule counts more than one step, so Steam can show a progress bar for it.</summary>
    public bool CanShowProgress => _progressMax > 1;
    public System.Windows.Visibility ProgressVisibility => CanShowProgress ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public string ProgressLabel => $"Show progress in Steam (0 / {_progressMax:N0})";

    public bool ShowProgress
    {
        get => _showProgress;
        set
        {
            if (!CanShowProgress || _showProgress == value) return;
            _showProgress = value;
            _rules!.SetProgress(Model.TrophyId!.Value, value);
            Raise();
            _game.MarkDirty();
        }
    }

    public string PercentText
    {
        get => Model.GlobalPercent?.ToString("0.#") ?? "";
        set
        {
            Model.GlobalPercent = double.TryParse(value.Trim().TrimEnd('%'), out var p) ? Math.Clamp(p, 0, 100) : null;
            Raise();
            Raise(nameof(Subtitle));
            _game.MarkDirty();
        }
    }

    long? _unlockedAt;
    public bool Unlocked
    {
        get => _unlockedAt != null;
        set
        {
            if (value == Unlocked) return;
            _unlockedAt = value ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : null;
            FromEmulator = false;
            Raise();
            Raise(nameof(Subtitle));
            Raise(nameof(ListIcon));
            _game.MarkDirty();
        }
    }
    public long? UnlockedAt => _unlockedAt;

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (EmulatorTrophies.GradeName(Model.Grade) is { Length: > 0 } grade) parts.Add(grade);
            if (Model.Points is int pts) parts.Add($"{pts} points");
            if (Model.Dlc != "") parts.Add("DLC: " + Model.Dlc);
            if (Hidden) parts.Add("Hidden");
            if (Model.GlobalPercent != null) parts.Add($"{Model.GlobalPercent:0.#}% of players");
            if (_unlockedAt != null) parts.Add("Unlocked " + DateTimeOffset.FromUnixTimeMilliseconds(_unlockedAt.Value).LocalDateTime.ToString("d MMM yyyy") + (FromEmulator ? " in the emulator" : ""));
            return string.Join(" · ", parts);
        }
    }

    // Icons are kept as bytes until Save, then written to the extras folder.
    public byte[]? IconBytes { get; private set; }
    public byte[]? IconGrayBytes { get; private set; }
    public bool IconDirty { get; private set; }

    public void LoadIconsFromDisk(uint appId)
    {
        IconBytes = string.IsNullOrEmpty(Model.Icon) ? null : TryRead(ExtrasStore.IconPath(appId, Model.Icon));
        IconGrayBytes = string.IsNullOrEmpty(Model.IconGray) ? null : TryRead(ExtrasStore.IconPath(appId, Model.IconGray));
    }

    static byte[]? TryRead(string p) => File.Exists(p) ? File.ReadAllBytes(p) : null;

    public void SetIcon(byte[]? bytes, bool gray)
    {
        if (gray) IconGrayBytes = bytes; else IconBytes = bytes;
        IconDirty = true;
        Raise(nameof(Icon));
        Raise(nameof(IconGray));
        Raise(nameof(ListIcon));
        _game.MarkDirty();
    }

    public ImageSource? Icon => Images.Load(IconBytes);
    /// <summary>The locked icon: the custom grey one, or the colour one turned grey.</summary>
    public ImageSource? IconGray => Images.Load(IconGrayBytes) ?? Images.Gray(Icon);
    public ImageSource? ListIcon => Unlocked ? Icon : IconGray;

    public void RaiseAll()
    {
        foreach (var p in new[] { nameof(Name), nameof(Description), nameof(Hidden), nameof(Dlc), nameof(IsDlc), nameof(PercentText), nameof(Unlocked), nameof(Subtitle), nameof(Icon), nameof(IconGray), nameof(ListIcon) })
            Raise(p);
    }
}

/// <summary>One row in a game's launch picker.</summary>
public sealed class LaunchOptionVm : Observable
{
    readonly GameVm _game;
    public LaunchOptionVm(GameVm game, LaunchOption model)
    {
        _game = game;
        _name = model.Name; _exe = model.Exe; _startDir = model.StartDir; _args = model.Args;
    }

    string _name, _exe, _startDir, _args;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Exe { get => _exe; set => Set(ref _exe, value); }
    public string StartDir { get => _startDir; set => Set(ref _startDir, value); }
    public string Args { get => _args; set => Set(ref _args, value); }

    protected override void OnChanged(string? name) => _game.MarkDirty();

    public LaunchOption ToModel() => new() { Name = Name.Trim(), Exe = Exe.Trim().Trim('"'), StartDir = StartDir.Trim().Trim('"'), Args = Args };
}

/// <summary>Everything editable about one non-Steam game.</summary>
public sealed class GameVm : Observable
{
    public Shortcut Shortcut { get; }
    public uint AccountId { get; }
    public bool IsNew { get; set; }
    public bool IsDeleted { get; set; }

    public ObservableCollection<ArtSlotVm> Art { get; } = new();
    public ObservableCollection<AchievementVm> Achievements { get; } = new();
    public ObservableCollection<LaunchOptionVm> LaunchChoices { get; } = new();
    public GameExtras Extras { get; private set; } = new();

    /// <summary>The DLC sets this game's achievements use, in list order (for the DLC name picker).</summary>
    public List<string> DlcNames => Achievements.Select(a => a.Dlc).Where(d => d != "").Distinct(StringComparer.Ordinal).ToList();
    public void RaiseDlcNames() => Raise(nameof(DlcNames));

    bool _dirty;
    public bool IsDirty { get => _dirty; private set => Set(ref _dirty, value); }

    public GameVm(uint accountId, Shortcut shortcut, bool isNew)
    {
        AccountId = accountId;
        Shortcut = shortcut;
        IsNew = isNew;
        foreach (var spec in Artwork.Specs) Art.Add(new ArtSlotVm(this, spec));
        LoadFromDisk();
        IsDirty = isNew;
    }

    void LoadFromDisk()
    {
        _name = Shortcut.Name;
        _exe = Shortcut.ExePath;
        _startDir = Shortcut.StartDir.Trim('"');
        _launchOptions = Shortcut.LaunchOptions;
        _hidden = Shortcut.IsHidden;
        _overlay = Shortcut.AllowOverlay;
        _desktopConfig = Shortcut.AllowDesktopConfig;
        _tags = string.Join(", ", Shortcut.Tags);

        var lp = Artwork.ReadLogoPosition(AccountId, AppId);
        _logoPinned = lp?.Pinned ?? "BottomLeft";
        _logoWidth = lp?.WidthPct ?? 50;
        _logoHeight = lp?.HeightPct ?? 50;
        _logoDirty = false;

        // Steam records playtime for shortcuts itself (localconfig.vdf); last played is in shortcuts.vdf.
        _playMinutes = IsNew ? 0 : SteamPlaytime.GetMinutes(AccountId, AppId);
        _lastPlayed = Shortcut.Node.GetInt("LastPlayTime");
        _playtimeHours = FormatHours(_playMinutes);
        _playtimeDirty = false;

        Extras = ExtrasStore.Load(AppId);
        LaunchChoices.Clear();
        foreach (var o in Extras.LaunchOptions) LaunchChoices.Add(new LaunchOptionVm(this, o));
        // With a launch menu, Steam holds whichever option ran last; show the shortcut's own settings instead.
        if (Extras.LaunchBase is { } b && Extras.LaunchOptions.Count >= 2)
        {
            _exe = b.Exe;
            _startDir = b.StartDir;
            _launchOptions = b.Args;
        }
        var unlocks = ExtrasStore.Unlocks(AppId);
        var emulator = Extras.TrophySource is { } src ? EmulatorTrophies.ReadUnlocks(src.Emulator, src.UnlockFile, src.NpCommId) : new();
        _saveRules = SaveRuleSet.For(Extras.TrophySource);
        Achievements.Clear();
        foreach (var a in Extras.Achievements)
        {
            // The emulator's record wins; a hand-made unlock still counts for trophies it hasn't recorded.
            AchievementVm vm = a.TrophyId is int tid && emulator.TryGetValue(tid, out var et)
                ? new AchievementVm(this, a, et, fromEmulator: true)
                : new AchievementVm(this, a, unlocks.TryGetValue(a.Id, out var t) ? t : null);
            vm.LoadIconsFromDisk(AppId);
            vm.AttachRules(_saveRules);
            Achievements.Add(vm);
        }
        foreach (var s in Art) s.Revert();
    }

    SaveRuleSet? _saveRules;

    public uint AppId => Shortcut.AppId;
    public string AppIdText => AppId.ToString();

    string _name = "", _exe = "", _startDir = "", _launchOptions = "", _tags = "";
    bool _hidden, _overlay, _desktopConfig;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Exe { get => _exe; set => Set(ref _exe, value); }
    public string StartDir { get => _startDir; set => Set(ref _startDir, value); }
    public string LaunchOptions { get => _launchOptions; set => Set(ref _launchOptions, value); }
    public bool Hidden { get => _hidden; set => Set(ref _hidden, value); }
    public bool AllowOverlay { get => _overlay; set => Set(ref _overlay, value); }
    public bool AllowDesktopConfig { get => _desktopConfig; set => Set(ref _desktopConfig, value); }
    public string Tags { get => _tags; set => Set(ref _tags, value); }

    // Store page
    public string ShortDescription { get => Extras.ShortDescription; set { Extras.ShortDescription = value; Raise(); MarkDirty(); } }
    public string Description { get => Extras.Description; set { Extras.Description = value; Raise(); MarkDirty(); } }
    public string Developer { get => Extras.Developer; set { Extras.Developer = value; Raise(); MarkDirty(); } }
    public string Publisher { get => Extras.Publisher; set { Extras.Publisher = value; Raise(); MarkDirty(); } }
    public string ReleaseDate { get => Extras.ReleaseDate; set { Extras.ReleaseDate = value; Raise(); MarkDirty(); } }
    public string Genres
    {
        get => string.Join(", ", Extras.Genres);
        set { Extras.Genres = Split(value); Raise(); MarkDirty(); }
    }
    public string SourceText => Extras.SourceAppId > 0 ? $"Imported from Steam app {Extras.SourceAppId}" : "";

    // Playtime (recorded by the Steam plugin; editable here)
    double _playMinutes;
    long _lastPlayed;
    string _playtimeHours = "0";
    bool _playtimeDirty;

    static string FormatHours(double minutes) => (minutes / 60).ToString(minutes < 600 ? "0.#" : "0");

    public string PlaytimeHours
    {
        get => _playtimeHours;
        set
        {
            var text = value.Trim();
            if (!double.TryParse(text, out var hours) || hours < 0) { Raise(); return; }
            if (Set(ref _playtimeHours, text))
            {
                _playMinutes = hours * 60;
                _playtimeDirty = true;
                Raise(nameof(PlaytimeText));
            }
        }
    }

    public string PlaytimeText => _playMinutes < 1 ? "Not played yet" : _playMinutes < 60 ? $"{Math.Round(_playMinutes)} minutes" : $"{_playMinutes / 60:0.#} hours";
    public string LastPlayedText => _lastPlayed > 0
        ? "Last played " + DateTimeOffset.FromUnixTimeSeconds(_lastPlayed).LocalDateTime.ToString("d MMM yyyy, HH:mm")
        : "Never played through Steam";

    public TrophySource? TrophySource
    {
        get => Extras.TrophySource;
        set { Extras.TrophySource = value; Raise(); Raise(nameof(TrophySourceText)); Raise(nameof(HasTrophySource)); MarkDirty(); }
    }
    public bool HasTrophySource => Extras.TrophySource != null;
    public string TrophySourceText => Extras.TrophySource switch
    {
        { Emulator: "retroachievements" } s => $"Live from RetroAchievements: {s.Title} (game {s.NpCommId}, {s.User}). Achievements you earn unlock in Steam while the game runs.",
        { Emulator: "savefile" } s => $"Live from your save files: {s.Title}. Every save slot in {s.UnlockFile} is checked whenever the game saves; achievements unlock in Steam automatically.",
        { Emulator: "dusklight" } s => $"Live from Dusklight's own achievements ({s.UnlockFile}). Achievements you earn in the game unlock in Steam automatically.",
        { } s => $"Live from {EmulatorTrophies.EmulatorName(s.Emulator)}: {s.Title} ({s.NpCommId}, user {s.User}). Trophies you earn there unlock here and in Steam automatically.",
        _ => "",
    };
    public int SourceAppId { get => Extras.SourceAppId; set { Extras.SourceAppId = value; Raise(nameof(SourceText)); MarkDirty(); } }

    // Logo position
    string _logoPinned = "BottomLeft";
    double _logoWidth = 50, _logoHeight = 50;
    bool _logoDirty;
    public string LogoPinned { get => _logoPinned; set { if (Set(ref _logoPinned, value)) _logoDirty = true; } }
    public double LogoWidth { get => _logoWidth; set { if (Set(ref _logoWidth, Math.Round(value, 1))) _logoDirty = true; } }
    public double LogoHeight { get => _logoHeight; set { if (Set(ref _logoHeight, Math.Round(value, 1))) _logoDirty = true; } }

    public ArtSlotVm Slot(ArtKind k) => Art.First(a => a.Kind == k);
    public ImageSource? Capsule => Slot(ArtKind.Capsule).Image;
    public ImageSource? HeroImage => Slot(ArtKind.Hero).Image;
    public ImageSource? LogoImage => Slot(ArtKind.Logo).Image;
    public ImageSource? IconImage => Slot(ArtKind.Icon).Image ?? (File.Exists(Shortcut.Icon) ? Images.LoadFile(Shortcut.Icon, 64) : null);

    public string? SavedArtFile(ArtKind k) => IsNew ? null : Artwork.FindFile(AccountId, AppId, k);

    public void ArtChanged(ArtSlotVm slot)
    {
        Raise(nameof(Capsule));
        Raise(nameof(HeroImage));
        Raise(nameof(LogoImage));
        Raise(nameof(IconImage));
        if (slot.IsDirty) MarkDirty();
    }

    public string UnlockSummary
    {
        get
        {
            if (Achievements.Count == 0) return "No achievements";
            int n = Achievements.Count(a => a.Unlocked);
            return $"{n} of {Achievements.Count} unlocked ({100 * n / Achievements.Count}%)";
        }
    }

    public void MarkDirty()
    {
        IsDirty = true;
        Raise(nameof(UnlockSummary));
    }

    protected override void OnChanged(string? name)
    {
        if (name != nameof(IsDirty)) IsDirty = true;
    }

    public void Revert()
    {
        if (IsNew) return;
        LoadFromDisk();
        foreach (var p in typeof(GameVm).GetProperties()) Raise(p.Name);
        IsDirty = false;
    }

    /// <summary>True when saving changes shortcuts.vdf or library art, which Steam only picks up after a restart.</summary>
    public bool TouchesSteamFiles =>
        IsNew || Art.Any(a => a.IsDirty) || _logoDirty || _playtimeDirty ||
        Name.Trim() != Shortcut.Name || Shortcut.Quote(Exe.Trim()) != Shortcut.Exe || Shortcut.Quote(StartDir.Trim()) != Shortcut.StartDir ||
        LaunchOptions != Shortcut.LaunchOptions || Hidden != Shortcut.IsHidden || AllowOverlay != Shortcut.AllowOverlay ||
        AllowDesktopConfig != Shortcut.AllowDesktopConfig || !Split(Tags).SequenceEqual(Shortcut.Tags);

    /// <summary>Copies the fields this app edits onto another copy of the same entry (one Steam re-wrote since loading).</summary>
    public void CopyEditsTo(Shortcut target)
    {
        target.Name = Shortcut.Name;
        target.Exe = Shortcut.Exe;
        target.StartDir = Shortcut.StartDir;
        target.Icon = Shortcut.Icon;
        target.LaunchOptions = Shortcut.LaunchOptions;
        target.IsHidden = Shortcut.IsHidden;
        target.AllowOverlay = Shortcut.AllowOverlay;
        target.AllowDesktopConfig = Shortcut.AllowDesktopConfig;
        target.Tags = Shortcut.Tags;
    }

    /// <summary>Copies edits into the VDF entry (call before the shortcuts file is saved).</summary>
    public void ApplyToShortcut()
    {
        Shortcut.Name = Name.Trim();
        Shortcut.Exe = Shortcut.Quote(Exe.Trim());
        Shortcut.StartDir = Shortcut.Quote(StartDir.Trim());
        Shortcut.LaunchOptions = LaunchOptions;
        Shortcut.IsHidden = Hidden;
        Shortcut.AllowOverlay = AllowOverlay;
        Shortcut.AllowDesktopConfig = AllowDesktopConfig;
        Shortcut.Tags = Split(Tags);
    }

    /// <summary>Writes art, logo position, extras and unlocks. Call after the shortcut entry is final.</summary>
    /// <summary>What the last <see cref="ApplyFiles"/> changed in Steam's grid folder: (slot, written path or null if removed).</summary>
    public List<(ArtKind Kind, string? Path)> LastArtChanges { get; } = new();
    public bool LastLogoChanged { get; private set; }

    /// <summary>Edits Steam can't take live (it has no API for them), so saving them needs a Steam restart.</summary>
    public bool NeedsRestart =>
        _playtimeDirty || AllowOverlay != Shortcut.AllowOverlay || AllowDesktopConfig != Shortcut.AllowDesktopConfig ||
        !Split(Tags).SequenceEqual(Shortcut.Tags);

    public void ApplyFiles()
    {
        LastArtChanges.Clear();
        foreach (var slot in Art)
        {
            bool dirty = slot.IsDirty;
            var path = slot.Apply(AccountId, AppId);
            if (dirty) LastArtChanges.Add((slot.Kind, path));
            // Steam reads a shortcut's icon from the path in shortcuts.vdf, so keep that pointing at our file.
            if (slot.Kind == ArtKind.Icon && path != null) Shortcut.Icon = path;
        }
        LastLogoChanged = false;
        if (_logoDirty || IsNew)
        {
            if (Slot(ArtKind.Logo).HasImage)
            {
                Artwork.WriteLogoPosition(AccountId, AppId, new LogoPosition { Pinned = LogoPinned, WidthPct = LogoWidth, HeightPct = LogoHeight });
                LastLogoChanged = true;
            }
            _logoDirty = false;
        }

        if (_playtimeDirty)
        {
            // Steam is closed here (playtime counts as a Steam file change), so its own record can be edited.
            SteamPlaytime.SetMinutes(AccountId, AppId, (int)Math.Round(_playMinutes));
            _playtimeDirty = false;
        }

        // Achievement icons first, so the saved extras only reference files that exist.
        foreach (var a in Achievements)
        {
            if (!a.IconDirty && !IsNew) continue;
            a.Model.Icon = a.IconBytes != null ? ExtrasStore.SaveIcon(AppId, a.IconBytes, a.Id) : "";
            a.Model.IconGray = a.IconGrayBytes != null ? ExtrasStore.SaveIcon(AppId, a.IconGrayBytes, a.Id + "_gray") : "";
        }
        Extras.Achievements = Achievements.Select(a => a.Model).ToList();
        Extras.LaunchOptions = LaunchChoices.Select(o => o.ToModel()).ToList();
        Extras.LaunchBase = LaunchChoices.Count >= 2
            ? new LaunchOption { Exe = Exe.Trim().Trim('"'), StartDir = StartDir.Trim().Trim('"'), Args = LaunchOptions }
            : null;
        ExtrasStore.Save(AppId, Extras);
        // Progress switches live in the save-file rules the Steam plugin reads.
        _saveRules?.SaveIfChanged();
        // Only hand-made unlocks are ours to store; emulator unlocks are re-read from its file every time.
        ExtrasStore.SaveUnlocks(AppId, Achievements.Where(a => a.UnlockedAt != null && !a.FromEmulator).ToDictionary(a => a.Id, a => a.UnlockedAt!.Value));

        IsNew = false;
        IsDirty = false;
        foreach (var slot in Art) slot.Refresh();
        IsDirty = false;
    }

    public string RememberedLaunchText
    {
        get
        {
            if (LaunchChoices.Count < 2) return "Add two or more options and Steam will ask which one to start when you press Play.";
            var i = IsNew ? null : ExtrasStore.LaunchChoice(AppId);
            return i is int n && n >= 0 && n < LaunchChoices.Count
                ? $"Steam always starts \"{LaunchChoices[n].Name}\" (\"Always use this option\" was ticked)."
                : "Steam asks which option to start each time you press Play.";
        }
    }
    public bool HasRememberedLaunch => !IsNew && LaunchChoices.Count >= 2 && ExtrasStore.LaunchChoice(AppId) != null;

    public void LaunchChoicesChanged()
    {
        MarkDirty();
        Raise(nameof(RememberedLaunchText));
        Raise(nameof(HasRememberedLaunch));
    }

    public void ForgetLaunchChoice()
    {
        ExtrasStore.ClearLaunchChoice(AppId);
        Raise(nameof(RememberedLaunchText));
        Raise(nameof(HasRememberedLaunch));
    }

    /// <summary>The appid changed (Steam assigned one to a new shortcut).</summary>
    public void RaiseIdentity()
    {
        Raise(nameof(AppId));
        Raise(nameof(AppIdText));
    }

    public void RaiseExtras()
    {
        foreach (var p in new[] { nameof(ShortDescription), nameof(Description), nameof(Developer), nameof(Publisher), nameof(ReleaseDate), nameof(Genres), nameof(SourceText), nameof(UnlockSummary), nameof(TrophySourceText), nameof(HasTrophySource) })
            Raise(p);
    }

    static List<string> Split(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
