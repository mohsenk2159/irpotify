using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MusicHub.Player.Models;
using MusicHub.Player.Services;
using MusicHub.Player.ViewModels;

namespace MusicHub.Player.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(PlayerApiClient api)
    {
        InitializeComponent();
        _viewModel = new MainViewModel(api);
        DataContext = _viewModel;
        NavList.SelectedIndex = 0;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) =>
        {
            api.SessionExpired -= OnApiSessionExpired;
            _viewModel.Dispose();
        };
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        _viewModel.SignedOut += () => SignedOut?.Invoke(this, EventArgs.Empty);

        // The server revokes access when an account expires or a quota is used up. When the
        // refresh attempt fails, return to the sign-in window instead of leaving a dead session.
        api.SessionExpired += OnApiSessionExpired;
    }

    private bool _handlingSessionExpiry;

    private void OnApiSessionExpired()
    {
        if (_handlingSessionExpiry) return;
        _handlingSessionExpiry = true;
        Dispatcher.BeginInvoke(() => _ = _viewModel.SignOutAsync());
    }

    public event EventHandler? SignedOut;

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null) return;
        if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
        {
            _viewModel.NavigateCommand.Execute(tag);
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _viewModel.NavigateCommand.Execute("Search");
        _viewModel.SearchCommand.Execute(null);
    }

    private void TrackList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TrackList_Selected(sender) is { } track) _viewModel.PlayTrackCommand.Execute(track);
    }

    private void DetailList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TrackList_Selected(sender) is { } track) _viewModel.PlayTrackCommand.Execute(track);
    }

    private static TrackItem? TrackList_Selected(object sender)
        => (sender as ListBox)?.SelectedItem as TrackItem;

    private void SeekSlider_DragStarted(object sender, DragStartedEventArgs e) => _viewModel.BeginSeek();

    private void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e) => _viewModel.EndSeek(SeekSlider.Value);

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Clicking the track (move-to-point) does not raise thumb drag events.
        if (!SeekSlider.IsMouseCaptured) _viewModel.EndSeek(SeekSlider.Value);
    }

    private async void NewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var editor = new PlaylistEditorWindow { Owner = this };
        if (editor.ShowDialog() != true) return;
        await _viewModel.CreatePlaylistAsync(editor.PlaylistName, editor.Description, editor.IsPublic);
    }

    private async void DeletePlaylist_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "Delete this playlist? Tracks stay in the library.", "Delete playlist",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        await _viewModel.DeleteOpenPlaylistAsync();
    }

    private async void AddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasCurrentTrack)
        {
            MessageBox.Show(this, "Start playing a track first.", "Add to playlist", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var playlists = await _viewModel.GetPlaylistsForPickerAsync();
        if (playlists.Count == 0)
        {
            MessageBox.Show(this, "You have no playlists yet. Create one from the Playlists page.", "Add to playlist",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var picker = new PlaylistPickerWindow(playlists) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedPlaylist is null) return;
        await _viewModel.AddCurrentTrackToPlaylistAsync(picker.SelectedPlaylist.Id);
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_viewModel.Api.BaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open dashboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "Sign out of MusicHub Player on this device?", "Sign out",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) _viewModel.SignOutCommand.Execute(null);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var typing = Keyboard.FocusedElement is TextBox or PasswordBox;
        if (e.Key == Key.Space && !typing)
        {
            _viewModel.PlayPauseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.NextCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.PreviousCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.ToggleQueueCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
