using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using DViewer.Controls.Overlays;

namespace DViewer.Controls.Tools
{
    public sealed class RectTool : IViewerTool
    {
        public bool IsInteracting { get; private set; }

        private PointF _p1Img, _p2Img;
        private bool _dragging;

        public void OnActivated(DicomViewerView v) { v.InvalidateToolOverlay(); }
        public void OnDeactivated(DicomViewerView v) { _dragging = false; IsInteracting = false; v.InvalidateToolOverlay(); }

        public void OnPointerPressed(DicomViewerView v, Point pt, bool left, bool right, bool middle)
        {
            if (!left) return;
            if (!v.TryScreenToImage(pt, out var img)) return;
            _p1Img = _p2Img = img;
            _dragging = true;
            IsInteracting = true;
            v.InvalidateToolOverlay();
        }

        public void OnPointerMoved(DicomViewerView v, Point pt)
        {
            if (!_dragging) return;
            if (v.TryScreenToImage(pt, out var img)) _p2Img = img;
            v.InvalidateToolOverlay();
        }

        public void OnPointerReleased(DicomViewerView v, Point pt)
        {
            if (!_dragging) return;
            _dragging = false;
            IsInteracting = false;

            v.AddRectForCurrent(new RectShape(_p1Img, _p2Img));
            v.CurrentTool = DViewer.Controls.DicomViewerView.ViewerTool.Cursor;
            v.InvalidateToolOverlay();
        }

        public void Draw(ICanvas canvas, RectF dirtyRect, DViewer.Controls.DicomViewerView v)
        {
            CursorTool.DrawAllOverlays(canvas, v, hotShape: null, hotHandle: -1);

            if (!_dragging) return;
            if (!v.TryImageToScreen(_p1Img, out var s1) || !v.TryImageToScreen(_p2Img, out var s2)) return;

            var x = (float)System.Math.Min(s1.X, s2.X);
            var y = (float)System.Math.Min(s1.Y, s2.Y);
            var w = (float)System.Math.Abs(s2.X - s1.X);
            var h = (float)System.Math.Abs(s2.Y - s1.Y);

            canvas.StrokeColor = Colors.DeepSkyBlue;
            canvas.StrokeSize = 2.5f;
            canvas.DrawRectangle(x, y, w, h);

            RectMetrics(v, _p1Img, _p2Img, out var wmm, out var hmm, out var amm, out var wpx, out var hpx);
            string text = (wmm >= 0 && hmm >= 0)
                ? $"W: {wmm:0.0} mm  H: {hmm:0.0} mm  A: {amm:0.0} mm²"
                : $"W: {wpx:0} px  H: {hpx:0} px";
            CursorTool.DrawLabel(canvas, text, new Point(x + 6, y - 18));
        }

        public void OnPinch(DicomViewerView v, PinchGestureUpdatedEventArgs e) { }
        public void OnPan(DicomViewerView v, PanUpdatedEventArgs e) { }
        public void OnTwoFingerPan(DicomViewerView v, PanUpdatedEventArgs e) { }

        static void RectMetrics(DicomViewerView v, PointF p1, PointF p2,
            out double wmm, out double hmm, out double amm, out double wpx, out double hpx)
        {
            wmm = hmm = amm = -1; wpx = System.Math.Abs(p2.X - p1.X); hpx = System.Math.Abs(p2.Y - p1.Y);
            if (v.TryGetPixelSpacing(out var rowMm, out var colMm))
            {
                wmm = System.Math.Abs(p2.X - p1.X) * colMm;
                hmm = System.Math.Abs(p2.Y - p1.Y) * rowMm;
                amm = wmm * hmm;
            }
        }
    }
}
