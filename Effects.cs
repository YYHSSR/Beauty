using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BeautyDock;

/// <summary>临时的透明特效窗口：置顶、鼠标穿透、不抢焦点，播放完自动关闭。</summary>
internal sealed class FxWindow : Window
{
    public Canvas Canvas { get; } = new();

    public FxWindow(double left, double top, double width, double height)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Left = left; Top = top; Width = width; Height = height;
        Content = Canvas;
        SourceInitialized += (_, _) => Native.AddExStyle(new WindowInteropHelper(this).Handle,
            Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED);
    }

    public void CloseAfter(double seconds)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) => { t.Stop(); Close(); };
        t.Start();
    }

    public void Add(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        Canvas.Children.Add(e);
    }
}

internal static class Effects
{
    static readonly Random Rng = new();

    /// <summary>音爆：从图标中心连续扩散出冲击波圆环。center 为屏幕坐标（DIP）。</summary>
    public static void SonicBoom(Point center, double s, int bursts, double interval)
    {
        double size = s * 4.5;
        var fx = new FxWindow(center.X - size / 2, center.Y - size / 2, size, size);
        var dur = TimeSpan.FromSeconds(0.55);

        for (int b = 0; b < bursts; b++)
        {
            double t0 = b * interval;

            // 中心的冲击闪光
            var flash = new Ellipse
            {
                Width = size * 0.45, Height = size * 0.45, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5),
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(200, 255, 255, 255), 0),
                        new GradientStop(Color.FromArgb(90, 150, 220, 255), 0.55),
                        new GradientStop(Color.FromArgb(0, 150, 220, 255), 1),
                    },
                },
            };
            var fs = new ScaleTransform(0.3, 0.3);
            flash.RenderTransform = fs;
            fx.Add(flash, (size - flash.Width) / 2, (size - flash.Height) / 2);
            Grow(fs, 0.3, 1.1, t0, 0.3);
            Fade(flash, 0.9, t0, 0.3);

            // 三道冲击波圆环
            for (int k = 0; k < 3; k++)
            {
                var ring = new Ellipse
                {
                    Width = size * 0.95, Height = size * 0.95, Opacity = 0,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    StrokeThickness = s * (k == 0 ? 0.09 : 0.055),
                    Stroke = new SolidColorBrush(k == 0 ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(200, 140, 215, 255)),
                };
                var st = new ScaleTransform(0.12, 0.12);
                ring.RenderTransform = st;
                fx.Add(ring, size * 0.025, size * 0.025);
                double begin = t0 + k * 0.09;
                Grow(st, 0.12, 1.0, begin, dur.TotalSeconds);
                Fade(ring, 0.95, begin, dur.TotalSeconds);
            }
        }

        fx.Show();
        fx.CloseAfter((bursts - 1) * interval + 0.9);
    }

    /// <summary>爆炸：闪光 + 火花四溅 + 烟雾。center 为屏幕坐标（DIP）。</summary>
    public static void Explosion(Point center, double s)
    {
        double size = s * 6;
        var fx = new FxWindow(center.X - size / 2, center.Y - size / 2, size, size);
        double c = size / 2;

        // 烟雾
        for (int i = 0; i < 7; i++)
        {
            double d = s * (0.7 + Rng.NextDouble() * 0.5);
            var puff = new Ellipse
            {
                Width = d, Height = d, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5),
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(150, 120, 120, 125), 0),
                        new GradientStop(Color.FromArgb(0, 120, 120, 125), 1),
                    },
                },
            };
            var tg = new TransformGroup();
            var sc = new ScaleTransform(0.4, 0.4);
            var mv = new TranslateTransform();
            tg.Children.Add(sc); tg.Children.Add(mv);
            puff.RenderTransform = tg;
            fx.Add(puff, c - d / 2, c - d / 2);
            double ang = Rng.NextDouble() * Math.PI * 2, dist = s * (0.4 + Rng.NextDouble() * 0.6);
            Move(mv, Math.Cos(ang) * dist, Math.Sin(ang) * dist - s * 0.4, 0.05, 1.0);
            Grow(sc, 0.4, 1.5, 0.05, 1.0);
            Fade(puff, 0.8, 0.05, 1.0);
        }

        // 闪光
        var flash = new Ellipse
        {
            Width = size * 0.7, Height = size * 0.7, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5),
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(255, 255, 255, 235), 0),
                    new GradientStop(Color.FromArgb(220, 255, 190, 70), 0.35),
                    new GradientStop(Color.FromArgb(0, 255, 90, 20), 1),
                },
            },
        };
        var fsc = new ScaleTransform(0.2, 0.2);
        flash.RenderTransform = fsc;
        fx.Add(flash, c - flash.Width / 2, c - flash.Height / 2);
        Grow(fsc, 0.2, 1.2, 0, 0.35);
        Fade(flash, 1, 0, 0.4);

        // 火花
        var colors = new[]
        {
            Color.FromRgb(255, 246, 213), Color.FromRgb(255, 213, 74), Color.FromRgb(255, 159, 28),
            Color.FromRgb(255, 90, 31), Color.FromRgb(230, 59, 46),
        };
        for (int i = 0; i < 30; i++)
        {
            double d = s * (0.07 + Rng.NextDouble() * 0.13);
            var spark = new Ellipse { Width = d, Height = d, Fill = new SolidColorBrush(colors[Rng.Next(colors.Length)]), Opacity = 0 };
            var mv = new TranslateTransform();
            spark.RenderTransform = mv;
            fx.Add(spark, c - d / 2, c - d / 2);
            double ang = Rng.NextDouble() * Math.PI * 2, dist = size * (0.2 + Rng.NextDouble() * 0.28);
            double dur = 0.45 + Rng.NextDouble() * 0.4;
            Move(mv, Math.Cos(ang) * dist, Math.Sin(ang) * dist + s * 0.3 /* 稍微下坠 */, 0, dur);
            Fade(spark, 1, 0, dur);
        }

        fx.Show();
        fx.CloseAfter(1.2);
    }

    // ---------- 动画小工具 ----------

    static void Grow(ScaleTransform st, double from, double to, double begin, double dur)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(dur))
        {
            BeginTime = TimeSpan.FromSeconds(begin),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    static void Move(TranslateTransform t, double dx, double dy, double begin, double dur)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var span = TimeSpan.FromSeconds(dur);
        var b = TimeSpan.FromSeconds(begin);
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, span) { BeginTime = b, EasingFunction = ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, span) { BeginTime = b, EasingFunction = ease });
    }

    /// <summary>出现后逐渐淡出。开始前保持透明。</summary>
    static void Fade(UIElement e, double peak, double begin, double dur)
    {
        var a = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(begin) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Math.Min(0.05, dur * 0.2)))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.35))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur))));
        e.BeginAnimation(UIElement.OpacityProperty, a);
    }
}
