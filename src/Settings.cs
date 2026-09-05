using System;
using System.IO;
using System.Text;
using System.Globalization;
using Microsoft.Win32;

namespace NocnyFiltr {
    internal sealed class Settings {
        public int Threshold = 45, Strength = 70, Curve = 1, Fps = 120;
        public bool Enabled = false;
        public bool AlwaysOnTop = false; public int Frequency = 30; public int Speed = 75, SuddenSpeed = 30;
        public string Language = "en";
        internal bool HdrPreview = false;
        internal static string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NocnyFiltrWindows");
        internal static string FilePath = Path.Combine(Folder, "settings.ini");
        internal static string LoadWarning = "";
        internal static Settings Load() {
            return Read(FilePath);
        }
        internal static Settings Read(string path) {
            Settings s = new Settings();
            try {
                if (!File.Exists(path)) return s;
                foreach (string line in File.ReadAllLines(path)) {
                    string[] p = line.Split('='); int n;
                    if (p.Length == 2 && p[0] == "language") { s.Language = p[1] == "pl" ? "pl" : "en"; continue; }
                    if (p.Length != 2 || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                    if (p[0] == "threshold") s.Threshold = Math.Max(0, Math.Min(95, n));
                    if (p[0] == "strength") s.Strength = Math.Max(0, Math.Min(95, n));
                    if (p[0] == "analysisHz") s.Frequency = n==120||n==60||n==30||n==12||n==4 ? n : 30; if(p[0]=="speed") s.Speed=Math.Max(0,Math.Min(100,n)); if(p[0]=="suddenSpeed") s.SuddenSpeed=Math.Max(0,Math.Min(100,n)); if (p[0] == "curve") s.Curve = n == 0 ? 0 : 1;
                    if (p[0] == "fps") s.Fps = n <= 30 ? 30 : 120;
                    if (p[0] == "alwaysOnTop") s.AlwaysOnTop = n == 1;
                    if (p[0] == "enabled") s.Enabled = n == 1;
                }
            } catch (Exception e) { LoadWarning = "Nie udało się odczytać ustawień: " + e.Message; }
            return s;
        }
        internal void Save() { Write(FilePath); }
        internal void Write(string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            string text = string.Format(CultureInfo.InvariantCulture,
                "threshold={0}\nstrength={1}\ncurve={2}\nfps={3}\nenabled={4}\nlanguage={5}\nalwaysOnTop={6}\nanalysisHz={7}\nspeed={8}\nsuddenSpeed={9}\n", Threshold, Strength, Curve, Fps, Enabled ? 1 : 0, Language, AlwaysOnTop ? 1 : 0, Frequency, Speed, SuddenSpeed);
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }
        internal static bool AutoStart {
            get {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                    return key != null && key.GetValue("NocnyFiltrWindows") != null;
                }
            }
            set {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                    if (value) key.SetValue("NocnyFiltrWindows", "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --tray");
                    else key.DeleteValue("NocnyFiltrWindows", false);
                }
            }
        }
    }
}
