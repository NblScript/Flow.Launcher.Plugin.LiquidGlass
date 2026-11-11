using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.LiquidGlass
{
    /// <summary>纯代码构建的设置面板（不依赖 XAML 编译）。</summary>
    internal sealed class SettingsPanel : UserControl
    {
        private readonly Main _main;
        private readonly Settings _s;

        public SettingsPanel(Main main)
        {
            _main = main;
            _s = main.Settings;
            L.Refresh();

            var root = new StackPanel { Margin = new Thickness(70, 14, 18, 14) };

            var enabled = new CheckBox { Content = L.T("Enable Liquid Glass (switches to the \"Liquid Glass\" theme)", "启用液态玻璃（会切换到「Liquid Glass」主题）"), IsChecked = _s.Enabled, Margin = new Thickness(0, 0, 0, 12) };
            enabled.Checked += (_, _) => { _s.Enabled = true; _main.ResetFailure(); Changed(); };
            enabled.Unchecked += (_, _) => { _s.Enabled = false; Changed(); };
            root.Children.Add(enabled);

            root.Children.Add(Combo(L.T("Mode", "模式"), new[]
            {
                L.T("Refraction (background capture + shader, Windows 10 2004+)", "自绘折射（抓取背景 + 着色器，Win10 2004+）"),
                L.T("System acrylic (Windows 11 DWM blur + glass highlight)", "系统亚克力（Win11 DWM 模糊 + 玻璃高光）")
            }, (int)_s.Mode, i => _s.Mode = (GlassMode)i));

            root.Children.Add(Combo(L.T("Appearance", "外观"), new[] { L.T("Follow Flow", "跟随 Flow"), L.T("Light", "浅色"), L.T("Dark", "深色") },
                (int)_s.Appearance, i => _s.Appearance = (GlassAppearance)i));

            root.Children.Add(Header(L.T("Shape", "形状")));
            root.Children.Add(SliderRow(L.T("Corner radius", "圆角"), 0, 40, _s.CornerRadius, 1, v => _s.CornerRadius = v));

            root.Children.Add(Header(L.T("Glass (refraction mode only)", "玻璃（仅自绘折射模式）")));
            root.Children.Add(SliderRow(L.T("Background blur", "背景模糊"), 0, 40, _s.BlurRadius, 1, v => _s.BlurRadius = v));
            root.Children.Add(SliderRow(L.T("Refraction strength", "折射强度"), -60, 80, _s.Refraction, 1, v => _s.Refraction = v));
            root.Children.Add(SliderRow(L.T("Refraction edge width", "折射边缘宽度"), 4, 80, _s.EdgeWidth, 1, v => _s.EdgeWidth = v));
            root.Children.Add(SliderRow(L.T("Dispersion", "色散"), 0, 1, _s.Chroma, 0.05, v => _s.Chroma = v));
            root.Children.Add(SliderRow(L.T("Tint", "着色"), 0, 0.9, _s.TintAmount, 0.02, v => _s.TintAmount = v));
            root.Children.Add(SliderRow(L.T("Saturation", "饱和度"), 0.5, 2.0, _s.Saturation, 0.05, v => _s.Saturation = v));

            root.Children.Add(Header(L.T("Lighting", "光照")));
            root.Children.Add(SliderRow(L.T("Highlight strength", "高光强度"), 0, 2, _s.Highlight, 0.05, v => _s.Highlight = v));
            root.Children.Add(SliderRow(L.T("Light angle", "光源角度"), 0, 360, _s.LightAngle, 5, v => _s.LightAngle = v));

            root.Children.Add(Header(L.T("Performance", "性能")));
            root.Children.Add(SliderRow(L.T("Live refresh interval, ms (0 = off)", "实时刷新间隔 ms（0=关闭）"), 0, 1000, _s.LiveRefreshMs, 50, v => _s.LiveRefreshMs = (int)v));

            root.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 14, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Text = L.T("Note: refraction mode excludes the Flow window from screen capture so the plugin can capture what is behind it. " +
                           "As a side effect, Flow does not appear in screenshots, recordings or screen sharing. Use system acrylic mode if that matters to you.",
                           "说明：自绘折射模式会把 Flow 窗口设为「不被截屏/录屏捕获」，这样插件才能抓到窗口背后的画面。" +
                           "副作用是截图和屏幕共享里看不到 Flow 窗口；介意的话请改用系统亚克力模式。")
            });

            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private void Changed() => _main.ScheduleApply();

        private static TextBlock Header(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 6)
        };

        private FrameworkElement Combo(string label, string[] items, int selected, Action<int> set)
        {
            var g = Row(label);
            var cb = new ComboBox { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var it in items) cb.Items.Add(it);
            cb.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
            cb.SelectionChanged += (_, _) => { set(cb.SelectedIndex); Changed(); };
            Grid.SetColumn(cb, 1);
            g.Children.Add(cb);
            return g;
        }

        private FrameworkElement SliderRow(string label, double min, double max, double value, double step, Action<double> set)
        {
            var g = Row(label);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(value, min, max),
                SmallChange = step,
                LargeChange = step * 5,
                TickFrequency = step,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            var valueText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Text = Format(slider.Value, step)
            };
            slider.ValueChanged += (_, e) =>
            {
                valueText.Text = Format(e.NewValue, step);
                set(e.NewValue);
                Changed();
            };

            Grid.SetColumn(slider, 1);
            Grid.SetColumn(valueText, 2);
            g.Children.Add(slider);
            g.Children.Add(valueText);
            return g;
        }

        private static Grid Row(string label)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(tb);
            return g;
        }

        private static string Format(double v, double step) =>
            v.ToString(step < 1 ? "0.00" : "0", CultureInfo.InvariantCulture);
    }
}
