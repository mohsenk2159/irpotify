using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using MusicHub.Player.Models;
using MusicHub.Player.Services;

namespace MusicHub.Player.Views;

public partial class LoginWindow : Window
{
    private readonly PlayerApiClient _api;
    private readonly SavedSession? _prefill;

    public LoginWindow(PlayerApiClient api, SavedSession? prefill)
    {
        InitializeComponent();
        _api = api;
        _prefill = prefill;

        ServerBox.Text = string.IsNullOrWhiteSpace(prefill?.ServerUrl) ? "http://127.0.0.1:5080" : prefill!.ServerUrl;
        EmailBox.Text = prefill?.Email ?? prefill?.User?.Email ?? string.Empty;

        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(EmailBox.Text)) EmailBox.Focus();
            else PasswordBox.Focus();
        };
    }

    public event EventHandler? SignedIn;

    private void BrandPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        var server = ServerBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(server))
        {
            ShowError("Enter the server address, for example http://192.168.1.20:5080.");
            return;
        }
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("Email and password are required.");
            return;
        }

        SetBusy(true);
        try
        {
            _api.Configure(server);
            var result = await _api.LoginAsync(email, password, $"MusicHub Player on {Environment.MachineName}");
            if (!result.User.CanUsePlayer)
            {
                _api.ClearAuth();
                ShowError("This account is not permitted to use the player application. Ask an administrator to enable player access.");
                return;
            }

            var session = _api.BuildSession(email);
            if (RememberBox.IsChecked == true) SessionStore.Save(session);
            else SessionStore.Clear();

            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (HttpRequestException)
        {
            ShowError($"Cannot reach the server at {_api.BaseUrl}. Check the address, the port, and that MusicHub is running.");
        }
        catch (TaskCanceledException)
        {
            ShowError("The server did not respond in time. Check the address and your network connection.");
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"Sign in failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        SignInButton.IsEnabled = !busy;
        SignInButton.Content = busy ? "Signing in…" : "Sign in";
        BusyText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ServerBox.IsEnabled = !busy;
        EmailBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
    }
}
