using System.Windows;
using System.Windows.Input;

namespace MusicHub.Player.Views;

public partial class PlaylistEditorWindow : Window
{
    public PlaylistEditorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    public string PlaylistName => NameBox.Text.Trim();
    public string? Description => string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
    public bool IsPublic => PublicBox.IsChecked == true;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "Playlist name is required.";
            ErrorBanner.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
