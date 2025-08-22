using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using DViewer.Controls.Overlays;

namespace DViewer.Controls.Tools
{
    public sealed class CircleTool : IViewerTool
    {
        public bool IsInteracting { get; private set; }

        private PointF _centerImg, _rimImg;
        private bool _dragging;

        public void OnActivated(DicomViewerView v) { v.InvalidateToolOverlay(); }
        public void OnDeactivated(DicomViewerView v) { _dragging = false; IsInteracting = false; v.InvalidateToolOverlay(); }

        public void OnPointerPressed(DicomViewerView v, Point pt, bool left, bool right, bool middle)
        {
            if (!left) return;
            if (!v.TryScreenToImage(pt, out var img)) return;
            _centerImg = _rimImg = img;
            _dragging = true;
            IsInteracting = true;
            v.InvalidateToolOverlay();
        }

        public void OnPointerMoved(DicomViewerView v, Point pt)
        {
            if (!_dragging) return;
            if (v.TryScreenToImage(pt, out var img)) _rimImg = img;
            v.InvalidateToolOverlay();
        }

        public void OnPointerReleased(DicomViewerView v, Point pt)
        {
            if (!_dragging) return;
            _dragging = false;
            IsInteracting = false;

            var shape = new CircleShape(_centerImg, _rimImg);
            v.AddCircleForCurrent(shape);

            v.CurrentTool = DViewer.Controls.DicomViewerView.ViewerTool.Cursor;
            v.InvalidateToolOverlay();
        }

        public void Draw(ICanvas canvas, RectF dirtyRect, DViewer.Controls.DicomViewerView v)
        {
            // Bestehendes zeichnen
            CursorTool.DrawAllOverlays(canvas, v, hotShape: null, hotHandle: -1);

            if (!_dragging) return;

            // Live-Kreis
            if (!v.TryImageToScreen(_centerImg, out var sc)) return;
            if (!v.TryImageToScreen(_rimImg, out var sr)) return;

            float r = (float)System.Math.Sqrt((sr.X - sc.X) * (sr.X - sc.X) + (sr.Y - sc.Y) * (sr.Y - sc.Y));
            canvas.StrokeColor = Colors.DeepSkyBlue;
            canvas.StrokeSize = 2.5f;
            canvas.DrawCircle((float)sc.X, (float)sc.Y, r);

            var (ok, rmm) = RadiusMm(v, _centerImg, _rimImg);
            string text = ok
                ? $"A: {System.Math.PI * rmm * rmm:0.0} mm²   D: {2 * rmm:0.0} mm"
                : $"r: {r:0} px";
            CursorTool.DrawLabel(canvas, text, new Point((float)sc.X, (float)sc.Y - r - 10));
        }

        public void OnPinch(DicomViewerView v, PinchGestureUpdatedEventArgs e) { }
        public void OnPan(DicomViewerView v, PanUpdatedEventArgs e) { }
        public void OnTwoFingerPan(DicomViewerView v, PanUpdatedEventArgs e) { }

        static (bool ok, double rmm) RadiusMm(DicomViewerView v, PointF center, PointF rim)
        {
            var (ok, mm, _) = v.MeasureLength(center, rim);
            return (ok, mm);
        }
    }
}
