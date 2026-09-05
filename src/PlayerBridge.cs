using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace NocnyFiltr {
    internal sealed class PlayerBridge : IDisposable {
        [DllImport("NocnyFiltr.Engine.dll",CallingConvention=CallingConvention.Cdecl)] static extern void NfBrowserContext(IntPtr window,int generation);
        [DllImport("NocnyFiltr.Engine.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void NfPlayer(IntPtr window,int left,int top,int right,int bottom,int generation);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int count);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Bounds rect);
        delegate bool EnumWindowProc(IntPtr window,IntPtr arg);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr arg);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        internal static DateTime LastSeen=DateTime.MinValue;
        [StructLayout(LayoutKind.Sequential)] struct Bounds {public int Left,Top,Right,Bottom;}
        public sealed class Message {public string title {get;set;} public int generation {get;set;} public bool visible {get;set;} public int left {get;set;} public int top {get;set;} public int right {get;set;} public int bottom {get;set;} }
        internal static string LastMessage="none";
        IntPtr cachedWindow=IntPtr.Zero;
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
                        LastSeen=DateTime.UtcNow;
                        LastMessage=m==null?"empty":("visible="+m.visible+" rect="+m.left+","+m.top+","+m.right+","+m.bottom);
                        IntPtr window=Native.GetForegroundWindow();var cls=new StringBuilder(128);GetClassName(window,cls,cls.Capacity);
                        var caption=new StringBuilder(1024);GetWindowText(window,caption,caption.Capacity);
                        if(m!=null && m.visible && string.IsNullOrEmpty(m.title))continue;
                        if(m!=null && !string.IsNullOrEmpty(m.title)) {
                            if(string.IsNullOrEmpty(m.title))continue;
                            IntPtr match=IntPtr.Zero;int matches=0;
                            var cachedTitle=new StringBuilder(1024);
                            if(cachedWindow!=IntPtr.Zero && IsWindowVisible(cachedWindow)) {
                                GetWindowText(cachedWindow,cachedTitle,cachedTitle.Capacity);
                                if(cachedTitle.ToString().StartsWith(m.title,StringComparison.Ordinal)) {match=cachedWindow;matches=1;}
                            }
                            if(matches==0)
                            EnumWindows(delegate(IntPtr candidate,IntPtr arg) {
                                var name=new StringBuilder(1024);var kind=new StringBuilder(128);
                                GetClassName(candidate,kind,kind.Capacity);
                                if(kind.ToString()!="MozillaWindowClass" || !IsWindowVisible(candidate))return true;
                                GetWindowText(candidate,name,name.Capacity);
                                if(IsWindowVisible(candidate) && kind.ToString()=="MozillaWindowClass" && name.ToString().StartsWith(m.title,StringComparison.Ordinal)) {match=candidate;matches++;}
                                return true;
                            },IntPtr.Zero);
                            if(matches!=1)continue;
                            cachedWindow=window=match;GetClassName(window,cls,cls.Capacity);
                            NfBrowserContext(window,m.generation);
                        }
                        Bounds b;
                        if(m!=null && m.visible && cls.ToString()=="MozillaWindowClass" && GetWindowRect(window,out b) &&
                            m.left>=b.Left && m.top>=b.Top && m.right<=b.Right && m.bottom<=b.Bottom &&
                            (long)m.right-m.left>=32 && (long)m.bottom-m.top>=24) {
                            NfPlayer(window,m.left,m.top,m.right,m.bottom,m.generation);
                        } else NfPlayer(IntPtr.Zero,0,0,0,0,0);
                    }
                }
            } catch(Exception ex) { LastMessage=ex.Message; if(!stopping)Thread.Sleep(50); }
        }
        public void Dispose() { stopping=true;try {if(server!=null)server.Dispose();}catch{} if(worker!=null)worker.Join(500);NfPlayer(IntPtr.Zero,0,0,0,0,0); }
    }
}
