using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace DViewer.Controls.Tools
{
    public interface IViewerTool
    {
        bool IsInteracting { get; } // TRUE während Drag/Resize → WL/WW/Pan/Zoom blocken

        void OnActivated(DViewer.Controls.DicomViewerView v);
        void OnDeactivated(DViewer.Controls.DicomViewerView v);

        void Draw(ICanvas canvas, RectF dirtyRect, DViewer.Controls.DicomViewerView v);

        void OnPointerPressed (DViewer.Controls.DicomViewerView v, Point pt, bool left, bool right, bool middle);
        void OnPointerMoved   (DViewer.Controls.DicomViewerView v, Point pt);
        void OnPointerReleased(DViewer.Controls.DicomViewerView v, Point pt);

        void OnPinch        (DViewer.Controls.DicomViewerView v, PinchGestureUpdatedEventArgs e);
        void OnPan          (DViewer.Controls.DicomViewerView v, PanUpdatedEventArgs e);
        void OnTwoFingerPan (DViewer.Controls.DicomViewerView v, PanUpdatedEventArgs e);
    }
}
