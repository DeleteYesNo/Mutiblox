using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mutiblox.Models;

namespace Mutiblox.Views.Dialogs;

public partial class ServerBrowserDialog : Window
{
    private readonly List<ServerInfo> _allServers;

    /// <summary>
    /// The server selected by the user, or null if cancelled.
    /// </summary>
    public ServerInfo? SelectedServer { get; private set; }

    public ObservableCollection<ServerInfo> FilteredServers { get; } = [];

    public ServerBrowserDialog(List<ServerInfo> servers)
    {
        InitializeComponent();
        _allServers = servers;
        ServerList.ItemsSource = FilteredServers;
        ApplyFilter(string.Empty);
    }

    private void ApplyFilter(string filter)
    {
        FilteredServers.Clear();

        var filtered = string.IsNullOrWhiteSpace(filter)
            ? _allServers
            : _allServers.Where(s => s.JobId.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var server in filtered)
            FilteredServers.Add(server);

        CountText.Text = $"{FilteredServers.Count} 個伺服器";
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            ApplyFilter(tb.Text);
    }

    private void ServerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ServerList.SelectedItem is ServerInfo server)
        {
            SelectedServer = server;
            DialogResult = true;
            Close();
        }
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (ServerList.SelectedItem is ServerInfo server)
        {
            SelectedServer = server;
            DialogResult = true;
            Close();
        }
        else
        {
            StatusText.Text = "請先選擇一個伺服器";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        // Raise event so parent can reload
        RefreshRequested?.Invoke();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Raised when the user clicks refresh. Parent should reload servers and call UpdateServers.
    /// </summary>
    public event Action? RefreshRequested;

    public void UpdateServers(List<ServerInfo> servers)
    {
        _allServers.Clear();
        _allServers.AddRange(servers);
        ApplyFilter(FilterBox.Text);
    }
}
