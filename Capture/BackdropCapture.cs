using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flow.Launcher.Plugin.LiquidGlass.Interop;

namespace Flow.Launcher.Plugin.LiquidGlass.Capture
{
    /// <summary>
    /// 抓取屏幕上一块区域并做低成本模糊：先整数倍缩小（面积平均），再在小图上做 3 次盒式模糊（近似高斯）。
    /// 着色器用双线性采样放大小图，得到平滑的毛玻璃底。
    /// 窗口本身已用 WDA_EXCLUDEFROMCAPTURE 排除，所以抓到的是窗口背后的内容。
    /// </summary>
    internal static class BackdropCapture
    {
        public static BitmapSource Capture(RECT src, double blurRadiusPx)
        {
            if (src.IsEmpty) return null;

            var pixels = GrabScreen(src);
            if (pixels == null) return null;

            int w = src.Width, h = src.Height;

            // 选择缩小倍数：模糊越大缩得越多，节省后续计算
            int scale = Math.Clamp((int)Math.Round(blurRadiusPx / 3.0), 1, 8);
            if (scale > 1)
                pixels = Downsample(pixels, w, h, scale, out w, out h);

            int boxR = (int)Math.Round(blurRadiusPx / scale / 1.7);
            if (boxR > 0)
            {
                BoxBlur3(pixels, w, h, boxR);
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, pixels, w * 4);
            bmp.Freeze();
            return bmp;
        }

        private static int[] GrabScreen(RECT src)
        {
            int w = src.Width, h = src.Height;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;

            IntPtr memDc = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                memDc = Native.CreateCompatibleDC(screenDc);
                var bih = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = w,
                    biHeight = -h, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0 // BI_RGB
                };
                dib = Native.CreateDIBSection(screenDc, ref bih, 0, out var bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;

                old = Native.SelectObject(memDc, dib);
                if (!Native.BitBlt(memDc, 0, 0, w, h, screenDc, src.Left, src.Top, Native.SRCCOPY | Native.CAPTUREBLT))
                    return null;

                var buffer = new int[w * h];
                Marshal.Copy(bits, buffer, 0, buffer.Length);
                return buffer;
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
                if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                if (memDc != IntPtr.Zero) Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static int[] Downsample(int[] src, int w, int h, int s, out int nw, out int nh)
        {
            nw = Math.Max(1, (w + s - 1) / s);
            nh = Math.Max(1, (h + s - 1) / s);
            var dst = new int[nw * nh];
            for (int y = 0; y < nh; y++)
            {
                int y0 = y * s, y1 = Math.Min(h, y0 + s);
                for (int x = 0; x < nw; x++)
                {
                    int x0 = x * s, x1 = Math.Min(w, x0 + s);
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        int row = yy * w;
                        for (int xx = x0; xx < x1; xx++)
                        {
                            int p = src[row + xx];
                            b += p & 0xFF;
                            g += (p >> 8) & 0xFF;
                            r += (p >> 16) & 0xFF;
                            n++;
                        }
                    }
                    dst[y * nw + x] = (r / n << 16) | (g / n << 8) | (b / n);
                }
            }
            return dst;
        }

        private static void BoxBlur3(int[] px, int w, int h, int r)
        {
            int n = w * h;
            var rc = new int[n]; var gc = new int[n]; var bc = new int[n];
            for (int i = 0; i < n; i++)
            {
                int p = px[i];
                bc[i] = p & 0xFF; gc[i] = (p >> 8) & 0xFF; rc[i] = (p >> 16) & 0xFF;
            }

            var tmp = new int[Math.Max(w, h)];
            var line = new int[Math.Max(w, h)];
            foreach (var ch in new[] { rc, gc, bc })
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int off = y * w;
                        for (int x = 0; x < w; x++) line[x] = ch[off + x];
                        Box1D(line, tmp, w, r);
                        for (int x = 0; x < w; x++) ch[off + x] = tmp[x];
                    }
                    for (int x = 0; x < w; x++)
                    {
                        for (int y = 0; y < h; y++) line[y] = ch[y * w + x];
                        Box1D(line, tmp, h, r);
                        for (int y = 0; y < h; y++) ch[y * w + x] = tmp[y];
                    }
                }
            }

            for (int i = 0; i < n; i++)
                px[i] = (rc[i] << 16) | (gc[i] << 8) | bc[i];
        }

        // 边缘夹取的滑动窗口均值
        private static void Box1D(int[] src, int[] dst, int len, int r)
        {
            int win = 2 * r + 1;
            int sum = 0;
            for (int i = -r; i <= r; i++)
                sum += src[Math.Clamp(i, 0, len - 1)];
            for (int i = 0; i < len; i++)
            {
                dst[i] = sum / win;
                int add = src[Math.Min(i + r + 1, len - 1)];
                int sub = src[Math.Max(i - r, 0)];
                sum += add - sub;
            }
        }
    }
}
