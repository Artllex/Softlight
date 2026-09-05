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
    internal sealed partial class MainForm : Form {
        Settings settings;
        bool previewOnly, ready, exiting, suspended, hiddenOnce, resourcesDisposed;
        internal bool StartHidden;
        NotifyIcon tray;
        Icon moonIcon;
        PlayerBridge playerBridge;
        Slider strength;
        WindowList windowList;
        LiveGraph liveGraph;
        Panel graphPanel;
        DarkButton graphToggle;
        bool graphExpanded;
        System.Windows.Forms.Timer graphTimer;
        Label strengthValue, statusLabel, hotkeyLabel;
        DarkButton toggle; Slider speedSlider,suddenSlider; Label speedValue,suddenValue; ToolStripMenuItem frequencyMenu, eco, flashProtection;

        System.Windows.Forms.Timer poll, saveTimer;
        ToolStripMenuItem trayToggle;
        ToolStripMenuItem panelItem, languageItem, englishItem, polishItem, exitItem;
        ContextMenuStrip trayMenu;
        string T(string text) { return Language.Text(text,settings.Language); }
        bool hotkeyRegistered;
        PanelCloseButton closePanel;
        float layoutScale = 1;
        internal MainForm(bool previewOnly) {
            SuspendLayout();
            this.previewOnly = previewOnly;
            settings = previewOnly ? new Settings() : Settings.Load();
            Text = Program.Title; BackColor = Theme.Background; ForeColor = Theme.Text;
            Font = Theme.Font(10, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(360, 438); FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; MaximizeBox = false; StartPosition = FormStartPosition.Manual;
            moonIcon = new Icon(Path.Combine(Application.StartupPath,"assets","Softlight-tray.ico")); Icon = new Icon(Path.Combine(Application.StartupPath,"assets","Softlight.ico"));
            BuildUi();
            float dpiScale;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96f;
            Scale(new SizeF(dpiScale, dpiScale));


            Control windowsPanel = windowList.Parent;
            windowsPanel.Height += 30;
            windowList.Height += 30;
            foreach (Control control in Controls) if (control.Top > windowsPanel.Top) control.Top += 30;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + 30);
            layoutScale = dpiScale;
            BuildGraphUi(dpiScale);
            ResumeLayout(false);
            VisibleChanged += delegate { UpdatePreviewRect(); };
            LocationChanged += delegate { UpdatePreviewRect(); };
            SizeChanged += delegate { UpdatePreviewRect(); };
            Scroll += delegate { UpdatePreviewRect(); };
            Activated += delegate { UpdatePreviewRect(); };
            Deactivate += delegate { UpdatePreviewRect(); };
            if (!previewOnly) {
                if (Environment.OSVersion.Version.Build < 19041) throw new NotSupportedException("Windows 10 2004 or Windows 11 is required.");
                if (Native.NfStart() == 0) throw new InvalidOperationException("Could not start the display engine.");
                BuildTray();
                playerBridge = new PlayerBridge();
                saveTimer = new System.Windows.Forms.Timer { Interval = 500 };
                saveTimer.Tick += delegate { saveTimer.Stop(); Save(); };
                poll = new System.Windows.Forms.Timer { Interval = 500 }; poll.Tick += delegate { Poll(); }; poll.Start();
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
                SystemEvents.PowerModeChanged += OnPowerChanged;
            }
            ready = true; UpdateLanguage(); Apply(false);
            Shown += delegate {
                int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                if (!previewOnly) {
                    hotkeyRegistered = Native.RegisterHotKey(Handle, 1, 0x4000 | 0x0001, (uint)Keys.F11);
                    hotkeyLabel.Tag = hotkeyRegistered;
                    if (!hotkeyRegistered) hotkeyLabel.Text = T("Skrót jest zajęty — użyj ikony obok zegara.");
                    if (Settings.LoadWarning.Length > 0) statusLabel.Text = Settings.LoadWarning;
                }
                int rounded = 2; Native.DwmSetWindowAttribute(Handle, 33, ref rounded, 4);
                AnchorPanel();
                if (StartHidden && !settings.AlwaysOnTop && !hiddenOnce) { hiddenOnce = true; BeginInvoke((Action)delegate { Hide(); }); }
                UpdatePreviewRect();
            };
        }
        static Icon CreateIcon() {
            using (Bitmap b = new Bitmap(32, 32)) using (Graphics g = Graphics.FromImage(b)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush moon = new SolidBrush(Theme.Accent)) g.FillEllipse(moon, 3, 3, 26, 26);
                using (SolidBrush cut = new SolidBrush(Theme.Background)) g.FillEllipse(cut, 13, 0, 23, 23);
                IntPtr h = b.GetHicon(); try { return (Icon)Icon.FromHandle(h).Clone(); } finally { Native.DestroyIcon(h); }
            }
        }
        Label LabelAt(Control parent, string text, int x, int y, int w, int h, float size, Color color, bool bold) {
            Label label = new Label { Text = text, Tag = text, Location = new Point(x, y), Size = new Size(w, h),
                ForeColor = color, Font = Theme.Font(size, bold ? FontStyle.Bold : FontStyle.Regular), BackColor = Color.Transparent };
            parent.Controls.Add(label); return label;
        }
        DarkButton ButtonAt(Control parent, string text, int x, int y, int w, int h, EventHandler action) {
            DarkButton b = new DarkButton { Text = text, Tag = text, Location = new Point(x, y), Size = new Size(w, h) }; b.Click += action; parent.Controls.Add(b); return b;
        }
        void BuildUi() {
            Panel header=new Panel { Location=Point.Empty,Size=new Size(360,32),BackColor=Theme.Card };Controls.Add(header);
            LabelAt(header,"Softlight",14,5,300,24,10,Theme.Text,false);
            closePanel=new PanelCloseButton {Name="ClosePanel",Location=new Point(328,0),Size=new Size(32,32)};
            closePanel.Click+=delegate {HidePanel();};header.Controls.Add(closePanel);
            toggle=ButtonAt(this,"",12,40,336,50,delegate {settings.Enabled=!settings.Enabled;Apply(true);});toggle.Name="ToggleFilter";
            statusLabel=LabelAt(this,"",14,380,170,30,7.5f,Theme.Muted,false);
            LabelAt(this,"Show/hide panel (Alt + F11)",14,412,170,18,7.5f,Theme.Muted,false);
            Panel card=new RoundedPanel {Location=new Point(12,98),Size=new Size(336,62),BackColor=Theme.Card};Controls.Add(card);
            LabelAt(card,"Siła automatycznego przyciemniania",12,6,244,24,8.5f,Theme.Text,true);
            strengthValue=LabelAt(card,"",264,4,60,26,11,Theme.Accent,true);strengthValue.TextAlign=ContentAlignment.MiddleRight;
            strength=new Slider {Location=new Point(8,29),Size=new Size(320,28),Value=settings.Strength};card.Controls.Add(strength);
            strength.ValueChanged+=delegate {settings.Strength=strength.Value;Apply(true);};
            Panel speedCard=new RoundedPanel {Location=new Point(12,168),Size=new Size(336,62),BackColor=Theme.Card};Controls.Add(speedCard);
            LabelAt(speedCard,"Szybkość reakcji",12,6,244,24,8.5f,Theme.Text,true);
            speedValue=LabelAt(speedCard,"",264,4,60,26,11,Theme.Accent,true);speedValue.TextAlign=ContentAlignment.MiddleRight;
            speedSlider=new Slider {Name="ChangeSpeed",Maximum=100,CenterMark=true,Value=settings.Speed,Location=new Point(8,29),Size=new Size(320,28)};speedCard.Controls.Add(speedSlider);
            speedSlider.ValueChanged+=delegate {settings.Speed=speedSlider.Value;Apply(true);};
            Panel suddenCard=new RoundedPanel {Location=new Point(12,238),Size=new Size(336,62),BackColor=Theme.Card};Controls.Add(suddenCard);
            LabelAt(suddenCard,"Reakcja na nagłą zmianę",12,6,244,24,8.5f,Theme.Text,true);
            suddenValue=LabelAt(suddenCard,"",264,4,60,26,11,Theme.Accent,true);suddenValue.TextAlign=ContentAlignment.MiddleRight;
            suddenSlider=new Slider {Name="SuddenSpeed",Maximum=100,CenterMark=true,Value=settings.SuddenSpeed,Location=new Point(8,29),Size=new Size(320,28)};suddenCard.Controls.Add(suddenSlider);
            suddenSlider.ValueChanged+=delegate {settings.SuddenSpeed=suddenSlider.Value;Apply(true);};
            Panel windowsCard=new RoundedPanel {Location=new Point(12,308),Size=new Size(336,62),BackColor=Theme.Card};Controls.Add(windowsCard);
            LabelAt(windowsCard,"ROZPOZNANE OKNA",12,6,244,24,8.5f,Theme.Text,true);
            windowList=new WindowList {Location=new Point(8,30),Size=new Size(320,28),BackColor=Theme.Card,ForeColor=Theme.Muted,Font=Theme.Font(9,FontStyle.Regular)};windowsCard.Controls.Add(windowList);
            CheckBox auto=new ThemeCheckBox {Name="StartWithWindows",Text="Uruchamiaj z Windows",Tag="Uruchamiaj z Windows",Location=new Point(190,402),Size=new Size(158,21),Font=Theme.Font(8,FontStyle.Regular),ForeColor=Theme.Muted,FlatStyle=FlatStyle.Flat};
            if(!previewOnly) {try {auto.Checked=Settings.AutoStart;}catch {auto.Enabled=false;}}
            auto.CheckedChanged+=delegate {if(!previewOnly) {try {Settings.AutoStart=auto.Checked;}catch(Exception ex) {MessageBox.Show(ex.Message);}}};Controls.Add(auto);
            CheckBox pin=new ThemeCheckBox {Name="AlwaysOnTop",Text="Zawsze na wierzchu",Tag="Zawsze na wierzchu",Location=new Point(190,378),Size=new Size(158,21),Font=Theme.Font(8,FontStyle.Regular),ForeColor=Theme.Muted,FlatStyle=FlatStyle.Flat,Checked=settings.AlwaysOnTop};
            pin.CheckedChanged+=delegate {settings.AlwaysOnTop=pin.Checked;TopMost=pin.Checked;Apply(true);};Controls.Add(pin);
            TopMost=settings.AlwaysOnTop;
            hotkeyLabel=LabelAt(this,"Alt + F11: pokaż / ukryj panel",14,417,332,20,8,Theme.Muted,false);hotkeyLabel.Name="HotkeyStatus";hotkeyLabel.Visible=false;
        }
        void BuildTray() {
            trayMenu = new ContextMenuStrip { BackColor = Theme.Card, ForeColor = Theme.Text, ShowImageMargin = false, ShowCheckMargin = true, Renderer = new TrayRenderer() };
            panelItem = new ToolStripMenuItem("",null,delegate { TogglePanel(); });
            trayToggle = new ToolStripMenuItem("", null, delegate { settings.Enabled = !settings.Enabled; Apply(true); });
            languageItem = new ToolStripMenuItem();
            englishItem = new ToolStripMenuItem("English",null,delegate { SetLanguage("en"); });
            polishItem = new ToolStripMenuItem("Polski",null,delegate { SetLanguage("pl"); });
            languageItem.DropDownItems.AddRange(new ToolStripItem[]{englishItem,polishItem});
            languageItem.DropDown.Renderer = new TrayRenderer();
            languageItem.DropDown.BackColor = Theme.Card;
            exitItem = new ToolStripMenuItem("",null,delegate { Exit(); });
            flashProtection=new ToolStripMenuItem {Text=T("Ochrona przed błyskami"),CheckOnClick=true,Checked=settings.FlashProtection};
            flashProtection.Click+=delegate {settings.FlashProtection=flashProtection.Checked;Apply(true);};
            eco=new ToolStripMenuItem {Text=T("Oszczędzaj energię (30 kl./s)"),CheckOnClick=true,Checked=settings.Fps==30};
            eco.Click+=delegate {settings.Fps=eco.Checked?30:120;if(eco.Checked && settings.Frequency>30)settings.Frequency=30;Apply(true);};
            frequencyMenu=new ToolStripMenuItem();
            foreach(int hz in new int[]{120,60,30,12,4}) {
                int chosen=hz;
                ToolStripMenuItem item=new ToolStripMenuItem(hz+" Hz",null,delegate {SetFrequency(chosen);});item.Tag=hz;frequencyMenu.DropDownItems.Add(item);
            }
            frequencyMenu.DropDown.Renderer=new TrayRenderer();frequencyMenu.DropDown.BackColor=Theme.Card;
            trayMenu.Items.AddRange(new ToolStripItem[]{panelItem,trayToggle,new ToolStripSeparator(),frequencyMenu,eco,flashProtection,languageItem,new ToolStripSeparator(),exitItem});
            SpaceMenu(trayMenu);
            tray = new NotifyIcon { Icon = moonIcon, Text = "Softlight", Visible = true, ContextMenuStrip = trayMenu };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePanel(); };
        }
        internal void SetFrequency(int hz) {
            settings.Frequency=hz;
            if(hz>30) {settings.Fps=120;eco.Checked=false;}
            Apply(true);
        }
        internal void SetLanguage(string language) {
            settings.Language=language=="pl" ? "pl" : "en"; UpdateLanguage(); Apply(true);
        }
        void SpaceMenu(ToolStripDropDown menu) {
            int unit = Math.Max(1, (int)Math.Round(layoutScale));
            menu.Padding = new Padding(3 * unit, 4 * unit, 3 * unit, 4 * unit);
            foreach (ToolStripItem item in menu.Items) {
                if (item is ToolStripSeparator) item.Margin = new Padding(0, 3 * unit, 0, 3 * unit);
                else item.Padding = new Padding(5 * unit, 3 * unit, 8 * unit, 3 * unit);
                ToolStripMenuItem entry = item as ToolStripMenuItem;
                if (entry != null && entry.HasDropDownItems) SpaceMenu(entry.DropDown);
            }
        }
        internal void RenderMenu(string path) {
            if(trayMenu==null) BuildTray(); UpdateLanguage();
            trayMenu.Show(this,new Point(20,40)); exitItem.Select(); Application.DoEvents();
            using(Bitmap image=new Bitmap(trayMenu.Width,trayMenu.Height)) { trayMenu.DrawToBitmap(image,new Rectangle(Point.Empty,trayMenu.Size)); image.Save(path,ImageFormat.Png); }
            trayMenu.Close();
        }
        void UpdateLanguage() {
            closePanel.AccessibleName=T("Zamknij panel");
            Language.Apply(this,settings.Language);
            speedSlider.AccessibleName=T("Szybkość reakcji");suddenSlider.AccessibleName=T("Reakcja na nagłą zmianę");
            strength.AccessibleName=T("Siła automatycznego przyciemniania");
            hotkeyLabel.Text=T(hotkeyLabel.Tag is bool && !hotkeyRegistered ? "Skrót jest zajęty — użyj ikony obok zegara." : "Alt + F11: pokaż / ukryj panel");
            if(trayMenu!=null) {
                frequencyMenu.Text=T("Częstotliwość")+" · "+settings.Frequency+" Hz";foreach(ToolStripMenuItem item in frequencyMenu.DropDownItems)item.Checked=(int)item.Tag==settings.Frequency;
                flashProtection.Text=T("Ochrona przed błyskami");
                eco.Text=T("Oszczędzaj energię (30 kl./s)");
                panelItem.Text=T("Pokaż / ukryj panel");languageItem.Text=T("Język");exitItem.Text=T("Zakończ");
                englishItem.Checked=settings.Language=="en";polishItem.Checked=settings.Language=="pl";
                trayToggle.Text=T(settings.Enabled ? "Wyłącz filtr" : "Włącz filtr");
            }

        }
        void Apply(bool save) {
            if (!ready) return;
            hotkeyLabel.Text=settings.AlwaysOnTop?T("Panel przypięty — odznacz, aby ukrywać."):T(hotkeyLabel.Tag is bool && !hotkeyRegistered ? "Skrót jest zajęty — użyj ikony obok zegara.":"Alt + F11: pokaż / ukryj panel");
            speedValue.Text=Math.Pow(4,(settings.Speed-50)/50.0).ToString("0.##")+"×"; suddenValue.Text=settings.SuddenSpeed+"%";
            if(flashProtection!=null)flashProtection.Checked=settings.FlashProtection;
            if(eco!=null) eco.Checked=settings.Fps==30;
            if(frequencyMenu!=null) {frequencyMenu.Text=T("Częstotliwość")+" · "+settings.Frequency+" Hz";foreach(ToolStripMenuItem item in frequencyMenu.DropDownItems) item.Checked=(int)item.Tag==settings.Frequency;}
            strengthValue.Text=settings.Strength+"%"; strength.AccessibleDescription=strengthValue.Text;
            toggle.Selected=false;toggle.Text=settings.Enabled?T("Uruchamianie…"):T("○  Włącz filtr");toggle.Invalidate();
            if (!previewOnly) {
                Native.NfConfigure(settings.Threshold / 100f, settings.Strength / 100f, 0, settings.Fps);
                Native.NfFlashProtection(settings.FlashProtection?1:0);
                Native.NfTiming(settings.Frequency,settings.Speed,settings.SuddenSpeed);
                Native.NfEnable(settings.Enabled && !suspended && settings.Strength > 0 ? 1 : 0);
                trayToggle.Text = settings.Enabled ? T("Wyłącz filtr") : T("Włącz filtr");
                if (save) { saveTimer.Stop(); saveTimer.Start(); }
            }
            Poll();
        }
        string FormatWindowReport(string raw) { return WindowReport.FormatList(raw); }
        void Poll() {
            UpdatePreviewRect();
            if (!settings.Enabled || settings.Strength == 0) { windowList.Text=""; statusLabel.Text = T("Wstrzymany · oryginalny obraz"); toggle.Selected=false; toggle.Text=settings.Enabled ? T("Siła wynosi 0%") : T("○  Włącz filtr"); toggle.Invalidate(); if (tray != null) tray.Text = T("Nocny Filtr — wstrzymany"); return; }
            if (suspended) { statusLabel.Text = T("Wstrzymany · zablokowana sesja"); return; }
            if (previewOnly) { statusLabel.Text = T("Podgląd ustawień"); return; }
            System.Text.StringBuilder report=new System.Text.StringBuilder(4096);Native.NfWindowReport(report,report.Capacity);
            string display=FormatWindowReport(report.ToString());
            if(windowList.Text!=display) windowList.Text=display;
            EngineStatus status; Native.NfGetStatus(out status);
            if (status.Heartbeat != 0 && Native.GetTickCount64() - status.Heartbeat > 2500) {
                Pause(); statusLabel.Text = T("Filtr wyłączony: silnik nie odpowiada."); return;
            }
            toggle.Selected=status.State==2;
            toggle.Text=status.State==2 ? T("●  Filtr działa") : status.State==4 ? T("Filtr nie działa") : T("Uruchamianie…");
            toggle.Invalidate();
            if (status.State == 2) statusLabel.Text = T("Działa · ") + (status.HdrMonitors>0 ? T("HDR") : T("SDR")) + T(" · ekrany: ") + status.Monitors + T("\nDo ") + settings.Fps + T(" kl./s");
            else if (status.State == 4) statusLabel.Text = T("Brak dostępu do obrazu\nKod: 0x") + unchecked((uint)status.Error).ToString(T("X8"));
            else statusLabel.Text = T("Uruchamianie filtra…");
            if (tray != null) tray.Text = status.State == 2 ? T("Nocny Filtr — działa") : T("Nocny Filtr — oczekiwanie");
        }
        void Save() { try { settings.Save(); } catch (Exception ex) { statusLabel.Text = T("Nie zapisano ustawień: ") + ex.Message; } }
        void UpdatePreviewRect() { if(!previewOnly && ready) Native.NfPreviewRect(0,0,0,0); }
        internal void Pause() { settings.Enabled = false; Apply(true); }
        void AnchorPanel() {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int margin = Math.Max(4, (int)Math.Round(6 * layoutScale));
            int desiredWidth = (int)Math.Round(360 * layoutScale);
            int desiredHeight = (int)Math.Round((468+(graphExpanded?180:0)) * layoutScale) + 30;
            AutoScroll = desiredHeight > area.Height - margin*2 || desiredWidth > area.Width - margin*2;
            Size = new Size(Math.Min(desiredWidth, area.Width-margin*2), Math.Min(desiredHeight, area.Height-margin*2));
            Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);
            UpdatePreviewRect();
        }
        void ShowPanel() { WindowState = FormWindowState.Normal; AnchorPanel(); Show(); AnchorPanel(); Activate(); }
        void HidePanel() { Hide(); if(!previewOnly)Save(); }
        void TogglePanel() { if(Visible && !settings.AlwaysOnTop) Hide(); else ShowPanel(); }
        void Exit() { exiting = true; Native.NfEnable(0); Close(); }
        void OnSessionSwitch(object sender, SessionSwitchEventArgs e) {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)delegate {
                if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.RemoteDisconnect || e.Reason == SessionSwitchReason.ConsoleDisconnect) suspended = true;
                if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.RemoteConnect || e.Reason == SessionSwitchReason.ConsoleConnect) suspended = false;
                Apply(false);
            });
        }
        void OnDisplayChanged(object sender, EventArgs e) {
            if (!IsDisposed) Native.NfRefresh();
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { if (!IsDisposed) AnchorPanel(); });
        }
        void OnPowerChanged(object sender, PowerModeChangedEventArgs e) {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)delegate { if (e.Mode == PowerModes.Suspend) suspended = true; if (e.Mode == PowerModes.Resume) suspended = false; Apply(false); });
        }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x02E0 && ready) {
                float next = (m.WParam.ToInt64() & 0xFFFF) / 96f;
                Scale(new SizeF(next / layoutScale, next / layoutScale)); layoutScale = next;
                AnchorPanel(); return;
            }
            if (m.Msg == 0x001A && ready) BeginInvoke((Action)delegate { if (!IsDisposed) AnchorPanel(); });
            if (m.Msg == Program.ShowMessage) { ShowPanel(); return; }
            if (m.Msg == Program.ExitMessage) { Exit(); return; }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) { TogglePanel(); return; }
            base.WndProc(ref m);
        }
        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect { public int Left, Top, Right, Bottom; }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (!exiting && !previewOnly && e.CloseReason == CloseReason.UserClosing) {
                e.Cancel = true; HidePanel();
            } else {
                if (!previewOnly) { Native.NfEnable(0); Save(); }
                if (tray != null) tray.Visible = false;
            }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) {
            if (disposing && !resourcesDisposed) {
                resourcesDisposed = true;
                if (!previewOnly) {
                    Native.NfEnable(0);
                    SystemEvents.SessionSwitch -= OnSessionSwitch; SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; SystemEvents.PowerModeChanged -= OnPowerChanged;
                }
                if (hotkeyRegistered && IsHandleCreated) Native.UnregisterHotKey(Handle, 1);
                if (playerBridge != null) playerBridge.Dispose();
                if (graphTimer != null) graphTimer.Dispose();
                if (poll != null) poll.Dispose(); if (saveTimer != null) saveTimer.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); } if (moonIcon != null) moonIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
