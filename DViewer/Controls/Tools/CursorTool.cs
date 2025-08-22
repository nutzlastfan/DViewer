using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using DViewer.Controls.Overlays;

namespace DViewer.Controls.Tools
{
    public sealed class CursorTool : IViewerTool
    {
        public bool IsInteracting { get; private set; }

        const float HANDLE_R = 5f;
        const float LINE_W = 2f;
        const float HIT_PX = 8f;

        // Neu: Kanten-Toleranz & Linien-Body nur in der Mitte greifbar
        const float EDGE_HIT = 8f;            // Treffer-Toleranz für Rechteck-Kanten
        const float MID_GRAB_WINDOW = 0.25f;  // Line-Body nur +-25% um die Mitte greifbar

        // Optional: Kanten-Marker beim Rect zeigen, wenn hot
        const bool SHOW_EDGE_MARKERS = true;

        private DicomViewerView? _v;

        public enum HotKind { None, Line, Circle, Rect }
        private HotKind _hotKind = HotKind.None;
        private object? _hotShape;
        // Line: 0=P1 1=P2 2=body | Circle: 0=center 1=rim 2=body | Rect: 0..3 corners, 10..13 edges
        private int _hotHandle = -1;
        private Point _lastScreen;

        public void OnActivated(DicomViewerView v) { _v = v; IsInteracting = false; }
        public void OnDeactivated(DicomViewerView v) { IsInteracting = false; _v = null; }

        public void Draw(ICanvas canvas, RectF dirtyRect, DicomViewerView v)
        {
            DrawAllOverlays(canvas, v, _hotShape, _hotKind, _hotHandle);
        }

