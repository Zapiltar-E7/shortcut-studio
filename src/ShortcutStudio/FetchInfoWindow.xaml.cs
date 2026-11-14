using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShortcutStudio.Core;

namespace ShortcutStudio;

public sealed record InfoResult(string Source, string Title, string Detail, int SteamAppId, string WikiTitle)
{
    public Brush BadgeBrush => Source == "STEAM" ? new SolidColorBrush(Color.FromRgb(0x1A, 0x9F, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x5A, 0x63, 0x6E));
}

public partial class FetchInfoWindow : Window
{
    public InfoResult? Selected { get; private set; }

    public FetchInfoWindow(string name)
    {
        InitializeComponent();
        Query.Text = name;
        Loaded += (_, _) => { Query.Focus(); Query.SelectAll(); _ = SearchAsync(); };
    }

    void Search_Click(object sender, RoutedEventArgs e) => _ = SearchAsync();

    async Task SearchAsync()
    {
        var term = Query.Text.Trim();
        if (term == "") return;
        Message.Text = "Searching Steam and Wikipedia…";
        Results.ItemsSource = null;
        var results = new List<InfoResult>();
        var steam = Task.Run(async () => { try { return await SteamStore.SearchAsync(term); } catch { return new List<StoreSearchResult>(); } });
        var wiki = Task.Run(async () => { try { return await Wikipedia.SearchAsync(term); } catch { return new List<WikiResult>(); } });
        foreach (var s in (await steam).Take(5)) results.Add(new InfoResult("STEAM", s.Name, $"Steam store page · app {s.AppId}", s.AppId, ""));
        foreach (var w in (await wiki).Take(6)) results.Add(new InfoResult("WIKIPEDIA", w.Title, w.Snippet, 0, w.Title));
        Results.ItemsSource = results;
        Message.Text = results.Count == 0 ? "Nothing found. Try a shorter or different name." : "";
        if (results.Count > 0) Results.SelectedIndex = 0;
    }

    void Results_Changed(object sender, SelectionChangedEventArgs e)
    {
        Selected = Results.SelectedItem as InfoResult;
        UseBtn.IsEnabled = Selected != null;
    }

    void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected != null) DialogResult = true;
    }

    void Use_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null) DialogResult = true;
    }
}
