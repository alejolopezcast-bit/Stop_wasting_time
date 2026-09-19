using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace StopWastingTime.App.Controls;

/// <summary>
/// The countdown ring: an arc that fills clockwise from the top as the session progresses. Drawn here
/// rather than pulled in from a charting library, because it is fifty lines of geometry and no
/// dependency.
/// </summary>
public sealed class ProgressRing : Shape
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction),
        typeof(double),
        typeof(ProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInvalidated));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness),
        typeof(double),
        typeof(ProgressRing),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInvalidated));

    /// <summary>How much of the ring is drawn, from 0 to 1.</summary>
    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    /// <summary>Stroke width, kept as a property so the ring can be reused at other sizes.</summary>
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override Geometry DefiningGeometry
    {
        get
        {
            var fraction = Math.Clamp(Fraction, 0, 1);
            var size = Math.Min(ActualWidth, ActualHeight);

            if (size <= 0 || fraction <= 0)
            {
                return Geometry.Empty;
            }

            var radius = (size - Thickness) / 2;
            var centre = new Point(ActualWidth / 2, ActualHeight / 2);
            var start = new Point(centre.X, centre.Y - radius);

            // A full circle cannot be drawn as one arc: the end point would land on the start point and
            // the segment would collapse, so it is drawn as two halves.
            if (fraction >= 1)
            {
                return new EllipseGeometry(centre, radius, radius);
            }

            var angle = fraction * 2 * Math.PI;
            var end = new Point(
                centre.X + (radius * Math.Sin(angle)),
                centre.Y - (radius * Math.Cos(angle)));

            var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment(
                end,
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: fraction > 0.5,
                SweepDirection.Clockwise,
                isStroked: true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return geometry;
        }
    }

    private static void OnGeometryInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ProgressRing)d).InvalidateVisual();

    protected override Size MeasureOverride(Size constraint)
    {
        // Shape measures to its geometry, which would make the ring collapse inside a Grid cell. It is
        // sized by its container instead.
        return new Size(
            double.IsInfinity(constraint.Width) ? 0 : constraint.Width,
            double.IsInfinity(constraint.Height) ? 0 : constraint.Height);
    }
}
