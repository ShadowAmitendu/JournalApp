using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace JournalApp;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public static MainWindow Instance { get; private set; }
    public Grid AppTitleBarControl => AppTitleBar;
    private bool _isCentered = false;

    public MainWindow()
    {
        Instance = this;
        try
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && AppWindow.TitleBar != null)
            {
                var titleBar = AppWindow.TitleBar;
                titleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(20, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
            }

            AppWindow.SetIcon("Assets/AppIcon.ico");
        }
        catch (Exception ex)
        {
            try
            {
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JournalApp", "crash.txt");
                System.IO.File.WriteAllText(path, ex.ToString());
            }
            catch {}
            System.Diagnostics.Debug.WriteLine($"Failed to customize title bar or icon: {ex.Message}");
        }

        this.Activated += MainWindow_Activated;

        // Size and center the window before activation to avoid visual jumps
        CenterWindow();

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (!_isCentered)
        {
            _isCentered = true;
            CenterWindow();
        }
    }

    private void CenterWindow()
    {
        try
        {
            var appWindow = AppWindow;
            if (appWindow != null)
            {
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
                if (displayArea != null)
                {
                    var workArea = displayArea.WorkArea;
                    int width = 1480;
                    int height = 800;
                    appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

                    int x = workArea.X + (workArea.Width - width) / 2;
                    int y = workArea.Y + (workArea.Height - height) / 2;
                    appWindow.Move(new Windows.Graphics.PointInt32(x, y));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during centering: {ex.Message}");
        }
    }

    public string SearchText => TitleSearchBox?.Text ?? "";

    private void TitleSearchBox_TextChanged(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, Microsoft.UI.Xaml.Controls.AutoSuggestBoxTextChangedEventArgs args)
    {
        if (MainPage.Instance != null)
        {
            MainPage.Instance.OnTitleSearchTextChanged();
        }
    }

    public void ApplyAppFont(string fontName)
    {
        try
        {
            var fontFamily = new Microsoft.UI.Xaml.Media.FontFamily(fontName);
            if (RootFrame != null) RootFrame.FontFamily = fontFamily;
            if (TitleSearchBox != null) TitleSearchBox.FontFamily = fontFamily;
            if (AppTitleTextBlock != null) AppTitleTextBlock.FontFamily = fontFamily;
        }
        catch {}
    }

    private string _updateDownloadUrl;
    private string _updateAssetName;

    public void ShowUpdateAvailable(string downloadUrl, string assetName, string version)
    {
        _updateDownloadUrl = downloadUrl;
        _updateAssetName = assetName;
        
        if (TitleBarUpdateBtn != null)
        {
            TitleBarUpdateBtn.Visibility = Visibility.Visible;
            if (TitleBarUpdateText != null)
            {
                TitleBarUpdateText.Text = $"Update to {version}";
            }
        }
    }

    public void ShowTitleBarDownloadState(bool downloading)
    {
        if (downloading)
        {
            if (TitleBarUpdateBtn != null) TitleBarUpdateBtn.IsEnabled = false;
            if (TitleBarUpdateProgress != null) TitleBarUpdateProgress.Visibility = Visibility.Visible;
            if (TitleBarUpdateText != null) TitleBarUpdateText.Text = "Downloading...";
        }
        else
        {
            if (TitleBarUpdateBtn != null) TitleBarUpdateBtn.IsEnabled = true;
            if (TitleBarUpdateProgress != null) TitleBarUpdateProgress.Visibility = Visibility.Collapsed;
        }
    }

    public void UpdateTitleBarDownloadProgress(int percentage)
    {
        if (TitleBarUpdateProgress != null)
        {
            TitleBarUpdateProgress.Value = percentage;
        }
        if (TitleBarUpdateText != null)
        {
            TitleBarUpdateText.Text = $"Downloading: {percentage}%";
        }
    }

    public void UpdateTitleBarDownloadText(string text)
    {
        if (TitleBarUpdateText != null)
        {
            TitleBarUpdateText.Text = text;
        }
    }

    private async void TitleBarUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_updateDownloadUrl)) return;
        
        if (MainPage.Instance != null)
        {
            await MainPage.Instance.StartUpdateDownloadFromTitleBar(_updateDownloadUrl, _updateAssetName);
        }
    }
}
