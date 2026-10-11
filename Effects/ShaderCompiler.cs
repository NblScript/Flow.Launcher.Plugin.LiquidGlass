using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Flow.Launcher.Plugin.LiquidGlass.Effects
{
    /// <summary>
    /// 通过系统自带的 d3dcompiler_47.dll 在运行时把 HLSL 编译为 WPF 可用的像素着色器字节码。
    /// Windows 8.1 起该 DLL 随系统提供，因此插件不需要附带预编译的 .ps 文件。
    /// </summary>
    internal static unsafe class ShaderCompiler
    {
        private const uint D3DCOMPILE_OPTIMIZATION_LEVEL3 = 1u << 15;

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        private static extern int D3DCompile(
            byte* pSrcData,
            nuint srcDataSize,
            byte* pSourceName,
            IntPtr pDefines,
            IntPtr pInclude,
            byte* pEntrypoint,
            byte* pTarget,
            uint flags1,
            uint flags2,
            out IntPtr ppCode,
            out IntPtr ppErrorMsgs);

        public static string LoadEmbeddedSource(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    using var s = asm.GetManifestResourceStream(name);
                    using var r = new StreamReader(s);
                    return r.ReadToEnd();
                }
            }
            throw new FileNotFoundException($"Embedded shader '{fileName}' not found");
        }

        public static byte[] Compile(string source, string entryPoint = "main", string target = "ps_3_0")
        {
            var src = Encoding.UTF8.GetBytes(source);
            var srcName = Encoding.ASCII.GetBytes("LiquidGlass.hlsl\0");
            var entry = Encoding.ASCII.GetBytes(entryPoint + "\0");
            var tgt = Encoding.ASCII.GetBytes(target + "\0");

            IntPtr code, errors;
            int hr;
            fixed (byte* pSrc = src)
            fixed (byte* pName = srcName)
            fixed (byte* pEntry = entry)
            fixed (byte* pTgt = tgt)
            {
                hr = D3DCompile(pSrc, (nuint)src.Length, pName, IntPtr.Zero, IntPtr.Zero,
                    pEntry, pTgt, D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, out code, out errors);
            }

            try
            {
                if (hr < 0 || code == IntPtr.Zero)
                {
                    var msg = errors != IntPtr.Zero ? BlobToString(errors) : $"HRESULT 0x{hr:X8}";
                    throw new InvalidOperationException("HLSL compile failed: " + msg);
                }
                return BlobToBytes(code);
            }
            finally
            {
                Release(code);
                Release(errors);
            }
        }

        // ID3DBlob vtable: 0 QueryInterface, 1 AddRef, 2 Release, 3 GetBufferPointer, 4 GetBufferSize
        private static byte[] BlobToBytes(IntPtr blob)
        {
            var vtbl = *(IntPtr**)blob;
            var getPtr = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)vtbl[3];
            var getSize = (delegate* unmanaged[Stdcall]<IntPtr, nuint>)vtbl[4];
            var ptr = getPtr(blob);
            var size = (int)getSize(blob);
            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);
            return bytes;
        }

        private static string BlobToString(IntPtr blob)
        {
            var bytes = BlobToBytes(blob);
            return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        private static void Release(IntPtr unk)
        {
            if (unk == IntPtr.Zero) return;
            var vtbl = *(IntPtr**)unk;
            var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtbl[2];
            release(unk);
        }
    }
}
