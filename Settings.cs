namespace Flow.Launcher.Plugin.LiquidGlass
{
    public enum GlassMode
    {
        /// <summary>自绘：抓取窗口背后画面，用着色器做折射/色散/高光（Win10 2004+）</summary>
        Refraction = 0,

        /// <summary>系统：使用 Win11 DWM 亚克力背景，插件只叠加玻璃高光（Win11）</summary>
        SystemAcrylic = 1
    }

    public enum GlassAppearance
    {
        Auto = 0,
        Light = 1,
        Dark = 2
    }

    public class Settings
    {
        /// <summary>插件是否接管主题</summary>
        public bool Enabled { get; set; } = true;

        public GlassMode Mode { get; set; } = GlassMode.Refraction;

        public GlassAppearance Appearance { get; set; } = GlassAppearance.Auto;

        /// <summary>窗口圆角（DIP）</summary>
        public double CornerRadius { get; set; } = 22;

        /// <summary>背景模糊半径（物理像素）。0 = 完全清透</summary>
        public double BlurRadius { get; set; } = 6;

        /// <summary>边缘折射强度（DIP）。负数为反向折射</summary>
        public double Refraction { get; set; } = 28;

        /// <summary>折射作用的边缘宽度（DIP）</summary>
        public double EdgeWidth { get; set; } = 26;

        /// <summary>色散强度 0..1</summary>
        public double Chroma { get; set; } = 0.35;

        /// <summary>着色强度 0..1（浅色模式着白色，深色模式着黑色）</summary>
        public double TintAmount { get; set; } = 0.22;

        /// <summary>高光强度 0..2</summary>
        public double Highlight { get; set; } = 1.0;

        /// <summary>光源角度（度，0 = 右，90 = 上）</summary>
        public double LightAngle { get; set; } = 120;

        /// <summary>饱和度倍数</summary>
        public double Saturation { get; set; } = 1.35;

        /// <summary>窗口显示期间背景实时刷新间隔（毫秒），0 = 只在显示/移动时抓取</summary>
        public int LiveRefreshMs { get; set; } = 0;

        /// <summary>切走前用户使用的主题，关闭插件时恢复</summary>
        public string PreviousTheme { get; set; } = "";
    }
}