        /// <summary>
        /// Zeichnet ALLE Shapes (mit optionalem Hot-Highlighting) und Labels (Line/Circle/Rect).
        /// Kann auch aus anderen Tools (z.B. MeasureTool) aufgerufen werden.
        /// </summary>
        public static void DrawAllOverlays(ICanvas canvas, DicomViewerView v, object? hotShape = null, HotKind hotKind = HotKind.None, int hotHandle = -1)
        {
            // LINES
            foreach (var sh in v.GetMeasuresForCurrent())
            {
                if (!v.TryImageToScreen(sh.P1, out var s1) || !v.TryImageToScreen(sh.P2, out var s2)) continue;
                bool isHot = hotKind == HotKind.Line && ReferenceEquals(sh, hotShape);
                DrawLine(canvas, s1, s2, isHot, isHot ? hotHandle : -1);

                // Label IMMER anzeigen (mm bevorzugt, sonst px) – leicht über der Mitte
                var (mmOk, mm, px) = v.MeasureLength(sh.P1, sh.P2);
                var mid = new Point((s1.X + s2.X) / 2, (s1.Y + s2.Y) / 2);
                var text = mmOk ? $"{mm:0.0} mm" : $"{px:0} px";
                DrawLabel(canvas, text, new Point(mid.X - 20, mid.Y - 22));
            }

            // CIRCLES
            foreach (var c in v.GetCirclesForCurrent())
            {
                if (!v.TryImageToScreen(c.Center, out var sc)) continue;
                if (!v.TryImageToScreen(c.Rim, out var sr)) continue;

                float r = Distance(sr, sc);
                bool isHot = hotKind == HotKind.Circle && ReferenceEquals(c, hotShape);

                // Kreis
                canvas.StrokeColor = isHot ? Colors.DeepSkyBlue : Colors.White;
                canvas.StrokeSize = LINE_W + (isHot ? 1.5f : 0f);
                canvas.DrawCircle((float)sc.X, (float)sc.Y, r);

                // sichtbarer Mittelpunkt
                canvas.FillColor = isHot ? Colors.DeepSkyBlue : Colors.White;
                canvas.FillCircle((float)sc.X, (float)sc.Y, HANDLE_R);

                // Rand-Handle markieren, wenn hot und Handle=Rim
                if (isHot && hotHandle == 1)
                {
                    canvas.FillColor = Colors.Yellow;
                    canvas.FillCircle((float)sr.X, (float)sr.Y, HANDLE_R - 1);
                }

                // Label: Fläche + Durchmesser (mm bevorzugt, sonst px)
                var (ok, rmm) = RadiusMm(v, c.Center, c.Rim);
                string text = ok
                    ? $"A: {Math.PI * rmm * rmm:0.0} mm²   D: {2 * rmm:0.0} mm"
                    : $"r: {r:0} px";
                DrawLabel(canvas, text, new Point((float)sc.X - 20, (float)sc.Y - r - 22));
            }

            // RECTS
            foreach (var r in v.GetRectsForCurrent())
            {
                if (!v.TryImageToScreen(r.P1, out var s1) || !v.TryImageToScreen(r.P2, out var s2)) continue;

                var x = (float)Math.Min(s1.X, s2.X);
                var y = (float)Math.Min(s1.Y, s2.Y);
                var w = (float)Math.Abs(s2.X - s1.X);
                var h = (float)Math.Abs(s2.Y - s1.Y);
                var x2 = x + w;
                var y2 = y + h;

                bool isHot = hotKind == HotKind.Rect && ReferenceEquals(r, hotShape);

                canvas.StrokeColor = isHot ? Colors.DeepSkyBlue : Colors.White;
                canvas.StrokeSize = LINE_W + (isHot ? 1.5f : 0f);
                canvas.DrawRectangle(x, y, w, h);

                // vier Eck-Handles sichtbar
                var corners = new[]
                {
                    new Point(x, y),             // TL (0)
                    new Point(x + w, y),         // TR (1)
                    new Point(x + w, y + h),     // BR (2)
                    new Point(x, y + h)          // BL (3)
                };

                canvas.FillColor = isHot ? Colors.DeepSkyBlue : Colors.White;
                for (int i = 0; i < 4; i++)
                    canvas.FillCircle((float)corners[i].X, (float)corners[i].Y, HANDLE_R);

                if (isHot && hotHandle >= 0 && hotHandle <= 3)
                {
                    canvas.FillColor = Colors.Yellow;
                    canvas.FillCircle((float)corners[hotHandle].X, (float)corners[hotHandle].Y, HANDLE_R - 1);
                }

                // (optional) Kanten-Marker, wenn hot
                if (isHot && SHOW_EDGE_MARKERS)
                {
                    canvas.FillColor = Colors.DeepSkyBlue;
                    canvas.FillCircle(x + w / 2, y, HANDLE_R - 2);       // top
                    canvas.FillCircle(x + w / 2, y2, HANDLE_R - 2);      // bottom
                    canvas.FillCircle(x, y + h / 2, HANDLE_R - 2);       // left
                    canvas.FillCircle(x2, y + h / 2, HANDLE_R - 2);      // right

                    if (hotHandle >= 10 && hotHandle <= 13)
                    {
                        canvas.FillColor = Colors.Yellow;
                        switch (hotHandle)
                        {
                            case 10: canvas.FillCircle(x, y + h / 2, HANDLE_R - 2); break;      // left
                            case 11: canvas.FillCircle(x + w / 2, y, HANDLE_R - 2); break;      // top
                            case 12: canvas.FillCircle(x2, y + h / 2, HANDLE_R - 2); break;     // right
                            case 13: canvas.FillCircle(x + w / 2, y2, HANDLE_R - 2); break;     // bottom
                        }
                    }
                }

                // Label: Breite/Höhe/Fläche
                RectMetrics(v, r.P1, r.P2, out var wmm, out var hmm, out var amm, out var wpx, out var hpx);
                string text = (wmm >= 0 && hmm >= 0)
                    ? $"W: {wmm:0.0} mm  H: {hmm:0.0} mm  A: {amm:0.0} mm²"
                    : $"W: {wpx:0} px  H: {hpx:0} px";
                DrawLabel(canvas, text, new Point(x + 6, y - 18));
            }
        }

        // =================== Pointer ===================

