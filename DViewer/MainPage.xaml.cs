using System.Windows.Input;


namespace DViewer
{



    public class MetadataDifference
    {
        public string Key { get; init; } = string.Empty;
        public string LeftValue { get; init; } = string.Empty;
        public string RightValue { get; init; } = string.Empty;
    }


    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            var loader = new DicomLoader();
            var vm = new MainViewModel(loader);
            BindingContext = vm;

            App.MainVM = vm;
            // ggf. schon vor dem UI eingetroffene Dateien abarbeiten
            while (App.PendingOpens.TryDequeue(out var p))
                _ = vm.HandleExternalOpenAsync(p);



        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SetWindowTitle($"DViewer");
        }

        private static void SetWindowTitle(string title)
        {
#if WINDOWS || MACCATALYST
            var win = Application.Current?.Windows?.FirstOrDefault();
            if (win != null)
                win.Title = title;
#endif
        }



        private async void OnLeftPreviewTapped(object? sender, TappedEventArgs e)
        {
            if (BindingContext is MainViewModel vm && vm.Left.Image != null)
                await Navigation.PushModalAsync(new FullscreenImagePage(vm.Left.Image, vm.Left.FileName));
        }

        private async void OnRightPreviewTapped(object? sender, TappedEventArgs e)
        {
            if (BindingContext is MainViewModel vm && vm.Right.Image != null)
                await Navigation.PushModalAsync(new FullscreenImagePage(vm.Right.Image, vm.Right.FileName));
        }


    }
}
