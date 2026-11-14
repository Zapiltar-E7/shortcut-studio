using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ShortcutStudio.Core;

namespace ShortcutStudio;

public sealed class TrophySetRow
{
    public TrophySet Set { get; }
    public TrophySetRow(TrophySet set) => Set = set;
    public string Title => Set.Title;
    public string Line1
    {
        get
        {
            var grades = Set.Trophies.GroupBy(t => t.Grade).ToDictionary(g => g.Key, g => g.Count());
            string G(string k, string n) => grades.TryGetValue(k, out var c) ? $"{c} {n}" : "";
            var mix = string.Join(", ", new[] { G("P", "Platinum"), G("G", "Gold"), G("S", "Silver"), G("B", "Bronze") }.Where(s => s != ""));
            return $"{EmulatorTrophies.EmulatorName(Set.Emulator)} · user {Set.User} · {Set.UnlockedCount} of {Set.Trophies.Count} unlocked · {mix}";
        }
    }
    public string Line2 => $"{Set.NpCommId} · {Set.Origin}";
    public ImageSource? Icon => Images.Load(Set.Trophies.FirstOrDefault(t => t.Icon != null)?.Icon, 112);
}

public partial class TrophyWindow : Window
{
    public TrophySet? Selected { get; private set; }

    public TrophyWindow(string exe, string startDir, string launchOptions, string gameName)
    {
        InitializeComponent();
        Searched.Text = "Searched the game's program, start folder and launch options.";
        Show(EmulatorTrophies.FindForShortcut(exe, startDir, launchOptions), gameName);
    }

    void Show(List<TrophySet> sets, string gameName)
    {
        var rows = sets
            // The set whose title matches the game, and the emulator user with progress, first.
            .OrderByDescending(s => Similar(s.Title, gameName))
            .ThenByDescending(s => s.UnlockedCount)
            .ThenByDescending(s => File.Exists(s.UnlockFile))
            .Select(s => new TrophySetRow(s)).ToList();
        Sets.ItemsSource = rows;
        Message.Text = rows.Count == 0
            ? "No trophies found for this shortcut.\n\nPoint it at your RPCS3 folder (the one with dev_hdd0) or your shadPS4 folder (the one with user), or the game's own folder."
            : "";
        if (rows.Count > 0) Sets.SelectedIndex = 0;
    }

    static bool Similar(string a, string b)
    {
        static string N(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var x = N(a); var y = N(b);
        return x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x));
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Emulator or game folder" };
        if (dlg.ShowDialog(this) != true) return;
        Searched.Text = "Searched " + dlg.FolderName;
        Show(EmulatorTrophies.FindInFolder(dlg.FolderName), "");
    }

    void Sets_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        Selected = (Sets.SelectedItem as TrophySetRow)?.Set;
        ImportBtn.IsEnabled = Selected != null;
    }

    void Sets_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected != null) Import_Click(sender, e);
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null) DialogResult = true;
    }
}
