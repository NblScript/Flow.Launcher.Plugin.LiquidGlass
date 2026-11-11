using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Flow.Launcher.Plugin.SharedModels;

namespace Flow.Launcher.Plugin.LiquidGlass
{
    public class Main : IPlugin, ISettingProvider, IDisposable
    {
        private const string ClassName = nameof(Main);
        private const string Icon = "Images\\icon.png";

        private PluginInitContext _context;
        private IPublicAPI _api;
        private Settings _settings;
        private GlassController _glass;
        private DispatcherTimer _applyDebounce;

        internal Settings Settings => _settings;

        public void Init(PluginInitContext context)
        {
            _context = context;
            _api = context.API;
            _settings = _api.LoadSettingJsonStorage<Settings>();
            _glass = new GlassController(_api, () => _settings);

            _api.VisibilityChanged += OnVisibilityChanged;
            _api.ActualApplicationThemeChanged += OnAppThemeChanged;

            // Flow 在线程池上调用 Init；所有 WPF 对象都必须在 UI 线程上创建和使用。
            // 等主窗口和主题系统就绪后再接管
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ApplyAll));
        }

        internal static bool ResolveDark(IPublicAPI api, Settings s) => s.Appearance switch
        {
            GlassAppearance.Dark => true,
            GlassAppearance.Light => false,
            _ => api.IsApplicationDarkTheme()
        };

        /// <summary>按当前设置写主题、切主题、挂玻璃层。必须在 UI 线程调用。</summary>
        internal void ApplyAll()
        {
            L.Refresh();
            try
            {
                if (!_settings.Enabled)
                {
                    _glass.Detach();
                    if (IsOurThemeActive()) RestorePreviousTheme();
                    return;
                }

                bool dark = ResolveDark(_api, _settings);
                bool fileChanged = ThemeWriter.Write(_api, _settings, dark);

                var current = _api.GetCurrentTheme();
                bool active = IsOurTheme(current);
                if (!active || fileChanged)
                {
                    var ours = _api.GetAvailableThemes()
                        .FirstOrDefault(t => string.Equals(t.FileNameWithoutExtension, ThemeWriter.ThemeFileName, StringComparison.OrdinalIgnoreCase));
                    if (ours == null)
                    {
                        _api.LogError(ClassName, "Generated theme not found in theme list: " + ThemeWriter.GetThemePath(_api));
                        return;
                    }
                    if (!active && current != null)
                    {
                        _settings.PreviousTheme = current.FileNameWithoutExtension;
                        _api.SaveSettingJsonStorage<Settings>();
                    }
                    _api.SetCurrentTheme(ours);
                }

                _glass.Attach();
            }
            catch (Exception e)
            {
                _api.LogException(ClassName, "Failed to apply liquid glass", e);
                _glass.Fail(e);
            }
        }

        /// <summary>
        /// 在 UI 线程上运行，并吞掉异常：插件里的任何错误都不能让 Flow 崩溃。
        /// 出错时卸下玻璃层，Flow 保持可用。
        /// </summary>
        private void RunOnUi(Action action, string what)
        {
            void Guarded()
            {
                try { action(); }
                catch (Exception e)
                {
                    _api.LogException(ClassName, what + " failed", e);
                    _glass.Fail(e);
                }
            }

            var d = Application.Current?.Dispatcher;
            if (d == null) return;
            if (d.CheckAccess()) Guarded();
            else d.Invoke(Guarded);
        }

        private bool IsOurThemeActive() => IsOurTheme(_api.GetCurrentTheme());

        private static bool IsOurTheme(ThemeData t) =>
            t != null && string.Equals(t.FileNameWithoutExtension, ThemeWriter.ThemeFileName, StringComparison.OrdinalIgnoreCase);

        private void RestorePreviousTheme()
        {
            var themes = _api.GetAvailableThemes();
            var prev = themes.FirstOrDefault(t => t.FileNameWithoutExtension == _settings.PreviousTheme)
                       ?? themes.FirstOrDefault(t => t.FileNameWithoutExtension == "Win11Light")
                       ?? themes.FirstOrDefault(t => !IsOurTheme(t));
            if (prev != null) _api.SetCurrentTheme(prev);
        }

        internal void ResetFailure() => _glass.ResetFailure();

        /// <summary>设置面板里每次改动都走这里，做个防抖</summary>
        internal void ScheduleApply()
        {
            _applyDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background,
                (_, _) => { _applyDebounce.Stop(); RunOnUi(() => { _api.SaveSettingJsonStorage<Settings>(); ApplyAll(); }, "Apply settings"); },
                Application.Current.Dispatcher);
            _applyDebounce.Stop();
            _applyDebounce.Start();
        }

        private void OnVisibilityChanged(object sender, VisibilityChangedEventArgs args)
        {
            RunOnUi(() =>
            {
                if (!args.IsVisible) { _glass.OnHidden(); return; }
                if (!_settings.Enabled) return;

                if (!IsOurThemeActive())
                {
                    // 用户在 Flow 设置里换了别的主题：尊重选择，自动关闭插件效果
                    _glass.Detach();
                    _settings.Enabled = false;
                    _api.SaveSettingJsonStorage<Settings>();
                    return;
                }

                if (!_glass.IsAttached) { if (!_glass.HasFailed) _glass.Attach(); }
                else _glass.OnShown();
            }, "Window shown");
        }

        private void OnAppThemeChanged(object sender, ActualApplicationThemeChangedEventArgs args)
        {
            if (_settings.Enabled && _settings.Appearance == GlassAppearance.Auto)
                Application.Current?.Dispatcher.BeginInvoke(new Action(ApplyAll));
        }

        public List<Result> Query(Query query)
        {
            var s = _settings;
            var results = new List<Result>
            {
                new()
                {
                    Title = s.Enabled ? L.T("Liquid Glass: on", "液态玻璃：已开启") : L.T("Liquid Glass: off", "液态玻璃：已关闭"),
                    SubTitle = s.Enabled ? L.T("Press Enter to turn off and restore your previous theme", "回车关闭，恢复之前的主题") : L.T("Press Enter to turn on", "回车开启"),
                    IcoPath = Icon,
                    Score = 100,
                    Action = _ =>
                    {
                        s.Enabled = !s.Enabled;
                        _glass.ResetFailure();
                        _api.SaveSettingJsonStorage<Settings>();
                        Application.Current.Dispatcher.BeginInvoke(new Action(ApplyAll));
                        return true;
                    }
                },
                new()
                {
                    Title = L.T("Mode: ", "模式：") + (s.Mode == GlassMode.Refraction ? L.T("Refraction", "自绘折射") : L.T("System acrylic", "系统亚克力")),
                    SubTitle = s.Mode == GlassMode.Refraction
                        ? L.T("Press Enter to switch to system acrylic (Windows 11 DWM blur + glass highlight, no screen capture)", "回车切换为系统亚克力（Win11 DWM 模糊 + 玻璃高光，不抓屏）")
                        : L.T("Press Enter to switch to refraction (background capture + shader refraction/dispersion)", "回车切换为自绘折射（抓取背景 + 着色器折射/色散）"),
                    IcoPath = Icon,
                    Score = 90,
                    Action = _ =>
                    {
                        s.Mode = s.Mode == GlassMode.Refraction ? GlassMode.SystemAcrylic : GlassMode.Refraction;
                        s.Enabled = true;
                        _api.SaveSettingJsonStorage<Settings>();
                        Application.Current.Dispatcher.BeginInvoke(new Action(ApplyAll));
                        return false;
                    }
                },
                new()
                {
                    Title = L.T("Appearance: ", "外观：") + s.Appearance switch
                    {
                        GlassAppearance.Light => L.T("Light", "浅色"),
                        GlassAppearance.Dark => L.T("Dark", "深色"),
                        _ => L.T("Follow Flow", "跟随 Flow")
                    },
                    SubTitle = L.T("Press Enter to cycle Follow Flow → Light → Dark", "回车在 跟随 Flow → 浅色 → 深色 之间切换"),
                    IcoPath = Icon,
                    Score = 80,
                    Action = _ =>
                    {
                        s.Appearance = (GlassAppearance)(((int)s.Appearance + 1) % 3);
                        _api.SaveSettingJsonStorage<Settings>();
                        Application.Current.Dispatcher.BeginInvoke(new Action(ApplyAll));
                        return false;
                    }
                },
                new()
                {
                    Title = L.T("Adjust settings…", "调整参数…"),
                    SubTitle = L.T("Open Flow settings → Plugins → Liquid Glass (corner radius, blur, refraction, dispersion, highlight)", "打开 Flow 设置 → 插件 → Liquid Glass（圆角、模糊、折射、色散、高光）"),
                    IcoPath = Icon,
                    Score = 70,
                    Action = _ =>
                    {
                        _api.OpenSettingDialog();
                        return true;
                    }
                }
            };

            if (!string.IsNullOrEmpty(_glass.LastError))
            {
                results.Add(new Result
                {
                    Title = L.T("Notice", "提示"),
                    SubTitle = _glass.LastError,
                    IcoPath = Icon,
                    Score = 10
                });
            }

            var term = query.Search?.Trim();
            if (!string.IsNullOrEmpty(term))
                results = results.Where(r => r.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                                             || (r.SubTitle?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
            return results;
        }

        public Control CreateSettingPanel() => new SettingsPanel(this);

        public void Dispose()
        {
            if (_api != null)
            {
                _api.VisibilityChanged -= OnVisibilityChanged;
                _api.ActualApplicationThemeChanged -= OnAppThemeChanged;
            }
            _glass?.Dispose();
        }
    }
}
