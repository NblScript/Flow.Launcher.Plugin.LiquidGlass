using System.Globalization;

namespace Flow.Launcher.Plugin.LiquidGlass
{
    /// <summary>
    /// UI text in English, or Chinese when Flow's language is Chinese.
    /// Flow only sets the culture on its UI thread, so it is read there (see Main.ApplyAll).
    /// </summary>
    internal static class L
    {
        public static bool Zh { get; private set; } = IsZh(CultureInfo.CurrentUICulture);

        public static void Refresh() => Zh = IsZh(CultureInfo.CurrentUICulture);

        private static bool IsZh(CultureInfo c) =>
            c.TwoLetterISOLanguageName.Equals("zh", System.StringComparison.OrdinalIgnoreCase);

        public static string T(string en, string zh) => Zh ? zh : en;
    }
}
