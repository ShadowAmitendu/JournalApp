using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace JournalApp;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        
        UnhandledException += (sender, e) =>
        {
            LogCrash("UnhandledException", e.Exception, e.Message);
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            LogCrash("AppDomainUnhandledException", e.ExceptionObject as Exception, e.ExceptionObject?.ToString());
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            LogCrash("UnobservedTaskException", e.Exception, e.Exception?.Message);
        };
    }

    private static void LogCrash(string source, Exception? ex, string? extraMessage)
    {
        try
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JournalApp");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "crash.txt");
            string log = $"[{DateTime.Now}] Crash Source: {source}\r\nExtra Message: {extraMessage}\r\nException Details:\r\n{ex?.ToString()}\r\n\r\n";
            System.IO.File.AppendAllText(path, log);
        }
        catch {}
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            try
            {
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JournalApp", "crash.txt");
                System.IO.File.WriteAllText(path, ex.ToString());
            }
            catch {}
            throw;
        }
    }
}
