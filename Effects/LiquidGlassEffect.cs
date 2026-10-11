using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Flow.Launcher.Plugin.LiquidGlass.Effects
{
    public sealed class LiquidGlassEffect : ShaderEffect
    {
        private static PixelShader _shader;
        private static string _shaderError;

        /// <summary>
        /// 编译并缓存像素着色器。失败时返回 false（例如显卡不支持 ps_3_0 或处于软件渲染）。
        /// </summary>
        public static bool TryEnsureShader(out string error)
        {
            if (_shader != null) { error = null; return true; }
            if (_shaderError != null) { error = _shaderError; return false; }

            try
            {
                if (!RenderCapability.IsPixelShaderVersionSupported(3, 0))
                    throw new NotSupportedException("GPU/render tier does not support pixel shader 3.0");

                var bytecode = ShaderCompiler.Compile(ShaderCompiler.LoadEmbeddedSource("LiquidGlass.hlsl"));
                var ps = new PixelShader();
                ps.SetStreamSource(new MemoryStream(bytecode));
                ps.Freeze();
                _shader = ps;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                _shaderError = e.Message;
                error = _shaderError;
                return false;
            }
        }

        public LiquidGlassEffect()
        {
            if (_shader == null)
                throw new InvalidOperationException("Call TryEnsureShader first");

            PixelShader = _shader;
            DdxUvDdyUvRegisterIndex = 0;
            Input = ImplicitInput;

            UpdateShaderValue(InputProperty);
            UpdateShaderValue(BackdropProperty);
            UpdateShaderValue(RadiusProperty);
            UpdateShaderValue(RefractionProperty);
            UpdateShaderValue(EdgeWidthProperty);
            UpdateShaderValue(ChromaProperty);
            UpdateShaderValue(TintProperty);
            UpdateShaderValue(TintAmountProperty);
            UpdateShaderValue(HighlightProperty);
            UpdateShaderValue(LightAngleProperty);
            UpdateShaderValue(SaturationProperty);
            UpdateShaderValue(PadLTProperty);
            UpdateShaderValue(PadRBProperty);
            UpdateShaderValue(BrightnessProperty);
            UpdateShaderValue(UseBackdropProperty);
        }

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LiquidGlassEffect), 0);
        public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }

        public static readonly DependencyProperty BackdropProperty =
            RegisterPixelShaderSamplerProperty(nameof(Backdrop), typeof(LiquidGlassEffect), 1, SamplingMode.Bilinear);
        public Brush Backdrop { get => (Brush)GetValue(BackdropProperty); set => SetValue(BackdropProperty, value); }

        private static DependencyProperty Reg<T>(string name, T def, int register) =>
            DependencyProperty.Register(name, typeof(T), typeof(LiquidGlassEffect),
                new UIPropertyMetadata(def, PixelShaderConstantCallback(register)));

        public static readonly DependencyProperty RadiusProperty = Reg(nameof(Radius), 20.0, 1);
        public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

        public static readonly DependencyProperty RefractionProperty = Reg(nameof(Refraction), 28.0, 2);
        public double Refraction { get => (double)GetValue(RefractionProperty); set => SetValue(RefractionProperty, value); }

        public static readonly DependencyProperty EdgeWidthProperty = Reg(nameof(EdgeWidth), 26.0, 3);
        public double EdgeWidth { get => (double)GetValue(EdgeWidthProperty); set => SetValue(EdgeWidthProperty, value); }

        public static readonly DependencyProperty ChromaProperty = Reg(nameof(Chroma), 0.35, 4);
        public double Chroma { get => (double)GetValue(ChromaProperty); set => SetValue(ChromaProperty, value); }

        public static readonly DependencyProperty TintProperty = Reg(nameof(Tint), Colors.White, 5);
        public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }

        public static readonly DependencyProperty TintAmountProperty = Reg(nameof(TintAmount), 0.2, 6);
        public double TintAmount { get => (double)GetValue(TintAmountProperty); set => SetValue(TintAmountProperty, value); }

        public static readonly DependencyProperty HighlightProperty = Reg(nameof(Highlight), 1.0, 7);
        public double Highlight { get => (double)GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }

        public static readonly DependencyProperty LightAngleProperty = Reg(nameof(LightAngle), 2.1, 8);
        /// <summary>弧度</summary>
        public double LightAngle { get => (double)GetValue(LightAngleProperty); set => SetValue(LightAngleProperty, value); }

        public static readonly DependencyProperty SaturationProperty = Reg(nameof(Saturation), 1.3, 9);
        public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }

        public static readonly DependencyProperty PadLTProperty = Reg(nameof(PadLT), new Point(0, 0), 10);
        public Point PadLT { get => (Point)GetValue(PadLTProperty); set => SetValue(PadLTProperty, value); }

        public static readonly DependencyProperty PadRBProperty = Reg(nameof(PadRB), new Point(0, 0), 11);
        public Point PadRB { get => (Point)GetValue(PadRBProperty); set => SetValue(PadRBProperty, value); }

        public static readonly DependencyProperty BrightnessProperty = Reg(nameof(Brightness), 1.0, 12);
        public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }

        public static readonly DependencyProperty UseBackdropProperty = Reg(nameof(UseBackdrop), 1.0, 13);
        public double UseBackdrop { get => (double)GetValue(UseBackdropProperty); set => SetValue(UseBackdropProperty, value); }
    }
}
