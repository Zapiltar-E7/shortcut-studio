using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShortcutStudio.Core;

namespace ShortcutStudio;

public sealed class ThumbVm : Observable
{
    public SgdbImage Image { get; }
    public double W { get; }
    public double H { get; }
    bool _picked;
    public bool Picked { get => _picked; set { if (Set(ref _picked, value)) Raise(nameof(Border)); } }
    public Brush Border => Picked ? (Brush)Application.Current.Resources["Accent"] : Brushes.Transparent;

    public ThumbVm(SgdbImage image, ArtKind kind)
    {
        Image = image;
        (W, H) = kind switch
        {
            ArtKind.Capsule => (150.0, 225.0),
            ArtKind.Wide => (230.0, 107.0),
            ArtKind.Hero => (310.0, 100.0),
            ArtKind.Logo => (230.0, 110.0),
            _ => (96.0, 96.0),
        };
    }
}

public partial class SgdbWindow : Window
{
    readonly string _key;
    readonly int _steamAppId;
    readonly Dictionary<ArtKind, SgdbImage> _picked = new();
    /// <summary>The downloaded full-size images the user chose, per slot.</summary>
    public List<(ArtKind Kind, byte[] Bytes)> Chosen { get; } = new();
    CancellationTokenSource? _cts;
    bool _ready;

    ArtKind Kind => Enum.Parse<ArtKind>((string)((TabItem)KindTabs.SelectedItem).Tag);
    SgdbGame? Game => GameBox.SelectedItem as SgdbGame;

    public SgdbWindow(string apiKey, string name, int steamAppId, ArtKind start)
    {
        InitializeComponent();
        _key = apiKey;
        _steamAppId = steamAppId;
        Query.Text = name;
        KindTabs.SelectedIndex = (int)start;
        _ready = true;
        Loaded += async (_, _) => await FindGamesAsync(true);
    }

    async void Search_Click(object sender, RoutedEventArgs e) => await FindGamesAsync(false);

    async Task FindGamesAsync(bool first)
    {
        Message.Text = "Searching SteamGridDB…";
        Thumbs.ItemsSource = null;
        try
        {
            var games = new List<SgdbGame>();
            // A shortcut imported from Steam maps straight to the right SteamGridDB game.
            if (first && _steamAppId > 0)
            {
                var g = await SteamGridDb.GameForSteamAppAsync(_key, _steamAppId);
                if (g != null) games.Add(g);
            }
            foreach (var g in await SteamGridDb.SearchAsync(_key, Query.Text.Trim()))
                if (games.All(x => x.Id != g.Id)) games.Add(g);
            GameBox.ItemsSource = games;
            if (games.Count > 0) GameBox.SelectedIndex = 0;
            else Message.Text = "No games found on SteamGridDB. Try another name.";
        }
        catch (Exception ex) { Message.Text = ex.Message; }
    }

    async void Game_Changed(object sender, SelectionChangedEventArgs e) => await LoadImagesAsync();
    async void Kind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == KindTabs && _ready) await LoadImagesAsync();
    }

    async Task LoadImagesAsync()
    {
        if (Game is not { } game) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var kind = Kind;
        Message.Text = "Loading…";
        Thumbs.ItemsSource = null;
        try
        {
            var images = await SteamGridDb.ImagesAsync(_key, game.Id, kind, ct);
            if (ct.IsCancellationRequested) return;
            var vms = images.Select(i => new ThumbVm(i, kind) { Picked = _picked.TryGetValue(kind, out var p) && p.Id == i.Id }).ToList();
            Thumbs.ItemsSource = vms;
            Message.Text = vms.Count == 0 ? "SteamGridDB has no images of this type for this game." : "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Message.Text = ex.Message; }
    }

    void Thumb_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ThumbVm vm) return;
        foreach (var t in (IEnumerable<ThumbVm>)Thumbs.ItemsSource) t.Picked = t == vm;
        _picked[Kind] = vm.Image;
        PickedText.Text = "Picked: " + string.Join(", ", _picked.Keys.Select(k => Artwork.Spec(k).Label.ToLower())) + ". Press Done to use them.";
    }

    async void Done_Click(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        foreach (var (kind, img) in _picked)
        {
            PickedText.Text = $"Downloading {Artwork.Spec(kind).Label.ToLower()}…";
            var bytes = await SteamStore.DownloadFirstAsync(new[] { img.Url });
            if (bytes != null) Chosen.Add((kind, bytes));
        }
        Close();
    }
}
