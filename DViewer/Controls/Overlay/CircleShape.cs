using Microsoft.Maui.Graphics;

namespace DViewer.Controls.Overlays
{
    public sealed class CircleShape
    {
        public PointF Center { get; set; }
        public PointF Rim { get; set; }
        public CircleShape() { }
        public CircleShape(PointF center, PointF rim) { Center = center; Rim = rim; }
    }
}