        public void OnPointerPressed(DicomViewerView v, Point pt, bool left, bool right, bool middle)
        {
            _hotKind = HotKind.None;
            _hotShape = null;
            _hotHandle = -1;
            _lastScreen = pt;

            if (!left) { IsInteracting = false; return; }

            // Reihenfolge: zuletzt gezeichnetes zuerst ⇒ reverse
            // 1) Rect (Ecken + Kanten – Innenraum „durchlässig“)
            foreach (var rect in v.GetRectsForCurrent().Reverse())
            {
                if (HitRect(v, rect, pt, out var handle))
                {
                    _hotKind = HotKind.Rect; _hotShape = rect; _hotHandle = handle;
                    IsInteracting = true; return;
                }
            }

            // 2) Circles
            foreach (var c in v.GetCirclesForCurrent().Reverse())
            {
                if (HitCircle(v, c, pt, out var handle))
                {
                    _hotKind = HotKind.Circle; _hotShape = c; _hotHandle = handle;
                    IsInteracting = true; return;
                }
            }

            // 3) Lines (Body nur nahe Mitte greifbar)
            foreach (var sh in v.GetMeasuresForCurrent().Reverse())
            {
                if (!v.TryImageToScreen(sh.P1, out var s1) || !v.TryImageToScreen(sh.P2, out var s2)) continue;

                if (Dist(pt, s1) <= HIT_PX) { SetHotLine(sh, 0); return; }
                if (Dist(pt, s2) <= HIT_PX) { SetHotLine(sh, 1); return; }

                var (d, t) = DistToSegmentWithT(pt, s1, s2);
                if (d <= HIT_PX && Math.Abs(t - 0.5) <= MID_GRAB_WINDOW)
                {
                    SetHotLine(sh, 2); return;
                }
            }

            IsInteracting = false;
        }

        public void OnPointerMoved(DicomViewerView v, Point pt)
        {
            if (!IsInteracting || _hotShape == null) return;

            if (!v.TryScreenToImage(pt, out var imgNow)) return;
            if (!v.TryScreenToImage(_lastScreen, out var imgPrev)) return;

            var dCol = imgNow.X - imgPrev.X;
            var dRow = imgNow.Y - imgPrev.Y;

            switch (_hotKind)
            {
                case HotKind.Line:
                    {
                        var ln = (MeasureShape)_hotShape;
                        switch (_hotHandle)
                        {
                            case 0: ln.P1 = imgNow; break;
                            case 1: ln.P2 = imgNow; break;
                            case 2:
                                ln.P1 = new PointF(ln.P1.X + (float)dCol, ln.P1.Y + (float)dRow);
                                ln.P2 = new PointF(ln.P2.X + (float)dCol, ln.P2.Y + (float)dRow);
                                break;
                        }
                        break;
                    }
                case HotKind.Circle:
                    {
                        var c = (CircleShape)_hotShape;
                        switch (_hotHandle)
                        {
                            case 0: // center
                                c.Center = imgNow;
                                c.Rim = new PointF(c.Rim.X + (float)dCol, c.Rim.Y + (float)dRow); // Radius/Position mitnehmen
                                break;
                            case 1: // rim
                                c.Rim = imgNow;
                                break;
                            case 2: // body (innerhalb)
                                c.Center = new PointF(c.Center.X + (float)dCol, c.Center.Y + (float)dRow);
                                c.Rim = new PointF(c.Rim.X + (float)dCol, c.Rim.Y + (float)dRow);
                                break;
                        }
                        break;
                    }
                case HotKind.Rect:
                    {
                        var rr = (RectShape)_hotShape;

                        // aktuelle Grenzen (Bildkoords)
                        float minX = MathF.Min(rr.P1.X, rr.P2.X);
                        float maxX = MathF.Max(rr.P1.X, rr.P2.X);
                        float minY = MathF.Min(rr.P1.Y, rr.P2.Y);
                        float maxY = MathF.Max(rr.P1.Y, rr.P2.Y);

                        switch (_hotHandle)
                        {
                            case 0: // TL
                                minX = (float)imgNow.X;
                                minY = (float)imgNow.Y;
                                break;
                            case 1: // TR
                                maxX = (float)imgNow.X;
                                minY = (float)imgNow.Y;
                                break;
                            case 2: // BR
                                maxX = (float)imgNow.X;
                                maxY = (float)imgNow.Y;
                                break;
                            case 3: // BL
                                minX = (float)imgNow.X;
                                maxY = (float)imgNow.Y;
                                break;

                            // Kanten-Resize:
                            case 10: minX = (float)imgNow.X; break; // Left edge
                            case 11: minY = (float)imgNow.Y; break; // Top edge
                            case 12: maxX = (float)imgNow.X; break; // Right edge
                            case 13: maxY = (float)imgNow.Y; break; // Bottom edge
                        }

                        // Normieren (falls gekreuzt)
                        var nminX = MathF.Min(minX, maxX);
                        var nmaxX = MathF.Max(minX, maxX);
                        var nminY = MathF.Min(minY, maxY);
                        var nmaxY = MathF.Max(minY, maxY);

                        rr.P1 = new PointF(nminX, nminY);
                        rr.P2 = new PointF(nmaxX, nmaxY);
                        break;
                    }
            }

            _lastScreen = pt;
            v.InvalidateToolOverlay();
        }

