using System;
using System.Runtime.InteropServices;

namespace NocnyFiltr {
    [StructLayout(LayoutKind.Sequential)]
    internal struct EngineStatus {
        public int State, Monitors, HdrMonitors, Error;
        public ulong Frames, Heartbeat;
    }
    internal static class Native {
        [DllImport("NocnyFiltr.Engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void NfFlashProtection(int enabled);
        [DllImport("NocnyFiltr.Engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void NfTestHoldCapture(int enabled);
        [DllImport("NocnyFiltr.Engine.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] internal static extern void NfGraphRead(ulong after,System.Text.StringBuilder text,int capacity);
        [DllImport("NocnyFiltr.Engine.dll", CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Unicode)] internal static extern void NfWindowReport(System.Text.StringBuilder text,int capacity);
        [DllImport("NocnyFiltr.Engine.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int NfTestResponse();
        [DllImport("NocnyFiltr.Engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void NfTiming(int hz,int speed,int sudden);
        const string Engine = "NocnyFiltr.Engine.dll";
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern int NfStart();
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern void NfConfigure(float threshold, float strength, int curve, int fps);
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern void NfEnable(int enabled);
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern void NfGetStatus(out EngineStatus status);
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern void NfRefresh();
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern void NfPreviewRect(int x, int y, int width, int height);
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern int NfProbe(int x, int y, int composite);
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern int NfProbeResult(out uint input, out uint mask, out uint display);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(System.Drawing.Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern void NfStop();
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern int NfTestShader(float t, float s, int curve, int rotation, int width, int height, byte[] input, byte[] output);
        [DllImport(Engine, CallingConvention = CallingConvention.Cdecl)] internal static extern int NfTestHdrShader(float t, float s, int curve, float white, int count, float[] input, float[] output);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("kernel32.dll")] internal static extern ulong GetTickCount64();
    }
    internal static class Tone {
        internal static double Decode(double x) { return x <= .04045 ? x/12.92 : Math.Pow((x+.055)/1.055,2.4); }
        internal static double Encode(double x) { return x <= .0031308 ? x*12.92 : 1.055*Math.Pow(x,1/2.4)-.055; }
        internal static double Map(double y, double threshold, double strength, int curve) {
            if (y <= threshold || strength <= 0) return y;
            double x = (y - threshold) / (1 - threshold);
            double mapped = curve == 0 ? x * (1 - strength) : x / (1 + strength / (1 - strength) * x);
            return threshold + (1 - threshold) * mapped;
        }
        internal static System.Drawing.Color MapColor(System.Drawing.Color color, Settings s) {
            if (s.HdrPreview) {
                double r=Decode(color.R/255.0),g=Decode(color.G/255.0),b=Decode(color.B/255.0);
                double lum=.2126*r+.7152*g+.0722*b;
                double scale=lum==0 ? 1 : Decode(Map(Encode(lum),s.Threshold/100.0,s.Strength/100.0,s.Curve))/lum;
                return System.Drawing.Color.FromArgb((int)Math.Round(255*Encode(r*scale)),(int)Math.Round(255*Encode(g*scale)),(int)Math.Round(255*Encode(b*scale)));
            }
            double y = (.2126 * color.R + .7152 * color.G + .0722 * color.B) / 255;
            double ratio = y == 0 ? 1 : Map(y, s.Threshold / 100.0, s.Strength / 100.0, s.Curve) / y;
            return System.Drawing.Color.FromArgb((int)Math.Round(color.R * ratio), (int)Math.Round(color.G * ratio), (int)Math.Round(color.B * ratio));
        }
    }
}
