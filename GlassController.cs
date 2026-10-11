using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Flow.Launcher.Plugin.LiquidGlass.Capture;
using Flow.Launcher.Plugin.LiquidGlass.Effects;
using Flow.Launcher.Plugin.LiquidGlass.Interop;

namespace Flow.Launcher.Plugin.LiquidGlass
{
    /// <summary>
    /// 把玻璃层注入 Flow 主窗口：
    ///   Window.Content = Grid { 玻璃层(Rectangle + 着色器), 原来的 WindowBorder }
    /// 原有的命名元素、绑定、事件都保持不变，只是多了一层父容器。
    /// </summary>
    internal sealed class GlassController : IDisposable
    {
        private const string ClassName = nameof(GlassController);

        private readonly IPublicAPI _api;
        private readonly Func<Settings> _settings;

        private Window _window;
        private Border _border;
        private Grid _host;
        private Grid _glassLayer;
        private Rectangle _glassRect;
        private LiquidGlassEffect _effect;
        private readonly ImageBrush _backdrop = new() { Stretch = Stretch.Fill };

        // 着色器不可用时的简易兜底
        private Rectangle _fallbackTint;
        private Border _fallbackRim;
        private bool _useShader;

        private object _savedBorderBackground;
        private bool _attached;
        private GlassMode _attachedMode;
        private bool _excludedFromCapture;

        private RECT _captureRect;
        private DispatcherTimer _liveTimer;
        private int _asyncCaptureBusy;

        public bool IsAttached => _attached;
        public string LastError { get; private set; }

        public GlassController(IPublicAPI api, Func<Settings> settings)
        {
            _api = api;
            _settings = settings;
        }

        #region Attach / Detach

        public void Attach()
        {
            var s = _settings();
            if (_attached && _attachedMode == s.Mode)
            {
                ApplyVisualSettings();
                return;
            }
            if (_attached) Detach();

            _window = Application.Current?.MainWindow;
            if (_window == null) { LastError = "MainWindow not ready"; return; }

            if (_window.Content is not Border border)
            {
                LastError = "Unexpected main window layout (content is not a Border)";
                _api.LogWarn(ClassName, LastError);
                return;
            }
            _border = border;

            _useShader = LiquidGlassEffect.TryEnsureShader(out var shaderError);
            if (!_useShader)
            {
                LastError = "着色器不可用，已使用简化效果：" + shaderError;
                _api.LogWarn(ClassName, LastError);
            }
            else
            {
                LastError = null;
            }

            BuildGlassLayer(s.Mode);

            // 重新挂接内容
            _window.Content = null;
            _host = new Grid();
            _host.Children.Add(_glassLayer);
            _host.Children.Add(_border);
            _window.Content = _host;

            // 玻璃层跟随 Border 的 Margin（Flow 开启阴影时会加 Margin）
            BindingOperations.SetBinding(_glassLayer, FrameworkElement.MarginProperty,
                new Binding(nameof(FrameworkElement.Margin)) { Source = _border });

            if (s.Mode == GlassMode.Refraction)
            {
                // Border 本身透明，底色由玻璃层提供
                _savedBorderBackground = _border.ReadLocalValue(Border.BackgroundProperty);
                _border.Background = Brushes.Transparent;
                SetExcludeFromCapture(true);

                if (!_excludedFromCapture)
                {
                    // 抓不到背景：退回主题自带的不透明底色，只保留高光边，保证文字可读
                    RestoreBorderBackground();
                    if (_effect != null) _effect.UseBackdrop = 0.0;
                    else _glassRect.Fill = null;
                    if (_fallbackTint != null) _fallbackTint.Visibility = Visibility.Collapsed;
                }
            }

            _border.SizeChanged += OnBorderSizeChanged;
            _window.LocationChanged += OnWindowMoved;
            _window.IsVisibleChanged += OnWindowIsVisibleChanged;
            _window.DpiChanged += OnDpiChanged;

            _attached = true;
            _attachedMode = s.Mode;

            ApplyVisualSettings();
            if (_window.IsVisible) OnShown();

            _api.LogInfo(ClassName, $"Liquid glass attached (mode={s.Mode}, shader={_useShader})");
        }

