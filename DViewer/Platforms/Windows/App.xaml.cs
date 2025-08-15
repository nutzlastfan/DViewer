using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DViewer.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        public App() { this.InitializeComponent(); }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Single-Instance registrieren
            var keyInstance = AppInstance.FindOrRegisterForKey("main");
            var current = AppInstance.GetCurrent();

            if (!keyInstance.IsCurrent)
            {
                // Start an bestehende Instanz umleiten
                var actArgs = current.GetActivatedEventArgs();
                keyInstance.RedirectActivationToAsync(actArgs).AsTask().Wait();
                Environment.Exit(0);
                return;
            }

            // Aktivierungen (auch spätere „Öffnen mit…“) hier empfangen
            current.Activated += OnAppActivated;

            // Falls Start mit Kommandozeilenpfad (Fallback)
            var cli = Environment.GetCommandLineArgs();
            var firstFile = cli.Skip(1).FirstOrDefault(p => p.EndsWith(".dcm", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(firstFile))
                DViewer.App.EnqueueOrHandle(firstFile);

            base.OnLaunched(args);
        }

        private void OnAppActivated(object? sender, AppActivationArguments e)
        {
            if (e.Kind == ExtendedActivationKind.File && e.Data is IFileActivatedEventArgs fa)
            {
                var file = fa.Files?.OfType<StorageFile>().FirstOrDefault();
                if (file != null)
                    DViewer.App.EnqueueOrHandle(file.Path);
            }
            else if (e.Kind == ExtendedActivationKind.Launch && e.Data is ILaunchActivatedEventArgs la)
            {
                // zusätzlicher Fallback: z.B. „Öffnen mit…“ liefert Argument(e)
                var arg = la.Arguments;
                if (!string.IsNullOrWhiteSpace(arg) && arg.EndsWith(".dcm", StringComparison.OrdinalIgnoreCase))
                    DViewer.App.EnqueueOrHandle(arg);
            }
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}