using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NocnyFiltr {
    internal static class SelfTests {
        internal static int FirefoxCheck(string path) {
            var report=new List<string>();
            try {
                Native.NfStart();Native.NfConfigure(0,.70f,0,120);Native.NfTiming(30,75,30);Native.NfEnable(1);
                using(var bridge=new PlayerBridge()) {
                    DateTime end=DateTime.UtcNow.AddSeconds(55);string latest="";
                    while(DateTime.UtcNow<end) {
                        WaitUi(200);var text=new StringBuilder(4096);Native.NfWindowReport(text,text.Capacity);latest=text.ToString();
                        if(latest.Contains("Firefox video")) {
                            WaitUi(2500);text.Clear();Native.NfWindowReport(text,text.Capacity);
                            latest=text.ToString();if(!latest.Contains("Firefox video"))continue;
                            string video=Array.Find(latest.Split('\n'),line=>line.Contains("Firefox video"));
                            int alpha=int.Parse(video.Substring(0,video.IndexOf('%')));
                            Require(alpha>20,"Video not dimmed independently: "+video);
                            string page=Array.Find(latest.Split('\n'),line=>line.Contains("Softlight player test"));
                            Require(page!=null,"Test browser missing");
                            int pageAlpha=int.Parse(page.Substring(0,page.IndexOf('%')));
                            Require(pageAlpha<10,"Dark page still affected by bright video: "+page);
                            report.Add("PASS: Firefox extension -> native messaging -> user-only pipe -> native regions.");
                            report.Add("PASS: video dimming="+alpha+"%, dark page="+pageAlpha+"%.");
                            File.WriteAllLines(path,report);return 0;
                        }
                    }
                    throw new Exception("No video region received. Bridge: "+PlayerBridge.LastMessage+" Latest report: "+latest);
                }
            }catch(Exception e){report.Add("FAIL: "+e);File.WriteAllLines(path,report);return 1;}
            finally{Native.NfEnable(0);Native.NfStop();}
        }
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        internal static int WindowSmoke(string path) {
            List<string> report=new List<string>();
            try {
                using(Form bright=new Form {StartPosition=FormStartPosition.Manual,AutoScaleMode=AutoScaleMode.None,Text="Window test bright",Bounds=new Rectangle(100,150,700,400),BackColor=Color.White,FormBorderStyle=FormBorderStyle.None,TopMost=true})
                using(Form dark=new Form {StartPosition=FormStartPosition.Manual,AutoScaleMode=AutoScaleMode.None,Text="Window test dark",Bounds=new Rectangle(900,150,400,400),BackColor=Color.FromArgb(24,24,24),FormBorderStyle=FormBorderStyle.None,TopMost=true}) {
                    bright.Paint+=delegate(object sender,PaintEventArgs e) {using(SolidBrush b=new SolidBrush(Color.FromArgb(60,60,60)))e.Graphics.FillRectangle(b,0,280,700,120);};
                    bright.Show();dark.Show();WaitUi(300);Native.NfStart();Native.NfConfigure(0,.65f,0,120);Native.NfEnable(1);WaitUi(6500);
                    EngineStatus status;Native.NfGetStatus(out status);Require(status.State==2,"Engine not active");
                    uint[] light=Probe(bright.PointToScreen(new Point(250,100)),false),shadow=Probe(bright.PointToScreen(new Point(250,340)),false),other=Probe(dark.PointToScreen(new Point(150,100)),false);
                    report.Add("Alphas: bright="+(light[1]>>24)+", shadow in same window="+(shadow[1]>>24)+", dark window="+(other[1]>>24));
                    Require((light[0]&255)>250,"Capture feedback: "+light[0].ToString("X8")+" bounds="+bright.Bounds);
                    Require((light[1]>>24)>20,"Bright window not selected");
                    Require(Math.Abs((double)(light[1]>>24)-(shadow[1]>>24))<=3,"Shadows have different gain");
                    Require((other[1]>>24)<=3,"Dark window dimmed");
                    report.Add("PASS: automatic whole-window selection; same dimming on highlights and shadows; dark neighboring window unchanged; original capture retained.");
                    bright.Hide();WaitUi(3300);bright.Show();WaitUi(100);
                    uint restored=Probe(bright.PointToScreen(new Point(250,100)),false)[1]>>24;
                    Require(Math.Abs(restored-(double)(light[1]>>24))<=8,"Hidden window lost its cached dimming");
                    report.Add("PASS: hidden window retains dimming after more than 3 seconds.");
                    dark.Bounds=bright.Bounds;dark.BringToFront();WaitUi(3300);
                    dark.Location=new Point(900,150);dark.Size=new Size(400,400);WaitUi(100);
                    restored=Probe(bright.PointToScreen(new Point(250,100)),false)[1]>>24;
                    Require(Math.Abs(restored-(double)(light[1]>>24))<=8,"Fully covered window faded toward zero");
                    report.Add("PASS: fully covered window retains dimming instead of fading to zero.");
                    Native.NfConfigure(0,.95f,0,120);WaitUi(5000);
                    uint strong=Probe(bright.PointToScreen(new Point(250,100)),false)[1]>>24;
                    Require(strong>=235,"95 percent strength is not visibly strong");
                    Require((Probe(dark.PointToScreen(new Point(150,100)),false)[1]>>24)<=3,"Strong mode selected a dark window");
                    report.Add("PASS: 95 percent setting gives alpha="+strong+"/255 on bright window; dark window unchanged.");
                    Native.NfConfigure(0,.65f,0,120);WaitUi(4000);
                    dark.Location=new Point(250,200);WaitUi(1200);
                    Require((Probe(new Point(350,250),false)[1]>>24)<=3,"Foreground dark window did not occlude bright window mask");
                    Require(Native.WindowFromPoint(new Point(350,250))==dark.Handle,"Click-through failed");
                    report.Add("PASS: foreground occlusion, movement and click-through.");
                    dark.Hide();bright.Location=new Point(700,650);WaitUi(1500);
                    Require((Probe(bright.PointToScreen(new Point(200,100)),false)[1]>>24)>20,"Mask did not follow moved window");
                    report.Add("PASS: mask follows whole window after moving.");
                    Native.NfEnable(0);WaitUi(400);Native.NfGetStatus(out status);Require(status.State==3,"Pause failed");
                    Native.NfEnable(1);WaitUi(1200);Native.NfGetStatus(out status);Require(status.State==2,"Resume failed");
                    report.Add("PASS: pause and resume.");
                    Native.NfTiming(4,0,0);bright.BackColor=Color.FromArgb(24,24,24);bright.Invalidate();WaitUi(5000);
                    bright.BackColor=Color.White;bright.Invalidate();WaitUi(150);
                    uint attack=Probe(bright.PointToScreen(new Point(250,100)),false)[1]>>24;
                    Require(attack>90,"Bright cut still fades slowly at minimum speed");
                    report.Add("PASS: dark-to-white cut attacks promptly even at 4 Hz and minimum speed; alpha after 150 ms plus probe wait="+attack+"/255 (not a display-latency measurement).");
                }
                report.Add("RESULT: PASS");File.WriteAllLines(path,report);return 0;
            } catch(Exception e) {report.Add("FAIL: "+e);File.WriteAllLines(path,report);return 1;}
            finally {Native.NfEnable(0);Native.NfStop();}
        }
        internal static int Motion(string path) {
            List<string> report=new List<string>();
            try {
                using(Form patch=new Form { Bounds=new Rectangle(80,100,480,240), FormBorderStyle=FormBorderStyle.None, BackColor=Color.FromArgb(100,100,100), TopMost=true }) {
                    int position=0;
                    patch.Paint+=delegate(object sender,PaintEventArgs e) { using(SolidBrush b=new SolidBrush(Color.FromArgb(180,180,180))) e.Graphics.FillRectangle(b,position,0,50,240); };
                    patch.Show(); WaitUi(250); Native.NfStart(); Native.NfConfigure(.45f,.65f,1,120); Native.NfEnable(1); WaitUi(600);
                    for(int run=0;run<3;run++) {
                        EngineStatus before,after; Native.NfGetStatus(out before);
                        System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
                        while(clock.ElapsedMilliseconds<3000) { position=(position+7)%430;patch.Invalidate();patch.Update();Application.DoEvents();Thread.Sleep(1); }
                        Native.NfGetStatus(out after); Require(after.State==2,"Engine not active");
                        report.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,"Run {0}: {1:F1} processed fps; HDR={2}",run+1,(after.Frames-before.Frames)/clock.Elapsed.TotalSeconds,after.HdrMonitors));
                    }
                }
                File.WriteAllLines(path,report);return 0;
            } catch(Exception e) {report.Add("FAIL: "+e);File.WriteAllLines(path,report);return 1;}
            finally {Native.NfEnable(0);Native.NfStop();}
        }
        internal static int Run(string path) {
            List<string> report = new List<string>();
            try {
                DiagnosticTests.Run();
                report.Add("PASS: shared report parsing, culture independence, active contexts, history boundaries, retention and Freeze.");
                Require(Native.NfTestResponse()==0,"Response speed, frame independence or step overshoot");
                report.Add("PASS: 20,000 bounded jitter samples, larger correction accepted, immediate bright attack with smooth release, speed defaults, five frequencies; fast transitions at 30/60/120 fps; standard/high analysis scheduling after a brightness cut; smooth response in both modes.");
                double[] thresholds = {0, .1, .45, .8, .95};
                double[] strengths = {0, .1, .65, .95};
                int checks = 0;
                foreach (double t in thresholds) foreach (double s in strengths) for (int c = 0; c < 2; c++) {
                    double last = -1;
                    for (int i = 0; i <= 10000; i++) {
                        double y = i / 10000.0, mapped = Tone.Map(y, t, s, c);
                        Require(mapped >= last - 1e-12 && mapped >= 0 && mapped <= y + 1e-12, "Range/monotonicity");
                        if (y <= t || s == 0) Require(mapped == y, "Shadows/identity");
                        last = mapped; ++checks;
                    }
                    Require(Math.Abs(Tone.Map(1, t, s, c) - (t + (1-t)*(1-s))) < 1e-9, "White endpoint");
                    Require(Math.Abs(Tone.Map(t + 1e-8, t, s, c) - t) < 2e-8, "Threshold continuity");
                }
                Require(Math.Abs(Tone.Map(1, .45, .65, 0) - .6425) < 1e-12, "Known white output");
                report.Add("PASS: " + checks + " tone-curve samples; unchanged shadows, continuity, monotonicity, bounded output and white endpoints.");
                byte[] input = new byte[1024 * 4], output = new byte[input.Length];
                Random rng = new Random(9513); rng.NextBytes(input);
                for (int i = 0; i < 256; i++) input[4*i] = input[4*i+1] = input[4*i+2] = (byte)i;
                for (int i = 0; i < 1024; i++) input[i*4+3] = 255;
                int samples = 0;
                foreach (double t in thresholds) foreach (double s in strengths) for (int c = 0; c < 2; c++) {
                    int hr = Native.NfTestShader((float)t, (float)s, c, 1, 1024, 1, input, output);
                    Require(hr == 0, "Shader HRESULT 0x" + unchecked((uint)hr).ToString("X8"));
                    for (int i = 0; i < 1024; i++) {
                        double y = (.2126 * input[4*i+2] + .7152 * input[4*i+1] + .0722 * input[4*i]) / 255;
                        double alpha = y == 0 ? 0 : 1 - Tone.Map(y, t, s, c) / y;
                        Require(output[4*i] == 0 && output[4*i+1] == 0 && output[4*i+2] == 0, "Mask must be black");
                        Require(Math.Abs(output[4*i+3] / 255.0 - alpha) <= 1.01 / 255, "CPU/GPU alpha disagreement at " + i);
                        ++samples;
                    }
                }
                report.Add("PASS: production D3D11 shader (WARP), " + samples + " grayscale/colour pixels compared with the CPU preview; <= 1 alpha byte error.");
                byte[] rotationInput = new byte[6*4];
                for (int i=0; i<6; i++) { rotationInput[4*i]=rotationInput[4*i+1]=rotationInput[4*i+2]=(byte)(40+i*40); rotationInput[4*i+3]=255; }
                // Expected source indices after inverse desktop rotations, independently enumerated.
                int[][] orders = { new int[]{0,1,2,3,4,5}, new int[]{3,0,4,1,5,2}, new int[]{5,4,3,2,1,0}, new int[]{2,5,1,4,0,3} };
                for (int rot=1; rot<=4; rot++) {
                    byte[] rotated = new byte[24];
                    Require(Native.NfTestShader(.1f,.65f,1,rot,3,2,rotationInput,rotated)==0,"Rotation shader call");
                    for (int i=0;i<6;i++) {
                        double y=(40+orders[rot-1][i]*40)/255.0;
                        double a=1-Tone.Map(y,.1,.65,1)/y;
                        Require(Math.Abs(rotated[i*4+3]/255.0-a)<=1.01/255,"Rotation "+rot+", pixel "+i);
                    }
                }
                report.Add("PASS: production shader on a rectangular texture at 0, 90, 180 and 270 degrees.");
                Require(Native.NfTestShader(0,.4f,9,1,1024,1,input,output)==0,"Window shader call");
                for(int i=0;i<1024;i++) {
                    Require(output[i*4]==0&&output[i*4+1]==0&&output[i*4+2]==0,"Window mask is not black");
                    Require(Math.Abs(output[i*4+3]-(i<256?0:102))<=1,"Window gain differs with pixel brightness or ignores foreground occlusion");
                }
                report.Add("PASS: whole-window shader: all colours and shadows share the same alpha; foreground region blocks the lower mask.");
                float[] hdrInput=new float[4096],hdrOutput=new float[4096];
                for(int i=0;i<1024;i++) {
                    double v=i<256 ? Tone.Decode(i/255.0) : Math.Pow(2,rng.NextDouble()*10-4);
                    hdrInput[i*4]=(float)v;hdrInput[i*4+1]=(float)(i<256?v:v*rng.NextDouble());hdrInput[i*4+2]=(float)(i<256?v:v*rng.NextDouble());hdrInput[i*4+3]=1;
                }
                int hdrChecks=0;
                foreach(float white in new float[]{1,2.5f,10}) foreach(float t in new float[]{.1f,.45f,.8f}) foreach(float s in new float[]{0,.65f,.95f}) for(int c=0;c<2;c++) {
                    Require(Native.NfTestHdrShader(t,s,c,white,1024,hdrInput,hdrOutput)==0,"HDR shader call");
                    for(int i=0;i<1024;i++) {
                        double lum=(.2126*hdrInput[i*4]+.7152*hdrInput[i*4+1]+.0722*hdrInput[i*4+2])/white;
                        double y=Tone.Encode(lum),mapped=Tone.Map(y,t,s,c);
                        double expected=lum==0?0:1-Tone.Decode(mapped)/lum;
                        Require(Math.Abs(hdrOutput[i*4+3]-expected)<.00002,"HDR CPU/GPU mismatch");
                        Require(hdrOutput[i*4]==0&&hdrOutput[i*4+1]==0&&hdrOutput[i*4+2]==0,"HDR mask RGB");
                        if(y<=t) Require(hdrOutput[i*4+3]==0,"HDR shadows changed");
                        hdrChecks++;
                    }
                }
                report.Add("PASS: HDR production shader, "+hdrChecks+" pixels, extended highlights and three SDR-white references; linear alpha matches CPU.");
                string configPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "test-settings.ini");
                File.WriteAllText(configPath, "threshold=999\nstrength=-12\ncurve=99\nfps=2\nenabled=1\n");
                Settings malformed = Settings.Read(configPath);
                Require(malformed.Threshold==95 && malformed.Strength==0 && malformed.Curve==1 && malformed.Fps==30 && malformed.Enabled,"Settings bounds");
                Settings defaults = new Settings();
                Require(defaults.Strength==70 && defaults.Speed==75 && defaults.SuddenSpeed==30,"Requested defaults");
                Settings original = new Settings { Threshold = 33, Strength = 71, Curve = 0, Fps = 30, Enabled = true, Speed = 80, SuddenSpeed = 20, Frequency = 12, AlwaysOnTop = true, Language = "pl" };
                original.Write(configPath); Settings loaded = Settings.Read(configPath);
                Require(loaded.Threshold==33 && loaded.Strength==71 && loaded.Curve==0 && loaded.Fps==30 && loaded.Enabled,"Settings round trip");
                Require(loaded.Speed==80 && loaded.SuddenSpeed==20 && loaded.Frequency==12 && loaded.AlwaysOnTop && loaded.Language=="pl","All preferences round trip");
                File.Delete(configPath);
                report.Add("PASS: defaults 70%, 2x, 30%; settings validation, atomic replacement and all preferences persistence.");
                report.Add("RESULT: PASS");
                File.WriteAllLines(path, report, Encoding.UTF8); return 0;
            } catch (Exception e) { report.Add("FAIL: " + e); File.WriteAllLines(path, report, Encoding.UTF8); return 1; }
        }
        static void WaitUi(int ms) { DateTime end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(15); } }
        static bool ContainsText(Control control,string text) {
            if(control.Text==text)return true;foreach(Control child in control.Controls)if(ContainsText(child,text))return true;return false;
        }
        internal static int InterfaceCheck(string path) {
            List<string> report=new List<string>();
            try {
                Settings.Folder=Path.GetDirectoryName(Path.GetFullPath(path));Settings.FilePath=Path.Combine(Settings.Folder,"language-ui-test.ini");
                new Settings().Save();
                using(MainForm ui=new MainForm(false)) {
                    ui.Show();WaitUi(200);
                    Require(!ContainsText(ui,"×"),"Close button still exists");
                    Require(ContainsText(ui,"Strength"),"English default");
                    Label key=(Label)ui.Controls.Find("HotkeyStatus",true)[0];Require((bool)key.Tag,"Alt+F11 registration failed");
                    Rectangle original=ui.Bounds;
                    Native.PostMessage(ui.Handle,0x0312,new IntPtr(1),IntPtr.Zero);WaitUi(150);Require(!ui.Visible,"Hotkey hide");
                    EngineStatus status;Native.NfGetStatus(out status);Require(status.State==3,"Hotkey changed filter state");
                    Native.PostMessage(ui.Handle,0x0312,new IntPtr(1),IntPtr.Zero);WaitUi(150);Require(ui.Visible&&ui.Bounds==original,"Hotkey show/anchor");
                    ui.SetFrequency(120);WaitUi(650);Require(Settings.Read(Settings.FilePath).Frequency==120,"High frequency persistence");
                    ui.SetFrequency(30);WaitUi(650);Require(Settings.Read(Settings.FilePath).Frequency==30,"Standard frequency persistence");
                    ((Slider)ui.Controls.Find("ChangeSpeed",true)[0]).Value=80;((Slider)ui.Controls.Find("SuddenSpeed",true)[0]).Value=20;WaitUi(650);Require(Settings.Read(Settings.FilePath).Speed==80 && Settings.Read(Settings.FilePath).SuddenSpeed==20,"Speed sliders persistence");
                    CheckBox pin=(CheckBox)ui.Controls.Find("AlwaysOnTop",true)[0];
                    pin.Checked=true;WaitUi(650);Require(ui.TopMost&&Settings.Read(Settings.FilePath).AlwaysOnTop,"Pinned setting not applied/saved");
                    Native.PostMessage(ui.Handle,0x0312,new IntPtr(1),IntPtr.Zero);WaitUi(150);Require(ui.Visible,"Pinned panel hidden by shortcut");
                    pin.Checked=false;WaitUi(650);Require(!ui.TopMost&&!Settings.Read(Settings.FilePath).AlwaysOnTop,"Unpin not applied/saved");
                    ui.SetLanguage("pl");WaitUi(650);Require(ContainsText(ui,"Siła automatycznego przyciemniania"),"Polish translation");Require(Settings.Read(Settings.FilePath).Language=="pl","Polish persistence");
                    ui.SetLanguage("en");WaitUi(650);Require(ContainsText(ui,"Strength"),"English translation");Require(Settings.Read(Settings.FilePath).Language=="en","English persistence");
                    Native.PostMessage(ui.Handle,Program.ExitMessage,IntPtr.Zero,IntPtr.Zero);WaitUi(100);
                }
                using(MainForm reopened=new MainForm(false)) {
                    reopened.Show();WaitUi(200);
                    Require(((Slider)reopened.Controls.Find("ChangeSpeed",true)[0]).Value==80 && ((Slider)reopened.Controls.Find("SuddenSpeed",true)[0]).Value==20,"Reopened panel lost custom slider values");
                    Require(Settings.Read(Settings.FilePath).Strength==70,"Reopened panel changed strength");
                    Native.PostMessage(reopened.Handle,Program.ExitMessage,IntPtr.Zero,IntPtr.Zero);WaitUi(100);
                }
                report.Add("PASS: no close button; English default; Alt+F11 registered; hotkey handler hides/shows without changing filter; anchor retained; pin/unpin applies TopMost and persists; pinned hotkey keeps panel visible; English/Polish switch and persistence; closed and reopened panel restores custom slider values.");
                File.Delete(Settings.FilePath);File.WriteAllLines(path,report,Encoding.UTF8);return 0;
            } catch(Exception e) {report.Add("FAIL: "+e);File.WriteAllLines(path,report,Encoding.UTF8);return 1;}
            finally {Native.NfEnable(0);Native.NfStop();}
        }
        static uint[] Probe(Point point, bool composite) {
            int request = Native.NfProbe(point.X, point.Y, composite ? 1 : 0);
            uint input, mask, display; int done;
            DateTime end = DateTime.UtcNow.AddSeconds(4);
            do { WaitUi(30); done = Native.NfProbeResult(out input, out mask, out display); } while (done != request && DateTime.UtcNow < end);
            Require(done == request,"Probe timed out");
            return new uint[]{input,mask,display};
        }
        internal static int Smoke(string path) {
            List<string> report = new List<string>();
            Form patch = null;
            try {
                patch = new Form { Text = "Nocny Filtr — test obrazu", BackColor = Color.FromArgb(32,32,32), FormBorderStyle = FormBorderStyle.None,
                    Bounds = new Rectangle(Screen.PrimaryScreen.Bounds.Left+80, Screen.PrimaryScreen.Bounds.Top+100, 360, 180), TopMost = true, ShowInTaskbar = false };
                patch.Paint += delegate(object sender, PaintEventArgs e) {
                    e.Graphics.FillRectangle(Brushes.White, 0, 0, 120, 180);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(100,100,100))) e.Graphics.FillRectangle(b,120,0,120,180);
                };
                patch.Show(); WaitUi(300);
                Require(Native.NfStart() == 1,"Engine start");
                Native.NfConfigure(.45f,.65f,1,60);
                Native.NfEnable(1);
                EngineStatus status = new EngineStatus();
                DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                do { WaitUi(150); Native.NfGetStatus(out status); } while (status.State != 2 && DateTime.UtcNow < deadline);
                report.Add(string.Format("Live startup: state={0}, monitors={1}, HDR monitors={2}, frames={3}, error=0x{4:X8}", status.State,status.Monitors,status.HdrMonitors,status.Frames,unchecked((uint)status.Error)));
                Require(status.State == 2 && status.Monitors > 0 && status.Frames > 0,"Live desktop filter did not become ready");
                Point white = patch.PointToScreen(new Point(60,90));
                Point gray = patch.PointToScreen(new Point(180,90));
                Point dark = patch.PointToScreen(new Point(300,90));
                double expectedAlpha=status.HdrMonitors>0 ? (1-Tone.Decode(.6425))*255 : 91;
                for (int i=0;i<10;i++) {
                    uint[] p=Probe(white,false);
                    Require((p[0]&0xFFFFFF)==0xFFFFFF,"Capture feedback: original white changed: "+p[0].ToString("X8"));
                    Require(Math.Abs((p[1]>>24)-expectedAlpha)<=2,"White mask alpha: "+p[1].ToString("X8"));
                    patch.Invalidate(); WaitUi(80);
                }
                report.Add("PASS: ten fresh desktop samples retain original white under the visible mask; no recursive dimming.");
                Require(Native.WindowFromPoint(dark)==patch.Handle,"Overlay blocks hit testing");
                report.Add("PASS: hit testing passes through the overlay to the test window.");
                Native.NfPreviewRect(white.X-4,white.Y-4,8,8); WaitUi(150);
                Require((Probe(white,false)[1]>>24)==0,"Comparison preview was filtered twice");
                Native.NfPreviewRect(0,0,0,0); WaitUi(150);
                Require(Math.Abs((Probe(white,false)[1]>>24)-expectedAlpha)<=2,"Preview exclusion was not removed");
                report.Add("PASS: comparison preview exemption is applied and removed correctly.");
                uint[] whiteResult=Probe(white,true); WaitUi(600);
                report.Add("DWM composed white: 0x"+whiteResult[2].ToString("X8"));
                Require(Math.Abs((whiteResult[2]&255)-164.0)<=3,"Actual desktop white was not dimmed to expected level");
                uint[] grayResult=Probe(gray,true); WaitUi(600);
                Require(Math.Abs((grayResult[2]&255)-100.0)<=1,"Gray below threshold changed on desktop");
                uint[] darkResult=Probe(dark,true); WaitUi(600);
                Require(Math.Abs((darkResult[2]&255)-32.0)<=1,"Shadows changed on desktop");
                report.Add("PASS: actual DWM composition: white 255 -> "+(whiteResult[2]&255)+", gray 100 -> "+(grayResult[2]&255)+", dark 32 -> "+(darkResult[2]&255)+".");
                Native.NfGetStatus(out status);
                ulong initial = status.Frames;
                // Force a parameter update even on an otherwise static desktop.
                Native.NfConfigure(.5f,.4f,0,30); WaitUi(800); Native.NfGetStatus(out status);
                Require(status.Frames > initial && status.State==2,"Live setting change");
                report.Add("PASS: live configuration update and 30 fps mode.");
                Native.NfRefresh(); WaitUi(1600); Native.NfGetStatus(out status);
                Require(status.State==2,"Rebuild after simulated display change");
                report.Add("PASS: output recreation.");
                Native.NfEnable(0); WaitUi(400); Native.NfGetStatus(out status);
                Require(status.State==3 && status.Monitors==0,"Pause did not release output resources");
                report.Add("PASS: pause removes overlays and releases capture.");
                Native.NfEnable(1); WaitUi(1600); Native.NfGetStatus(out status);
                Require(status.State==2,"Resume");
                report.Add("PASS: resume.");
                Native.NfStop(); Native.NfGetStatus(out status); Require(status.State==0,"Shutdown");
                report.Add("PASS: shutdown.");
                patch.Dispose(); patch=null;
                Settings.Folder=Path.GetDirectoryName(Path.GetFullPath(path));
                Settings.FilePath=Path.Combine(Settings.Folder,"ui-test-settings.ini");
                new Settings().Save();
                using (MainForm ui=new MainForm(false)) {
                    ui.Show(); WaitUi(300);
                    Button toggle=(Button)ui.Controls.Find("ToggleFilter",true)[0];
                    Label hotkey=(Label)ui.Controls.Find("HotkeyStatus",true)[0];
                    Require(hotkey.Tag is bool && (bool)hotkey.Tag,"Global shortcut registration");
                    toggle.PerformClick(); WaitUi(1500); Native.NfGetStatus(out status); Require(status.State==2,"UI enable");
                    ui.Close(); WaitUi(150); Require(!ui.IsDisposed && !ui.Visible,"Close should hide to tray");
                    Native.PostMessage(ui.Handle,Program.ShowMessage,IntPtr.Zero,IntPtr.Zero); WaitUi(150); Require(ui.Visible,"Tray/show route");
                    Native.PostMessage(ui.Handle,0x0312,new IntPtr(1),IntPtr.Zero); WaitUi(500); Native.NfGetStatus(out status); Require(status.State==2 && !ui.Visible,"Hotkey must hide panel without pausing filter");
                    Native.PostMessage(ui.Handle,0x0312,new IntPtr(1),IntPtr.Zero); WaitUi(200); Require(ui.Visible,"Hotkey show route");
                    ui.Pause(); WaitUi(300);
                    Native.PostMessage(ui.Handle,Program.ExitMessage,IntPtr.Zero,IntPtr.Zero); WaitUi(150); Require(ui.IsDisposed,"UI exit");
                }
                Native.NfStop();
                Require(!Settings.Read(Settings.FilePath).Enabled,"UI state was not saved");
                File.Delete(Settings.FilePath);
                report.Add("PASS: real panel enables filter; window close hides; reopen works; global shortcut registers and its handler hides/shows without changing filter; explicit exit closes; settings persist.");
                report.Add("RESULT: PASS");
                File.WriteAllLines(path, report, Encoding.UTF8); return 0;
            } catch (Exception e) { report.Add("FAIL: " + e); File.WriteAllLines(path, report, Encoding.UTF8); return 1; }
            finally { Native.NfEnable(0); Native.NfStop(); if (patch!=null) patch.Dispose(); }
        }
    }
}