        public void Detach()
        {
            if (!_attached) return;
            StopLiveTimer();

            _border.SizeChanged -= OnBorderSizeChanged;
            _window.LocationChanged -= OnWindowMoved;
            _window.IsVisibleChanged -= OnWindowIsVisibleChanged;
            _window.DpiChanged -= OnDpiChanged;

            BindingOperations.ClearBinding(_glassLayer, FrameworkElement.MarginProperty);

            _window.Content = null;
            _host.Children.Clear();
            _window.Content = _border;

            if (_attachedMode == GlassMode.Refraction) RestoreBorderBackground();
            SetExcludeFromCapture(false);

            _backdrop.ImageSource = null;
            _host = null;
            _glassLayer = null;
            _glassRect = null;
            _effect = null;
            _fallbackTint = null;
            _fallbackRim = null;
            _attached = false;

            _api.LogInfo(ClassName, "Liquid glass detached");
        }

        private void RestoreBorderBackground()
        {
            if (_savedBorderBackground == null) return;
            if (_savedBorderBackground == DependencyProperty.UnsetValue)
                _border.ClearValue(Border.BackgroundProperty);
            else
                _border.SetValue(Border.BackgroundProperty, _savedBorderBackground);
            _savedBorderBackground = null;
        }

        private void BuildGlassLayer(GlassMode mode)
        {
            _glassLayer = new Grid { IsHitTestVisible = false, SnapsToDevicePixels = true };

            _glassRect = new Rectangle
            {
                // 几乎透明：万一效果没渲染出来，也不会出现一个白块
                Fill = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
                IsHitTestVisible = false
            };

            if (_useShader)
            {
                _effect = new LiquidGlassEffect
                {
                    Backdrop = _backdrop,
                    UseBackdrop = mode == GlassMode.Refraction ? 1.0 : 0.0
                };
                _glassRect.Effect = _effect;
                _glassLayer.Children.Add(_glassRect);
            }
            else if (mode == GlassMode.Refraction)
            {
                // 兜底：模糊背景 + 着色 + 渐变亮边，没有折射
                _glassRect.Fill = _backdrop;
                _fallbackTint = new Rectangle { IsHitTestVisible = false };
                _fallbackRim = new Border
                {
                    IsHitTestVisible = false,
                    BorderThickness = new Thickness(1.2),
                    BorderBrush = new LinearGradientBrush(
                        Color.FromArgb(200, 255, 255, 255), Color.FromArgb(40, 255, 255, 255), 60)
                };
                _glassLayer.Children.Add(_glassRect);
                _glassLayer.Children.Add(_fallbackTint);
                _glassLayer.Children.Add(_fallbackRim);
            }
            else
            {
                _fallbackRim = new Border
                {
                    IsHitTestVisible = false,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new LinearGradientBrush(
                        Color.FromArgb(160, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 60)
                };
                _glassLayer.Children.Add(_fallbackRim);
            }
        }

        #endregion

        #region Visual parameters

        public void ApplyVisualSettings()
        {
            if (!_attached) return;
            var s = _settings();
            bool dark = Main.ResolveDark(_api, s);
            double dpi = GetDpiScale();

            // Win11 的 DWM 圆角固定约 8px；系统模式下高光要与之对齐
            double radiusDip = s.Mode == GlassMode.SystemAcrylic ? 8 : s.CornerRadius;

            _glassRect.RadiusX = _glassRect.RadiusY = radiusDip;
            if (_fallbackRim != null) _fallbackRim.CornerRadius = new CornerRadius(radiusDip);

            if (_effect != null)
            {
                _effect.Radius = radiusDip * dpi;
                _effect.Refraction = s.Refraction * dpi;
                _effect.EdgeWidth = Math.Max(1, s.EdgeWidth) * dpi;
                _effect.Chroma = s.Chroma;
                _effect.Tint = dark ? Color.FromRgb(8, 8, 12) : Colors.White;
                _effect.TintAmount = s.TintAmount;
                _effect.Highlight = s.Highlight * (dark ? 0.85 : 1.0);
                _effect.LightAngle = s.LightAngle * Math.PI / 180.0;
                _effect.Saturation = s.Saturation;
                _effect.Brightness = dark ? 0.82 : 1.06;
            }

            if (_fallbackTint != null)
            {
                _fallbackTint.RadiusX = _fallbackTint.RadiusY = radiusDip;
                var c = dark ? Color.FromRgb(8, 8, 12) : Colors.White;
                c.A = (byte)Math.Clamp(s.TintAmount * 255 + 40, 0, 255);
                _fallbackTint.Fill = new SolidColorBrush(c);
            }

            if (s.Mode == GlassMode.Refraction)
            {
                if (s.LiveRefreshMs > 0 && _window.IsVisible) StartLiveTimer(); else StopLiveTimer();
                if (_window.IsVisible) CaptureNow();
            }
        }

        #endregion

        #region Capture

        public void OnShown()
        {
            if (!_attached || _attachedMode != GlassMode.Refraction) return;
            // 同步抓取：此时还在 UI 线程上、首帧尚未渲染，所以第一帧就是新的背景
            CaptureNow();
            if (_settings().LiveRefreshMs > 0) StartLiveTimer();
        }

        public void OnHidden()
        {
            StopLiveTimer();
        }

        private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) OnShown(); else OnHidden();
        }

