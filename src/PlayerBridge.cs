using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace NocnyFiltr {
    internal sealed class PlayerBridge : IDisposable {
        [DllImport("NocnyFiltr.Engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void NfTraceMark(int stage,int generation,double a,double b,double c);
        [DllImport("NocnyFiltr.Engine.dll",CallingConvention=CallingConvention.Cdecl)] static extern void NfBrowserUpdate(IntPtr window,int generation,double changedAt,int pending,int visible,int left,int top,int right,int bottom);
        [DllImport("NocnyFiltr.Engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void NfPlayer(IntPtr window,int left,int top,int right,int bottom,int generation);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int count);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Bounds rect);
        delegate bool EnumWindowProc(IntPtr window,IntPtr arg);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr arg);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        internal static DateTime LastSeen=DateTime.MinValue;
        [StructLayout(LayoutKind.Sequential)] struct Bounds {public int Left,Top,Right,Bottom;}
        public sealed class Message {public int windowId {get;set;} public bool pending {get;set;} public double changedAt {get;set;} public double sentAt {get;set;} public string title {get;set;} public int generation {get;set;} public bool visible {get;set;} public int left {get;set;} public int top {get;set;} public int right {get;set;} public int bottom {get;set;} }
        internal static string LastMessage="none";
        IntPtr cachedWindow=IntPtr.Zero;
        readonly Dictionary<int,IntPtr> browserWindows=new Dictionary<int,IntPtr>();
        volatile bool stopping; NamedPipeServerStream server; Thread worker;
        internal PlayerBridge() { worker=new Thread(Run) {IsBackground=true};worker.Start(); }
        void Run() {
            var sid=WindowsIdentity.GetCurrent().User;
            var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new PipeAccessRule(sid,PipeAccessRights.FullControl,AccessControlType.Allow));
            while(!stopping) try {
                using(var pipe=new NamedPipeServerStream("SoftlightFirefox-"+sid.Value,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.None,4096,4096,security)) {
                    server=pipe; if(stopping)break;pipe.WaitForConnection();
                    using(var reader=new BinaryReader(pipe,Encoding.UTF8)) {
                        int length=reader.ReadInt32();if(length<2||length>4096)continue;
                        byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length)continue;
                        var json=new JavaScriptSerializer {MaxJsonLength=4096,RecursionLimit=8};
                        Message m=json.Deserialize<Message>(Encoding.UTF8.GetString(bytes));
                        ProcessMessage(m);
                    }
                }
            } catch(Exception ex) { LastMessage=ex.Message; if(!stopping)Thread.Sleep(50); }
        }
        void ProcessMessage(Message m) {
            LastSeen=DateTime.UtcNow;
            if(m!=null)NfTraceMark(4,m.generation,m.sentAt,m.visible?1:0,0);
            LastMessage=m==null?"empty":("visible="+m.visible+" rect="+m.left+","+m.top+","+m.right+","+m.bottom);
            IntPtr window=Native.GetForegroundWindow();var cls=new StringBuilder(128);GetClassName(window,cls,cls.Capacity);
            if(m!=null && m.visible && string.IsNullOrEmpty(m.title))return;
            if(m!=null && !string.IsNullOrEmpty(m.title)) {
                IntPtr match;
                if(!browserWindows.TryGetValue(m.windowId,out match) || !IsWindowVisible(match) || !IsFirefox(match))match=FindBrowser(m.title);
                if(match==IntPtr.Zero) {NfTraceMark(5,m.generation,0,0,0);return;}
                cachedWindow=window=match;browserWindows[m.windowId]=match;GetClassName(window,cls,cls.Capacity);
            }
            if(m!=null)NfTraceMark(6,m.generation,0,0,0);
            Bounds b;
            if(m!=null && m.visible && cls.ToString()=="MozillaWindowClass" && GetWindowRect(window,out b) &&
                m.left>=b.Left && m.top>=b.Top && m.right<=b.Right && m.bottom<=b.Bottom &&
                (long)m.right-m.left>=32 && (long)m.bottom-m.top>=24) {
                NfBrowserUpdate(window,m.generation,m.changedAt,m.pending?1:0,1,m.left,m.top,m.right,m.bottom);
            } else if(m!=null && cls.ToString()=="MozillaWindowClass")NfBrowserUpdate(window,m.generation,m.changedAt,m.pending?1:0,0,0,0,0,0);
        }

        static bool IsFirefox(IntPtr window) {var cls=new StringBuilder(128);GetClassName(window,cls,cls.Capacity);return cls.ToString()=="MozillaWindowClass";}
        IntPtr FindBrowser(string title) {
            if (cachedWindow != IntPtr.Zero && IsWindowVisible(cachedWindow)) {
                var cachedTitle = new StringBuilder(1024);
                GetWindowText(cachedWindow, cachedTitle, cachedTitle.Capacity);
                if (cachedTitle.ToString().StartsWith(title, StringComparison.Ordinal)) return cachedWindow;
            }

            IntPtr match = IntPtr.Zero;
            int matches = 0;
            EnumWindows(delegate(IntPtr candidate, IntPtr arg) {
                var kind = new StringBuilder(128);
                GetClassName(candidate, kind, kind.Capacity);
                if (kind.ToString() != "MozillaWindowClass" || !IsWindowVisible(candidate)) return true;
                var name = new StringBuilder(1024);
                GetWindowText(candidate, name, name.Capacity);
                if (name.ToString().StartsWith(title, StringComparison.Ordinal)) {
                    match = candidate;
                    matches++;
                }
                return true;
            }, IntPtr.Zero);
            // Ambiguous browser titles must never place a mask on a guessed window.
            return matches == 1 ? match : IntPtr.Zero;
        }

        public void Dispose() { stopping=true;try {if(server!=null)server.Dispose();}catch{} if(worker!=null)worker.Join(500);NfPlayer(IntPtr.Zero,0,0,0,0,0); }
    }
}
