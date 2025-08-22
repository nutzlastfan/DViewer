using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;

namespace DViewer.Controls.Tools
{
    public interface IViewerTool
    {
        // Wird beim Aktivieren/Deaktivieren des Tools gerufen
        void OnActivated(DicomViewerView viewer);
        void OnDeactivated(DicomViewerView viewer);

        // Pointer (plattf.-unabhängig, Screen-Koordinaten relativ zum Root/Container)
        void OnPointerPressed(DicomViewerView viewer, Point pos, bool left, bool right, bool middle);
        void OnPointerMoved(DicomViewerView viewer, Point pos);
        void OnPointerReleased(DicomViewerView viewer, Point pos);

        // Touch/Trackpad (non-Windows)
        void OnPinch(DicomViewerView viewer, PinchGestureUpdatedEventArgs e);
        void OnPan(DicomViewerView viewer, PanUpdatedEventArgs e);
        void OnTwoFingerPan(DicomViewerView viewer, PanUpdatedEventArgs e);

        // Overlay-Rendering (z.B. Mess-Linie)
        void Draw(ICanvas canvas, RectF dirtyRect, DicomViewerView viewer);
    }
}