        private void OnBorderSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMapping(recaptureIfOutside: true);

        private void OnWindowMoved(object sender, EventArgs e) => UpdateMapping(recaptureIfOutside: true);

        private void OnDpiChanged(object sender, DpiChangedEventArgs e)
        {
            ApplyVisualSettings();
        }

        private bool TryGetBorderScreenRect(out RECT rect)
        {
            rect = default;
            if (_border == null || PresentationSource.FromVisual(_border) == null) return false;
            if (_border.ActualWidth < 1 || _border.ActualHeight < 1) return false;
            try
            {
                var tl = _border.PointToScreen(new Point(0, 0));
                var br = _border.PointToScreen(new Point(_border.ActualWidth, _border.ActualHeight));
                rect = new RECT((int)Math.Floor(tl.X), (int)Math.Floor(tl.Y),
                                (int)Math.Ceiling(br.X), (int)Math.Ceiling(br.Y));
                return !rect.IsEmpty;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private RECT ComputeCaptureRect(RECT b)
        {
            var s = _settings();
            double dpi = GetDpiScale();
            int pad = (int)Math.Ceiling(Math.Abs(s.Refraction) * dpi + s.BlurRadius * 3 + 8);
            var mon = Native.GetMonitorRect((b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2);

            // 一直抓到显示器底部：结果列表展开（窗口变高）时只需更新映射，不用重新抓
            // 尽量不越出当前显示器，但始终完整包含窗口本身
            return new RECT(
                Math.Min(b.Left, Math.Max(mon.Left, b.Left - pad)),
                Math.Min(b.Top, Math.Max(mon.Top, b.Top - pad)),
                Math.Max(b.Right, Math.Min(mon.Right, b.Right + pad)),
                Math.Max(b.Bottom, mon.Bottom));
        }

        public void CaptureNow()
        {
            if (!_attached || _attachedMode != GlassMode.Refraction) return;
            if (!_excludedFromCapture) return; // 无法排除自身时不抓，避免把自己抓进去
            if (!TryGetBorderScreenRect(out var b)) return;

            var c = ComputeCaptureRect(b);
            try
            {
                var bmp = BackdropCapture.Capture(c, _settings().BlurRadius);
                if (bmp == null) return;
                _backdrop.ImageSource = bmp;
                _captureRect = c;
                UpdateMapping(recaptureIfOutside: false);
            }
            catch (Exception e)
            {
                _api.LogException(ClassName, "Capture failed", e);
            }
        }

        private void CaptureAsync()
        {
            if (!_attached || _attachedMode != GlassMode.Refraction || !_excludedFromCapture) return;
            if (!TryGetBorderScreenRect(out var b)) return;
            if (Interlocked.Exchange(ref _asyncCaptureBusy, 1) == 1) return;

            var c = ComputeCaptureRect(b);
            var blur = _settings().BlurRadius;
            var dispatcher = _window.Dispatcher;
            Task.Run(() =>
            {
                try
                {
                    var bmp = BackdropCapture.Capture(c, blur);
                    if (bmp != null)
                    {
                        dispatcher.InvokeAsync(() =>
                        {
                            if (!_attached) return;
                            _backdrop.ImageSource = bmp;
                            _captureRect = c;
                            UpdateMapping(recaptureIfOutside: false);
                        });
                    }
                }
                catch (Exception e)
                {
                    _api.LogException(ClassName, "Async capture failed", e);
                }
                finally
                {
                    Interlocked.Exchange(ref _asyncCaptureBusy, 0);
                }
            });
        }

        /// <summary>
        /// 计算窗口在背景纹理中的位置，传给着色器（无需重新抓屏）。
        /// </summary>
        private void UpdateMapping(bool recaptureIfOutside)
        {
            if (!_attached || _attachedMode != GlassMode.Refraction) return;
            if (!TryGetBorderScreenRect(out var b)) return;

            var c = _captureRect;
            if (c.IsEmpty || !c.Contains(b))
            {
                if (recaptureIfOutside && _window.IsVisible) CaptureNow();
                return;
            }

            double cw = c.Width, ch = c.Height;
            var lt = new Point((b.Left - c.Left) / cw, (b.Top - c.Top) / ch);
            var rb = new Point((c.Right - b.Right) / cw, (c.Bottom - b.Bottom) / ch);

            if (_effect != null)
            {
                _effect.PadLT = lt;
                _effect.PadRB = rb;
            }
            else
            {
                _backdrop.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
                _backdrop.Viewbox = new Rect(lt.X, lt.Y, Math.Max(0.001, 1 - lt.X - rb.X), Math.Max(0.001, 1 - lt.Y - rb.Y));
            }
        }

        private void StartLiveTimer()
        {
            var ms = _settings().LiveRefreshMs;
            if (ms <= 0) { StopLiveTimer(); return; }
            ms = Math.Max(33, ms);
            if (_liveTimer == null)
            {
                _liveTimer = new DispatcherTimer(DispatcherPriority.Background);
                _liveTimer.Tick += (_, _) => CaptureAsync();
            }
            _liveTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _liveTimer.Start();
        }

        private void StopLiveTimer() => _liveTimer?.Stop();

        private void SetExcludeFromCapture(bool exclude)
        {
            if (_window == null) return;
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) { _excludedFromCapture = false; return; }

            if (exclude)
            {
                _excludedFromCapture = Native.SetWindowDisplayAffinity(hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
                if (!_excludedFromCapture)
                {
                    LastError = "SetWindowDisplayAffinity 失败（需要 Windows 10 2004 或更高版本），折射背景已禁用，可在设置里改用「系统亚克力」模式";
                    _api.LogWarn(ClassName, LastError);
                }
            }
            else if (_excludedFromCapture)
            {
                Native.SetWindowDisplayAffinity(hwnd, Native.WDA_NONE);
                _excludedFromCapture = false;
            }
        }

        private double GetDpiScale()
        {
            if (_window == null) return 1.0;
            return VisualTreeHelper.GetDpi(_window).DpiScaleX;
        }

        #endregion

        public void Dispose()
        {
            try { Detach(); } catch { /* shutting down */ }
        }
    }
}
