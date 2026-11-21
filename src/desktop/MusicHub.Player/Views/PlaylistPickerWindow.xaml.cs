using System.Windows;
using System.Windows.Input;
using MusicHub.Player.Models;

namespace MusicHub.Player.Views;

public partial class PlaylistPickerWindow : Window
{
    public PlaylistPickerWindow(IReadOnlyList<PlaylistItem> playlists)
    {
        InitializeComponent();
        PlaylistList.ItemsSource = playlists;
        if (playlists.Count > 0) PlaylistList.SelectedIndex = 0;
        Loaded += (_, _) => PlaylistList.Focus();
    }

    public PlaylistItem? SelectedPlaylist { get; private set; }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SelectedPlaylist = PlaylistList.SelectedItem as PlaylistItem;
        if (SelectedPlaylist is null) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