        public void OnPointerReleased(DicomViewerView v, Point pt)
        {
            IsInteracting = false;
            _hotHandle = -1;
            _hotShape = null;
            _hotKind = HotKind.None;
            v.InvalidateToolOverlay();
        }

        public void OnPinch(DicomViewerView v, PinchGestureUpdatedEventArgs e) { }
        public void OnPan(DicomViewerView v, PanUpdatedEventArgs e) { }
        public void OnTwoFingerPan(DicomViewerView v, PanUpdatedEventArgs e) { }

        // =================== Draw helpers ===================

        static void DrawLine(ICanvas c, Point s1, Point s2, bool hot, int hotHandle)
        {
            c.StrokeColor = hot ? Colors.DeepSkyBlue : Colors.White;
            c.StrokeSize = LINE_W + (hot ? 1.5f : 0f);
            c.DrawLine((float)s1.X, (float)s1.Y, (float)s2.X, (float)s2.Y);

            c.FillColor = hot ? Colors.DeepSkyBlue : Colors.White;
            c.FillCircle((float)s1.X, (float)s1.Y, HANDLE_R);
            c.FillCircle((float)s2.X, (float)s2.Y, HANDLE_R);

            if (hotHandle == 0) { c.FillColor = Colors.Yellow; c.FillCircle((float)s1.X, (float)s1.Y, HANDLE_R - 1); }
            if (hotHandle == 1) { c.FillColor = Colors.Yellow; c.FillCircle((float)s2.X, (float)s2.Y, HANDLE_R - 1); }
        }

        public static void DrawLabel(ICanvas canvas, string text, Point anchor)
        {
            const float pad = 4f;
            var font = Microsoft.Maui.Graphics.Font.Default;
            const float fontSize = 12f;
            canvas.Font = font;
            canvas.FontSize = fontSize;

            var size = canvas.GetStringSize(text, font, fontSize);

            float w = size.Width + 2 * pad;
            float h = size.Height + 2 * pad;
            float x = (float)anchor.X;
            float y = (float)anchor.Y;

            canvas.FillColor = new Color(0, 0, 0, 0.6f);
            canvas.FillRoundedRectangle(x, y, w, h, 4);

            canvas.FontColor = Colors.White;
            canvas.DrawString(text, x + pad, y + pad, HorizontalAlignment.Left);
        }

        static (bool ok, double rmm) RadiusMm(DicomViewerView v, PointF center, PointF rim)
        {
            var (ok, mm, _) = v.MeasureLength(center, rim);
            return (ok, mm);
        }

