using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using DViewer.Controls.Overlays;

namespace DViewer.Controls.Tools
{
    /// <summary>
    /// Zeichnet eine neue Messlinie (live). Nach Loslassen wird gespeichert und
    /// automatisch auf Cursor-Tool umgeschaltet.
    /// </summary>
    public sealed class MeasureTool : IViewerTool
    {
        bool _dragging;
        Point _startScreen;    // Start in Screen-Koordinaten (nur fürs Live-Zeichnen)
        Point _currScreen;
        PointF _startImg;      // Start in Bildkoordinaten (col,row)
        PointF _currImg;

        const float HANDLE_R = 4f;     // px (Screen)
        const float LINE_W = 2f;

        public bool IsInteracting { get; private set; }


        // Gesten derzeit nicht benötigt → No-Op
        public void OnPinch(DViewer.Controls.DicomViewerView v, PinchGestureUpdatedEventArgs e) { }
        public void OnPan(DViewer.Controls.DicomViewerView v, PanUpdatedEventArgs e) { }
        public void OnTwoFingerPan(DViewer.Controls.DicomViewerView v, PanUpdatedEventArgs e) { }

        public void OnActivated(DicomViewerView v)
        {
            v.SetHoverCursor(DicomViewerView.HoverCursor.Crosshair);
            v.InvalidateToolOverlay();
        }

        public void OnDeactivated(DicomViewerView v)
        {
            v.SetHoverCursor(DicomViewerView.HoverCursor.Arrow);
            v.InvalidateToolOverlay();
        }

        public void OnPointerPressed(DicomViewerView v, Point p, bool left, bool right, bool middle)
        {
            if (!left) return;
            if (!v.TryScreenToImage(p, out var imgPt)) return;

            _dragging = true;
            IsInteracting = true;                 // <—
            _startScreen = _currScreen = p;
            _startImg = _currImg = imgPt;
            v.InvalidateToolOverlay();
        }

        public void OnPointerReleased(DicomViewerView v, Point p)
        {
            if (!_dragging) return;
            _dragging = false;
            IsInteracting = false;                // <—

            var shape = new MeasureShape(_startImg, _currImg);
            v.AddMeasureForCurrent(shape);

            v.CurrentTool = DViewer.Controls.DicomViewerView.ViewerTool.Cursor;
            v.InvalidateToolOverlay();
        }

        public void OnPointerMoved(DicomViewerView v, Point p)
        {
            if (!_dragging) return;
            _currScreen = p;
            if (v.TryScreenToImage(p, out var imgPt))
                _currImg = imgPt;

            v.InvalidateToolOverlay();
        }


        public void Draw(ICanvas canvas, RectF dirty, DicomViewerView v)
        {
            // Bestehende Shapes inkl. Labels
            CursorTool.DrawAllOverlays(canvas, v); // hotShape/Handle leer lassen

            // Live-Linie beim Ziehen
            if (!_dragging) return;

            // Screenpunkte berechnen
            if (!v.TryImageToScreen(_startImg, out var s1)) s1 = _startScreen;
            if (!v.TryImageToScreen(_currImg, out var s2)) s2 = _currScreen;

            DrawMeasure(canvas, v, s1, s2, isHot: true);

            // Live-Text
            var (mmOk, lenMm, lenPx) = v.MeasureLength(_startImg, _currImg);
            var mid = new Point((s1.X + s2.X) / 2, (s1.Y + s2.Y) / 2);
            var text = mmOk ? $"{lenMm:0.0} mm" : $"{lenPx:0} px";
            CursorTool.DrawLabel(canvas, text, new Point(mid.X - 20, mid.Y - 22));
        }

        // --- helpers ---
        internal static void DrawMeasure(ICanvas canvas, DicomViewerView v, Point s1, Point s2, bool isHot)
        {
            canvas.StrokeSize = LINE_W + (isHot ? 1.5f : 0);
            canvas.StrokeColor = isHot ? Colors.DeepSkyBlue : Colors.White;
            canvas.DrawLine((float)s1.X, (float)s1.Y, (float)s2.X, (float)s2.Y);

            // Handles
            canvas.FillColor = isHot ? Colors.DeepSkyBlue : Colors.White;
            canvas.Alpha = 0.9f;
            canvas.FillCircle((float)s1.X, (float)s1.Y, HANDLE_R);
            canvas.FillCircle((float)s2.X, (float)s2.Y, HANDLE_R);
            canvas.Alpha = 1f;
        }

        internal static void DrawLabel(ICanvas canvas, string text, Point mid)
        {
            var pad = 4f;

            // Schrift setzen (optional)
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = 12;

            // <-- WICHTIG: 3-Parameter-Überladung verwenden!
            var size = canvas.GetStringSize(text, Microsoft.Maui.Graphics.Font.Default, 12f);

            var w = size.Width + 2 * pad;
            var h = size.Height + 2 * pad;

            var x = (float)mid.X - w / 2f;
            var y = (float)mid.Y - h - 8f;

            canvas.FillColor = new Color(0, 0, 0, 0.6f);
            canvas.FillRectangle(x, y, w, h);

            canvas.FontColor = Colors.White;
            canvas.DrawString(text, x + pad, y + pad, HorizontalAlignment.Left);
        }

    }
}
