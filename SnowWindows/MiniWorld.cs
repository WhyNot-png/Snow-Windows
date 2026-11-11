using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace SnowWindows;

// A decorative preview inside the panel only; not a live simulation or wallpaper.
internal sealed class MiniWorld : Border
{
    private readonly Settings settings;
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly List<TranslateTransform> animated = [];
    private string currentKey = "";
    private Canvas? scene;
    private static readonly ImageSource Plow = LoadPlow();
    public MiniWorld(Settings settings)
    {
        this.settings = settings;
        Height = 76; Margin = new Thickness(0, 0, 0, 8); CornerRadius = new CornerRadius(10);
        BorderBrush = ColorBrush("#486883"); BorderThickness = new Thickness(1);
        IsHitTestVisible = false;
        Loaded += (_, _) => { clock.Start(); Refresh(true); };
        Unloaded += (_, _) => { clock.Stop(); StopAnimations(); };
        SizeChanged += (_, _) => Refresh();
        clock.Tick += (_, _) => Refresh();
    }
    private static ImageSource LoadPlow()
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/SnowWindows;component/Assets/snowplow.png")); image.Freeze(); return image;
    }
    private static Brush ColorBrush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private void StopAnimations()
    {
        foreach (var t in animated) { t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null); }
        animated.Clear();
    }
    public void Refresh(bool force = false)
    {
        if (!IsLoaded || ActualWidth < 60) return;
        double w = Math.Max(1, ActualWidth - 2), h = 74;
        bool night = WorldClock.IsNight(DateTime.Now);
        bool motion = settings.UiAnimations && Native.AllowInterfaceAnimations();
        bool snow = settings.EffectiveEnabled && settings.FallingSnow;
        bool plow = settings.EffectiveEnabled && settings.Snowdrifts && settings.Snowplow;
        string key = $"{(int)w}|{night}|{motion}|{snow}|{settings.Snowdrifts}|{plow}";
        if (!force && key == currentKey) return;
        currentKey = key; StopAnimations();
        scene = new Canvas { Width = w, Height = h, Clip = new RectangleGeometry(new Rect(0, 0, w, h), 9, 9) };
        scene.Background = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(night ? "#142846" : "#A7D5F5"),
            (Color)ColorConverter.ConvertFromString(night ? "#3D6687" : "#E4F4FF"), 90);
        Child = scene;
        var random = new Random(771);
        if (night)
        {
            for (int i = 0; i < 14; i++)
            {
                double size = i % 4 == 0 ? 2.5 : 1.5;
                var star = new Ellipse { Width = size, Height = size, Fill = ColorBrush("#E8F5FF"), Opacity = 0.60 + random.NextDouble() * 0.35 };
                Canvas.SetLeft(star, 10 + random.NextDouble() * (w - 20)); Canvas.SetTop(star, 5 + random.NextDouble() * 38); scene.Children.Add(star);
            }
            var moonShape = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Point(12, 12), 11, 11), new EllipseGeometry(new Point(18, 7), 9, 9));
            var moon = new Path { Data = moonShape, Fill = ColorBrush("#FFF4CA"), Width = 25, Height = 25, Stretch = Stretch.None };
            Canvas.SetLeft(moon, w - 41); Canvas.SetTop(moon, 9); scene.Children.Add(moon);
        }
        else
        {
            var sun = new Ellipse { Width = 22, Height = 22, Fill = ColorBrush("#FFF5CC"), Opacity = 0.95 };
            Canvas.SetLeft(sun, w - 42); Canvas.SetTop(sun, 9); scene.Children.Add(sun);
            var cloud = new Path { Data = Geometry.Parse("M 0,10 C 1,6 5,5 8,6 C 10,0 18,0 21,6 C 26,5 30,7 30,11 C 30,14 27,15 23,15 L 6,15 C 2,15 0,13 0,10 Z"), Fill = ColorBrush("#F7FCFF"), Opacity = .75 };
            Canvas.SetLeft(cloud, 36); Canvas.SetTop(cloud, 10); scene.Children.Add(cloud);
        }
        if (settings.Snowdrifts)
        {
            var hill = new StreamGeometry();
            using (var c = hill.Open())
            {
                c.BeginFigure(new Point(0, h - 12), true, true);
                c.BezierTo(new Point(w * .12, h - 22), new Point(w * .22, h - 9), new Point(w * .36, h - 14), true, false);
                c.BezierTo(new Point(w * .50, h - 22), new Point(w * .60, h - 10), new Point(w * .72, h - 15), true, false);
                c.BezierTo(new Point(w * .84, h - 20), new Point(w * .92, h - 9), new Point(w, h - 13), true, false);
                c.LineTo(new Point(w, h), true, false); c.LineTo(new Point(0, h), true, false);
            }
            hill.Freeze(); scene.Children.Add(new Path { Data = hill, Fill = ColorBrush("#EAF5FF") });
        }
        if (snow)
        {
            for (int i = 0; i < 8; i++)
            {
                double size = 2 + random.NextDouble() * 2;
                var flake = new Ellipse { Width = size, Height = size, Fill = night ? ColorBrush("#F8FCFF") : ColorBrush("#FFFFFF"), Opacity = .80 };
                Canvas.SetLeft(flake, 8 + random.NextDouble() * (w - 20)); Canvas.SetTop(flake, 3 + random.NextDouble() * 35);
                if (motion)
                {
                    var transform = new TranslateTransform(); flake.RenderTransform = transform; animated.Add(transform);
                    transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 26, TimeSpan.FromSeconds(3.5 + i * .25)) { RepeatBehavior = RepeatBehavior.Forever });
                }
                scene.Children.Add(flake);
            }
        }
        if (plow)
        {
            var car = new Image { Source = Plow, Width = 45, Height = 23, Stretch = Stretch.Uniform };
            Canvas.SetTop(car, h - 28);
            if (motion)
            {
                var move = new TranslateTransform(); car.RenderTransform = move; animated.Add(move);
                var travel = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                travel.KeyFrames.Add(new LinearDoubleKeyFrame(-48, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                travel.KeyFrames.Add(new LinearDoubleKeyFrame(w + 5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(9))));
                travel.KeyFrames.Add(new LinearDoubleKeyFrame(w + 5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(15))));
                move.BeginAnimation(TranslateTransform.XProperty, travel);
            }
            else Canvas.SetLeft(car, w * .18);
            scene.Children.Add(car);
        }
        var caption = new TextBlock { Text = night ? "НОЧЬ" : "ДЕНЬ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = ColorBrush(night ? "#D7EFFF" : "#244B6D"), Opacity = .95 };
        Canvas.SetLeft(caption, 10); Canvas.SetTop(caption, 7); scene.Children.Add(caption);
    }
}
