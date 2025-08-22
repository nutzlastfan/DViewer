using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using DViewer.Controls.Overlays;

namespace DViewer.Controls.Tools
{
    /// <summary>
    /// Standard-Werkzeug: WL (L-Maus), Zoom (R-Maus), Pan (Mitte)
    /// Zusätzlich: Messlinien nachträglich bearbeiten (Handles ziehen), Hover-Highlight.
    /// </summary>
    public sealed class CursorTool : IViewerTool
    {



        public void OnPinch(DViewer.Controls.DicomViewerView v, PinchGestureUpdatedEventArgs e) { }
        public void OnPan(DViewer.Controls.DicomViewerView v, PanUpdatedEventArgs e) { }
        public void OnTwoFingerPan(DViewer.Controls.DicomViewerView v, PanUpdatedEventArgs e) { }




        // WL/Zoom/Move (wie zuvor – nur über öffentliche APIs der View)
        bool _lDown, _rDown, _mDown;
        Point _startScreen;
        double _wlStartC, _wlStartW;
        double _zoomStartScale;
        Point _zoomAnchor;

        const double WLWidthSensitivity = 2.0;
        const double WLCenterSensitivity = 2.0;
        const double WL_MIN_STEP_W = 1;
        const double WL_MIN_STEP_C = 1;

        // Messen: Hover/Drag
        MeasureShape? _hotShape;
        int _hotHandle = -1; // -1: none, 0: P1, 1: P2, 2: line
        bool _draggingHandle;

        const float HANDLE_R = 4f;
        const float HIT_HANDLE_R = 8f; // größerer Hit-Radius
        const float HIT_LINE_PX = 6f;

        public void OnActivated(DicomViewerView v)
        {
            v.SetHoverCursor(DicomViewerView.HoverCursor.Arrow);
            v.InvalidateToolOverlay();
        }

        public void OnDeactivated(DicomViewerView v)
        {
            _lDown = _rDown = _mDown = false;
            _draggingHandle = false;
            _hotShape = null; _hotHandle = -1;
            v.SetHoverCursor(DicomViewerView.HoverCursor.Arrow);
            v.InvalidateToolOverlay();
        }

        public void OnPointerPressed(DicomViewerView v, Point p, bool left, bool right, bool middle)
        {
            // Zuerst prüfen, ob wir eine Messlinie anfassen
            UpdateHot(v, p);
            if (left && _hotShape != null && _hotHandle >= 0)
            {
                _draggingHandle = true;
                v.SetHoverCursor(DicomViewerView.HoverCursor.SizeAll);
                return;
            }

            // sonst Standard: WL/Zoom/Move
            _lDown = left; _rDown = right; _mDown = middle;
            _startScreen = p;

            _wlStartC = v.WindowCenter;
            _wlStartW = v.WindowWidth;
            _zoomStartScale = v.CurrentScale;
            _zoomAnchor = p;

            v.GetTranslation(out _startTx, out _startTy);
        }

        double _startTx, _startTy;

        public void OnPointerMoved(DicomViewerView v, Point p)
        {
            if (_draggingHandle)
            {
                if (v.TryScreenToImage(p, out var img))
                {
                    if (_hotHandle == 0) _hotShape!.P1 = img;
                    else _hotShape!.P2 = img;
                    v.InvalidateToolOverlay();
                }
                return;
            }

            // Hover-Status für Messungen aktualisieren (nur wenn keine Taste gedrückt)
            if (!_lDown && !_rDown && !_mDown)
                UpdateHot(v, p);

            if (_lDown && v.CanWindowLevel)
            {
                var dx = p.X - _startScreen.X;
                var dy = p.Y - _startScreen.Y;
                var newW = Math.Max(1, _wlStartW + dx * WLWidthSensitivity);
                var newC = _wlStartC - dy * WLCenterSensitivity;
                if (Math.Abs(newW - v.WindowWidth) >= WL_MIN_STEP_W) v.SetWindowWidth(newW);
                if (Math.Abs(newC - v.WindowCenter) >= WL_MIN_STEP_C) v.SetWindowCenter(newC);
            }
            else if (_rDown)
            {
                var dy = p.Y - _startScreen.Y;
                var factor = Math.Pow(1.01, -dy);
                var newScale = Math.Clamp(_zoomStartScale * factor, v.MinScale, v.MaxScale);
                v.ZoomAt(_zoomAnchor, newScale);
            }
            else if (_mDown)
            {
                var dx = p.X - _startScreen.X;
                var dy = p.Y - _startScreen.Y;
                v.SetTranslation(_startTx + dx, _startTy + dy);
            }
        }

