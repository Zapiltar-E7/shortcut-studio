using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShortcutStudio.Core;

namespace ShortcutStudio;

public sealed record ImportOptions(bool Art, bool Details, bool Achievements, bool Name);

public partial class ImportWindow : Window
{
    public StoreSearchResult? Selected { get; private set; }
    public ImportOptions Options => new(ChkArt.IsChecked == true, ChkDetails.IsChecked == true, ChkAch.IsChecked == true, ChkName.IsChecked == true);

    CancellationTokenSource? _cts;

    public ImportWindow(string name, int sourceAppId)
    {
        InitializeComponent();
        Query.Text = sourceAppId > 0 ? sourceAppId.ToString() : name;
        Loaded += (_, _) => { Query.Focus(); Query.SelectAll(); _ = SearchAsync(); };
    }

    void Search_Click(object sender, RoutedEventArgs e) => _ = SearchAsync();

    async Task SearchAsync()
    {
        var term = Query.Text.Trim();
        if (term.Length == 0) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Message.Text = "Searching…";
        Results.ItemsSource = null;
        try
        {
            var list = await SteamStore.SearchAsync(term, ct);
            if (ct.IsCancellationRequested) return;
            Results.ItemsSource = list;
            Message.Text = list.Count == 0 ? "No Steam games found. Try a shorter name or the appid from the store page URL." : "";
            if (list.Count > 0) Results.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Message.Text = "Search failed: " + ex.Message; }
    }

    void Results_Changed(object sender, SelectionChangedEventArgs e)
    {
        Selected = Results.SelectedItem as StoreSearchResult;
        ImportBtn.IsEnabled = Selected != null;
    }

    void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected != null) Import_Click(sender, e);
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        DialogResult = true;
    }
}
