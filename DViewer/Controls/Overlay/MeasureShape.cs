using Microsoft.Maui.Graphics;
using System;

namespace DViewer.Controls.Overlays
{
    /// <summary>
    /// Mess-Linie in Bildkoordinaten (Col/Row in Pixeln des DICOM-Bildrasters).
    /// </summary>
    public sealed class MeasureShape
    {
        public PointF P1;   // (col,row)
        public PointF P2;   // (col,row)
        public Guid Id { get; } = Guid.NewGuid();

        public MeasureShape(PointF p1, PointF p2)
        {
            P1 = p1; P2 = p2;
        }
    }
}