        public void OnPointerReleased(DicomViewerView v, Point p)
        {
            if (_draggingHandle)
            {
                _draggingHandle = false;
                UpdateHot(v, p);
                return;
            }

            _lDown = _rDown = _mDown = false;
            v.CommitScale(); // falls gezoomt wurde
        }

        public void Draw(ICanvas canvas, RectF dirty, DicomViewerView v)
        {
            DrawAllMeasures(canvas, v, _hotShape, _hotHandle);
        }

        // ---------- Measure rendering (shared) ----------
        public static void DrawAllMeasures(ICanvas canvas, DicomViewerView v, MeasureShape? hotShape, int hotHandle)
        {
            var list = v.GetMeasuresForCurrent();
            foreach (var m in list)
            {
                if (!v.TryImageToScreen(m.P1, out var s1)) continue;
                if (!v.TryImageToScreen(m.P2, out var s2)) continue;

                var isHot = (m == hotShape);
                MeasureTool.DrawMeasure(canvas, v, s1, s2, isHot);

                // Länge
                var (mmOk, lenMm, lenPx) = v.MeasureLength(m.P1, m.P2);
                var mid = new Point((s1.X + s2.X) / 2, (s1.Y + s2.Y) / 2);
                var text = mmOk ? $"{lenMm:0.0} mm" : $"{lenPx:0} px";
                MeasureTool.DrawLabel(canvas, text, mid);
            }
        }

        // ---------- HitTest ----------
        void UpdateHot(DicomViewerView v, Point p)
        {
            _hotShape = null; _hotHandle = -1;
            v.SetHoverCursor(DicomViewerView.HoverCursor.Arrow);

            var list = v.GetMeasuresForCurrent();
            foreach (var m in list)
            {
                if (!v.TryImageToScreen(m.P1, out var s1)) continue;
                if (!v.TryImageToScreen(m.P2, out var s2)) continue;

                if (Dist(p, s1) <= HIT_HANDLE_R) { _hotShape = m; _hotHandle = 0; v.SetHoverCursor(DicomViewerView.HoverCursor.SizeAll); return; }
                if (Dist(p, s2) <= HIT_HANDLE_R) { _hotShape = m; _hotHandle = 1; v.SetHoverCursor(DicomViewerView.HoverCursor.SizeAll); return; }
                if (PointToSegmentDist(p, s1, s2) <= HIT_LINE_PX) { _hotShape = m; _hotHandle = 2; v.SetHoverCursor(DicomViewerView.HoverCursor.SizeAll); return; }
            }
            v.InvalidateToolOverlay();
        }

        static double Dist(Point a, Point b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        static double PointToSegmentDist(Point p, Point a, Point b)
        {
            var vx = b.X - a.X; var vy = b.Y - a.Y;
            var wx = p.X - a.X; var wy = p.Y - a.Y;
            var c1 = vx * wx + vy * wy;
            if (c1 <= 0) return Dist(p, a);
            var c2 = vx * vx + vy * vy;
            if (c2 <= c1) return Dist(p, b);
            var t = c1 / c2;
            var proj = new Point(a.X + t * vx, a.Y + t * vy);
            return Dist(p, proj);
        }
    }
}
