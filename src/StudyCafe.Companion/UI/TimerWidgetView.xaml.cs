using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StudyCafe.Companion.Core;

namespace StudyCafe.Companion.UI;

public partial class TimerWidgetView : UserControl
{
    private string _currentPhase = "idle";
    private string _currentTime = "Study Cafe";
    private int _participantCount = 0;
    private bool _isPaused = false;

    public TimerWidgetView()
    {
        InitializeComponent();
        WidgetToolTip.CustomPopupPlacementCallback = CustomToolTipPlacement;
        Loaded += (_, _) => UpdateAutostartMenuState();
    }

    private CustomPopupPlacement[] CustomToolTipPlacement(Size popupSize, Size targetSize, Point offset)
    {
        double x = (targetSize.Width - popupSize.Width) / 2.0;
        double y = -popupSize.Height - 10.0;
        double fallbackY = targetSize.Height + 10.0;

        return new[]
        {
            new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(x, fallbackY), PopupPrimaryAxis.Horizontal)
        };
    }

    public double ComputeDesiredContentWidth()
    {
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(DesiredSize.Width);
    }

    public void UpdateState(string formattedTime, string phase, int participants, bool isPaused)
    {
        _currentTime = formattedTime;
        _currentPhase = phase;
        _participantCount = participants;
        _isPaused = isPaused;

        Dispatcher.Invoke(() =>
        {
            TimeText.Text = formattedTime;

            // Pick logo stroke color based on phase
            Brush phaseBrush;
            string phaseLabel;

            if (phase == "work")
            {
                phaseBrush = (Brush)FindResource("PhaseFocusBrush");
                phaseLabel = isPaused ? "Focus (Paused)" : "Focus";
            }
            else if (phase == "break")
            {
                phaseBrush = (Brush)FindResource("PhaseBreakBrush");
                phaseLabel = isPaused ? "Break (Paused)" : "Break";
            }
            else if (phase == "break_ended")
            {
                phaseBrush = (Brush)FindResource("PhaseBreakEndedBrush");
                phaseLabel = isPaused ? "Break Over (Paused)" : "Break Over";
            }
            else if (phase == "left_table")
            {
                phaseBrush = (Brush)FindResource("PhaseBreakEndedBrush");
                phaseLabel = "Left Table";
            }
            else
            {
                phaseBrush = (Brush)FindResource("PhaseIdleBrush");
                phaseLabel = "Ready";
            }

            Logo.StrokeBrush = phaseBrush;

            string personWord = participants == 1 ? "person" : "people";
            string tooltipText = phase == "idle"
                ? $"Study Cafe • Ready to focus{(participants > 0 ? $" ({participants} {personWord})" : "")}"
                : $"Study Cafe • {phaseLabel} ({formattedTime}) • {participants} {personWord} studying";

            WidgetToolTip.Content = tooltipText;
            MenuStatus.Header = $"Study Cafe • {phaseLabel} ({formattedTime})";
        });
    }

    private void OnPillMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(1.0, TimeSpan.FromMilliseconds(80));
        HoverBorder.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void OnPillMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100));
        HoverBorder.BeginAnimation(UIElement.OpacityProperty, anim);
        HoverBorder.SetResourceReference(Border.BackgroundProperty, "WidgetBackgroundHover");
        HoverBorder.SetResourceReference(Border.BorderBrushProperty, "WidgetBorderHover");
    }

    private void OnPillMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            HoverBorder.SetResourceReference(Border.BackgroundProperty, "WidgetBackgroundPressed");
            HoverBorder.SetResourceReference(Border.BorderBrushProperty, "WidgetBorderPressed");
        }
    }

    private void OnPillMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            HoverBorder.SetResourceReference(Border.BackgroundProperty, "WidgetBackgroundHover");
            HoverBorder.SetResourceReference(Border.BorderBrushProperty, "WidgetBorderHover");
            OpenStudyCafeInBrowser();
        }
    }

    private void OnOpenInBrowserClick(object sender, RoutedEventArgs e)
    {
        OpenStudyCafeInBrowser();
    }

    private static void OpenStudyCafeInBrowser()
    {
        try
        {
            var url = AppSettings.Current.WebUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = "https://thestudycafe.vercel.app";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open browser: {ex.Message}");
        }
    }

    private void UpdateAutostartMenuState()
    {
        MenuAutostart.IsChecked = AutostartHelper.IsEnabled();
    }

    private void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        bool enable = MenuAutostart.IsChecked;
        AutostartHelper.SetEnabled(enable);
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
