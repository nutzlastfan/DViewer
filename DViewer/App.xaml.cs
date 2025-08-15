using System.Collections.Concurrent;
using Microsoft.Maui.ApplicationModel;

namespace DViewer
{
    public partial class App : Application
    {
        public static MainViewModel? MainVM { get; set; }
        public static readonly ConcurrentQueue<string> PendingOpens = new();

        public static void EnqueueOrHandle(string path)
        {
            if (MainVM == null)
                PendingOpens.Enqueue(path);
            else
                MainThread.BeginInvokeOnMainThread(async () => await MainVM.HandleExternalOpenAsync(path));
        }

        public App()
        {
            InitializeComponent();
            MainPage = new NavigationPage(new MainPage());
        }
    }
}
