using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StudyCafe.Companion.UI;

public partial class StudyCafeLogo : UserControl
{
    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(
            nameof(StrokeBrush),
            typeof(Brush),
            typeof(StudyCafeLogo),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE0, 0x6D, 0x53))));

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public StudyCafeLogo()
    {
        InitializeComponent();
    }
}
