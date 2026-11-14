using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShortcutStudio.Core;

namespace ShortcutStudio;

public partial class RaWindow : Window
{
    readonly string _key;
    readonly string? _guess;
    List<RaGameEntry> _games = new();
    public RaGameEntry? Selected { get; private set; }

    public RaWindow(string key, string gameName, string? consoleGuess)
    {
        InitializeComponent();
        _key = key;
        _guess = consoleGuess;
        Query.Text = CleanName(gameName);
        Loaded += async (_, _) => await LoadConsolesAsync();
    }

    /// <summary>Drops region tags and emulator suffixes ("Bloodborne™ [shadPS4]", "Game (USA)") for matching.</summary>
    static string CleanName(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"[\[(].*?[\])]|[™®]", "").Trim();

    async Task LoadConsolesAsync()
    {
        Message.Text = "Loading systems…";
        try
        {
            var consoles = await RetroAchievements.ConsolesAsync(_key);
            ConsoleBox.ItemsSource = consoles;
            ConsoleBox.SelectedItem = consoles.FirstOrDefault(c => c.Name == _guess) ?? consoles.FirstOrDefault(c => c.Name == "PlayStation 2") ?? consoles.FirstOrDefault();
            Message.Text = "";
        }
        catch (Exception ex) { Message.Text = ex.Message; }
    }

    async void Console_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ConsoleBox.SelectedItem is not RaConsole c) return;
        Results.ItemsSource = null;
        Message.Text = $"Loading {c.Name} games…";
        try
        {
            _games = await RetroAchievements.GamesAsync(_key, c.Id);
            Filter();
        }
        catch (Exception ex) { Message.Text = ex.Message; }
    }

    void Query_Changed(object sender, TextChangedEventArgs e) => Filter();

    void Filter()
    {
        var words = Query.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var list = _games
            .Where(g => words.All(w => g.Title.ToLowerInvariant().Contains(w)))
            // Exact-ish titles first, then shorter ones (the base game before "~Hack~" or subsets).
            .OrderByDescending(g => g.Title.Equals(Query.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            .ThenBy(g => g.Title.StartsWith('~') ? 1 : 0)
            .ThenBy(g => g.Title.Length)
            .Take(200).ToList();
        Results.ItemsSource = list;
        Message.Text = _games.Count == 0 ? "" : list.Count == 0 ? "No games with that name on this system. Try fewer words or another system." : "";
        if (list.Count > 0) Results.SelectedIndex = 0;
    }

    void Results_Changed(object sender, SelectionChangedEventArgs e)
    {
        Selected = Results.SelectedItem as RaGameEntry;
        ImportBtn.IsEnabled = Selected != null;
    }

    void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected != null) DialogResult = true;
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null) DialogResult = true;
    }
}
