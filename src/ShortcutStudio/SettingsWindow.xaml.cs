using System.Diagnostics;
using System.IO;
using System.Windows;
using ShortcutStudio.Core;

namespace ShortcutStudio;

public partial class SettingsWindow : Window
{
    readonly AppSettings _settings;
    public bool InstallPluginRequested { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        KeyBox.Text = settings.SteamGridDbKey;
        RaUserBox.Text = settings.RaUser;
        RaKeyBox.Text = settings.RaKey;

        if (!Steam.HasMillennium)
        {
            PluginState.Text = "Millennium isn't installed in Steam, so the plugin can't be added. Get it from steambrew.app.";
            InstallBtn.IsEnabled = false;
        }
        else
        {
            var installed = PluginInstaller.InstalledVersion;
            PluginState.Text = installed == null ? "Not installed."
                : $"Installed: v{installed}" + (PluginInstaller.IsEnabled ? ", enabled." : ", but switched off in Millennium.")
                  + (PluginInstaller.BundledVersion != installed ? $" This app has v{PluginInstaller.BundledVersion}." : "");
        }
    }

    void KeyLink_Click(object sender, RoutedEventArgs e) => Steam.OpenUrl("https://www.steamgriddb.com/profile/preferences/api");
    void RaLink_Click(object sender, RoutedEventArgs e) => Steam.OpenUrl("https://retroachievements.org/settings");

    void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ExtrasStore.Root);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ExtrasStore.Root}\"") { UseShellExecute = true });
    }

    void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallPluginRequested = true;
        Save_Click(sender, e);
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.SteamGridDbKey = KeyBox.Text.Trim();
        _settings.RaUser = RaUserBox.Text.Trim();
        _settings.RaKey = RaKeyBox.Text.Trim();
        _settings.Save();
        DialogResult = true;
    }
}
