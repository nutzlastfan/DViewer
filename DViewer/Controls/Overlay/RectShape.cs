using Microsoft.Maui.Graphics;

namespace DViewer.Controls.Overlays
{
    public sealed class RectShape
    {
        public PointF P1 { get; set; }
        public PointF P2 { get; set; }
        public RectShape() { }
        public RectShape(PointF p1, PointF p2) { P1 = p1; P2 = p2; }
    }
}
