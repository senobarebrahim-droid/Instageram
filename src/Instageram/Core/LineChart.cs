using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Instageram;

/// <summary>
/// Phase 3: a small line chart drawn directly with WPF primitives.
///
/// No charting package is used on purpose: adding one would grow the portable
/// package by megabytes for a single graph. The element re-renders whenever its
/// size or data changes, and it follows the active theme through the brushes
/// bound from XAML.
/// </summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(IReadOnlyList<double>),
        typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels),
        typeof(IReadOnlyList<string>),
        typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent),
        typeof(Brush),
        typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush),
        typeof(Brush),
        typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush),
        typeof(Brush),
        typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Series
    {
        get => (IReadOnlyList<double>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public IReadOnlyList<string>? Labels
    {
        get => (IReadOnlyList<string>?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    private const double PaddingLeft = 62;
    private const double PaddingRight = 18;
    private const double PaddingTop = 16;
    private const double PaddingBottom = 30;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 220 : availableSize.Height;

        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;

        if (width <= 4 || height <= 4)
            return;

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var plotLeft = PaddingLeft;
        var plotRight = width - PaddingRight;
        var plotTop = PaddingTop;
        var plotBottom = height - PaddingBottom;

        var values = Series;

        if (values == null || values.Count == 0)
        {
            DrawText(
                drawingContext,
                Localization.T("growth_no_data"),
                new Point(plotLeft, (plotTop + plotBottom) / 2 - 8),
                13,
                LabelBrush,
                pixelsPerDip);

            return;
        }

        var minimum = values.Min();
        var maximum = values.Max();

        if (Math.Abs(maximum - minimum) < 0.0001)
        {
            // A flat line still deserves a sensible band.
            minimum -= 1;
            maximum += 1;
        }
        else
        {
            var margin = (maximum - minimum) * 0.12;
            minimum -= margin;
            maximum += margin;
        }

        var range = maximum - minimum;

        // ---- horizontal grid + value labels -------------------------------
        const int gridLines = 4;
        var gridPen = new Pen(GridBrush, 1);

        for (var i = 0; i <= gridLines; i++)
        {
            var ratio = i / (double)gridLines;
            var y = plotBottom - ratio * (plotBottom - plotTop);

            drawingContext.DrawLine(gridPen, new Point(plotLeft, y), new Point(plotRight, y));

            var label = (minimum + ratio * range).ToString("N0", CultureInfo.CurrentCulture);

            DrawText(
                drawingContext,
                label,
                new Point(6, y - 8),
                11,
                LabelBrush,
                pixelsPerDip);
        }

        // ---- the series ---------------------------------------------------
        var points = new List<Point>(values.Count);
        var step = values.Count > 1 ? (plotRight - plotLeft) / (values.Count - 1) : 0;

        for (var i = 0; i < values.Count; i++)
        {
            var x = plotLeft + i * step;
            var y = plotBottom - (values[i] - minimum) / range * (plotBottom - plotTop);

            points.Add(new Point(x, y));
        }

        var accentPen = new Pen(Accent, 2.2)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        if (points.Count > 1)
        {
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], false, false);
                context.PolyLineTo(points.Skip(1).ToList(), true, false);
            }

            geometry.Freeze();

            drawingContext.DrawGeometry(null, accentPen, geometry);
        }

        foreach (var point in points)
        {
            drawingContext.DrawEllipse(Accent, null, point, 3, 3);
        }

        // ---- a few date labels along the bottom ---------------------------
        var labels = Labels;

        if (labels == null || labels.Count != values.Count)
            return;

        foreach (var index in LabelIndexes(values.Count))
        {
            var x = plotLeft + index * step;
            var text = labels[index];

            var formatted = MakeText(text, 11, LabelBrush, pixelsPerDip);

            drawingContext.DrawText(
                formatted,
                new Point(
                    Math.Max(2, Math.Min(x - formatted.Width / 2, width - formatted.Width - 2)),
                    plotBottom + 8));
        }
    }

    /// <summary>Keeps the x-axis readable: first, middle and last point only.</summary>
    private static IEnumerable<int> LabelIndexes(int count)
    {
        if (count <= 1)
        {
            yield return 0;
            yield break;
        }

        yield return 0;

        if (count > 2)
            yield return count / 2;

        yield return count - 1;
    }

    private static void DrawText(
        DrawingContext drawingContext,
        string text,
        Point origin,
        double size,
        Brush brush,
        double pixelsPerDip) =>
        drawingContext.DrawText(MakeText(text, size, brush, pixelsPerDip), origin);

    private static FormattedText MakeText(string text, double size, Brush brush, double pixelsPerDip) =>
        new(
            text ?? "",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            pixelsPerDip);
}
