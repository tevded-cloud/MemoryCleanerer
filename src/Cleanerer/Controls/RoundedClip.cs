using System.Windows;
using System.Windows.Media;

namespace Cleanerer.Controls;

/// <summary>
/// Attached behavior that clips an element to a rounded rectangle matching its own bounds.
/// A WPF <see cref="System.Windows.Controls.Border"/> rounds only what IT paints; children
/// (a DataGrid's header row, a ScrollBar at the edge) still render square and poke through the
/// corners. Setting <c>controls:RoundedClip.Radius="13"</c> on the child keeps it inside.
/// </summary>
public static class RoundedClip
{
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.RegisterAttached(
            "Radius",
            typeof(double),
            typeof(RoundedClip),
            new PropertyMetadata(0d, OnRadiusChanged));

    public static double GetRadius(DependencyObject obj) => (double)obj.GetValue(RadiusProperty);

    public static void SetRadius(DependencyObject obj, double value) => obj.SetValue(RadiusProperty, value);

    private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.SizeChanged -= OnSizeChanged;

        if ((double)e.NewValue > 0)
        {
            element.SizeChanged += OnSizeChanged;
            Apply(element);
        }
        else
        {
            element.Clip = null;
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);

    private static void Apply(FrameworkElement element)
    {
        double radius = GetRadius(element);
        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
    }
}