        static void RectMetrics(DicomViewerView v, PointF p1, PointF p2,
            out double wmm, out double hmm, out double amm, out double wpx, out double hpx)
        {
            wmm = hmm = amm = -1;
            wpx = Math.Abs(p2.X - p1.X);
            hpx = Math.Abs(p2.Y - p1.Y);

            if (v.TryGetPixelSpacing(out var rowMm, out var colMm))
            {
                wmm = Math.Abs(p2.X - p1.X) * colMm;
                hmm = Math.Abs(p2.Y - p1.Y) * rowMm;
                amm = wmm * hmm;
            }
        }

        // =================== Hit-Tests ===================

        void SetHotLine(MeasureShape sh, int handle)
        {
            _hotKind = HotKind.Line;
            _hotShape = sh;
            _hotHandle = handle;
            IsInteracting = true;
        }

        static double Dist(Point a, Point b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        static float Distance(Point a, Point b)
            => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        static double DistToSegment(Point p, Point a, Point b)
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

        // Neu: Distanz + Parameter t entlang des Segments (0..1) – für „Mitte greifbar“
        static (double dist, double t) DistToSegmentWithT(Point p, Point a, Point b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double wx = p.X - a.X, wy = p.Y - a.Y;

            double c2 = vx * vx + vy * vy;
            if (c2 <= double.Epsilon) return (Dist(p, a), 0.0); // degeneriert

            double t = (vx * wx + vy * wy) / c2;
            if (t <= 0) return (Dist(p, a), 0.0);
            if (t >= 1) return (Dist(p, b), 1.0);

            var proj = new Point(a.X + t * vx, a.Y + t * vy);
            return (Dist(p, proj), t);
        }

        static bool HitCircle(DicomViewerView v, CircleShape c, Point pt, out int handle)
        {
            handle = -1;
            if (!v.TryImageToScreen(c.Center, out var sc) || !v.TryImageToScreen(c.Rim, out var sr))
                return false;

            var r = Distance(sr, sc);
            var d = Distance(pt, sc);

            // Center-Handle
            if (d <= HIT_PX) { handle = 0; return true; }

            // Rand überall greifbar (d ~ r)
            if (Math.Abs(d - r) <= HIT_PX) { handle = 1; return true; }

            // Body (innerhalb)
            if (d < r) { handle = 2; return true; }

            return false;
        }

        // Innenraum „durchlässig“: Nur Ecken und Kanten greifbar
        static bool HitRect(DicomViewerView v, RectShape r, Point pt, out int handle)
        {
            handle = -1;
            if (!v.TryImageToScreen(r.P1, out var s1) || !v.TryImageToScreen(r.P2, out var s2))
                return false;

            var x = Math.Min(s1.X, s2.X);
            var y = Math.Min(s1.Y, s2.Y);
            var w = Math.Abs(s2.X - s1.X);
            var h = Math.Abs(s2.Y - s1.Y);
            var x2 = x + w;
            var y2 = y + h;

            // Ecken (0..3)
            var corners = new[]
            {
                new Point(x,  y),   // TL = 0
                new Point(x2, y),   // TR = 1
                new Point(x2, y2),  // BR = 2
                new Point(x,  y2)   // BL = 3
            };
            for (int i = 0; i < 4; i++)
                if (Dist(pt, corners[i]) <= HIT_PX) { handle = i; return true; }

            // Kanten (10..13) – „Rand überall greifbar“
            bool onLeft = Math.Abs(pt.X - x) <= EDGE_HIT && pt.Y >= y && pt.Y <= y2;
            bool onRight = Math.Abs(pt.X - x2) <= EDGE_HIT && pt.Y >= y && pt.Y <= y2;
            bool onTop = Math.Abs(pt.Y - y) <= EDGE_HIT && pt.X >= x && pt.X <= x2;
            bool onBottom = Math.Abs(pt.Y - y2) <= EDGE_HIT && pt.X >= x && pt.X <= x2;

            if (onLeft) { handle = 10; return true; }
            if (onTop) { handle = 11; return true; }
            if (onRight) { handle = 12; return true; }
            if (onBottom) { handle = 13; return true; }

            // Innenraum NICHT greifbar → durchlässig
            return false;
        }
    }
}
