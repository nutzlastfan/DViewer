using FellowOakDicom.Imaging;
using FellowOakDicom;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DViewer
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            //builder.Services.AddSingleton<IDicomLoader, DicomLoader>();


#if WINDOWS
    Microsoft.Maui.Handlers.TimePickerHandler.Mapper.AppendToMapping("Force24h", (handler, view) =>
    {
        // WinUI TimePicker -> 24h
        handler.PlatformView.ClockIdentifier = "24HourClock";
    });
#endif

            new DicomSetupBuilder()
                .RegisterServices(s => s
                    .AddFellowOakDicom()
                    .AddTranscoderManager<FellowOakDicom.Imaging.NativeCodec.NativeTranscoderManager>())
                .SkipValidation()
                .Build();

            return builder.Build();
        }




   

    }
}
