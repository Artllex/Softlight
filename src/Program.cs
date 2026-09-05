using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NocnyFiltr {
    internal static class Program {
        internal const string Title = "Nocny Filtr";
        internal static readonly uint ShowMessage = Native.RegisterWindowMessage("NocnyFiltr.Show.v1");
        internal static readonly uint ExitMessage = Native.RegisterWindowMessage("NocnyFiltr.Exit.v1");
        [STAThread]
        static int Main(string[] args) {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--firefox-check") return SelfTests.FirefoxCheck(args[1]);
            if (args.Length > 0 && args[0] == "--self-test") return SelfTests.Run(args.Length > 1 ? args[1] : "self-test.txt");
            if (args.Length > 0 && args[0] == "--interface-check") return SelfTests.InterfaceCheck(args[1]);
            if (args.Length > 0 && args[0] == "--render-menu") {
                using(MainForm f=new MainForm(true)) { f.Show(); Application.DoEvents(); f.RenderMenu(args[1]); } return 0;
            }
            if (args.Length > 0 && args[0] == "--render-ui") {
                using (MainForm f = new MainForm(true)) {
                    f.Show(); Application.DoEvents();
                    if(Array.IndexOf(args,"--graph")>=0) {f.ToggleGraph();Application.DoEvents();}
                    using (Bitmap b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(Point.Empty, f.Size)); b.Save(args[1], ImageFormat.Png); }
                }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--smoke-test") return SelfTests.WindowSmoke(args[1]);
            if (args.Length > 0 && args[0] == "--motion-test") return SelfTests.Motion(args[1]);
            if (args.Length > 0 && args[0] == "--exit") {
                Native.PostMessage(Native.FindWindow(null, Title), ExitMessage, IntPtr.Zero, IntPtr.Zero); return 0;
            }
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\NocnyFiltr.v1", out created)) {
                if (!created) { Native.PostMessage(Native.FindWindow(null, Title), ShowMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                try {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    using (MainForm f = new MainForm(false)) {
                        f.StartHidden = Array.IndexOf(args, "--tray") >= 0;
                        if (Array.IndexOf(args, "--paused") >= 0) f.Pause();
                        Application.Run(f);
                    }
                    return 0;
                } catch (Exception e) {
                    try { Native.NfEnable(0); } catch { }
                    MessageBox.Show("Could not start Softlight.\n\n" + e.Message, "Softlight", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                } finally { try { Native.NfStop(); } catch { } }
            }
        }
    }
}
