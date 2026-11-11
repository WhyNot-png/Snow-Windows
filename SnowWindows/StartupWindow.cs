using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SnowWindows;

// An original, short intro. It is a regular, closable window, not a full-screen overlay.
internal sealed class StartupWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(2150) };
    public StartupWindow(bool animate)
    {
        Title = "Snow Windows — запуск";
        var work = SystemParameters.WorkArea;
        Width = Math.Min(560, work.Width - 24);
        Height = Math.Min(300, work.Height - 24);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = true; Topmost = true;
        FontFamily = new FontFamily("Segoe UI");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };

        var frame = new Border {
            CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1),
            BorderBrush = Brush("#8BAED7"), ClipToBounds = true,
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#102543"), (Color)ColorConverter.ConvertFromString("#1F4A78"), 50)
        };
        Content = frame;
        var grid = new Grid { ClipToBounds = true };
        frame.Child = grid;
        var snow = new Canvas { IsHitTestVisible = false, Opacity = 0.8 };
        grid.Children.Add(snow);
        var random = new Random(131);
        for (int i = 0; i < 20; i++)
        {
            double radius = 2 + random.NextDouble() * 3;
            var flake = new Ellipse { Width = radius, Height = radius, Fill = Brush("#DAEEFF"), Opacity = 0.35 + random.NextDouble() * 0.5 };
            Canvas.SetLeft(flake, 18 + random.NextDouble() * Math.Max(1, Width - 40));
            Canvas.SetTop(flake, 14 + random.NextDouble() * Math.Max(1, Height - 40));
            var fall = new TranslateTransform(); flake.RenderTransform = fall;
            snow.Children.Add(flake);
            double shift = 8 + random.NextDouble() * 20;
            if (animate) fall.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, shift, TimeSpan.FromSeconds(2.4)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20, 10, 20, 20) };
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/SnowWindows;component/Assets/snow-icon-preview.png")), Width = 82, Height = 82, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
        var iconScale = new ScaleTransform(animate ? 0.82 : 1, animate ? 0.82 : 1); icon.RenderTransform = iconScale; icon.RenderTransformOrigin = new Point(0.5, 0.5);
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = "Snow Windows", Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock { Text = "Немного зимы на рабочем столе", Foreground = Brush("#C9DDF5"), FontSize = 14, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        content.Opacity = animate ? 0 : 1; grid.Children.Add(content);
        var skip = new Button { Content = "Пропустить  ·  Esc", Foreground = Brush("#D9E8F9"), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, FontSize = 12, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 10), Padding = new Thickness(8, 7, 8, 7) };
        skip.Click += (_, _) => Close(); grid.Children.Add(skip);
        Loaded += (_, _) =>
        {
            if (animate)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease });
                iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
                iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
            }
            timer.Start();
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!animate) { Close(); return; }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fade.Completed += (_, _) => { if (IsVisible) Close(); };
            BeginAnimation(OpacityProperty, fade);
        };
        Closed += (_, _) => timer.Stop();
    }
    private static Brush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush;
    }
}
