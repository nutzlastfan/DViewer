using System;
using Microsoft.Maui.Controls;

namespace DViewer
{
    /// <summary>
    /// Saubere Übergabe eines Frame-Providers (cine/multiframe) ins ViewModel.
    /// </summary>
    public interface IFrameProviderSink
    {
        /// <param name="frameCount">Gesamtzahl Frames.</param>
        /// <param name="getFrame">Liefert einen ImageSource für den gewünschten Frame-Index.</param>
        /// <param name="prefetch">Optionales Vorabrendern (z.B. index+1..index+N).</param>
        void SetFrameProvider(int frameCount, Func<int, ImageSource?> getFrame, Action<int>? prefetch = null);
    }
}
