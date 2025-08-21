using System;
using Microsoft.Maui.Controls;

namespace DViewer
{
    /// <summary>
    /// Saubere Übergabe eines Window/Level-Renderers ins ViewModel.
    /// </summary>
    public interface IWindowingSink
    {
        /// <param name="render">Renderer: (center,width,frame) -> ImageSource.</param>
        /// <param name="defaultCenter">Default Window Center.</param>
        /// <param name="defaultWidth">Default Window Width.</param>
        void SetWindowing(Func<double, double, int, ImageSource> render, double? defaultCenter, double? defaultWidth);
    }
}
