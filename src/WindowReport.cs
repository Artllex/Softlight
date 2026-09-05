using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NocnyFiltr {
    // The native ABI stays text-based; parse it once into a shared UI model.
    internal sealed class WindowReading {
        internal string Title, Source;
        internal float Brightness, Dim;
        internal bool Active;
        internal bool IsPlayer { get { return Title == "Firefox video"; } }
        internal bool IsPage { get { return Title.StartsWith("Firefox page:", StringComparison.Ordinal); } }
        internal string Label { get { return IsPlayer ? "Player" : IsPage ? "Page" : Title; } }
    }

    internal static class WindowReport {
        internal static List<WindowReading> Parse(string report) {
            var readings = new List<WindowReading>();
            foreach (string line in (report ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                string[] fields = line.Split('\t');
                int percent = fields[0].IndexOf('%');
                float dim, brightness;
                if (percent < 0 || !TryNumber(fields[0].Substring(0, percent), out dim)) continue;
                if (fields.Length < 2 || !TryNumber(fields[1], out brightness)) brightness = float.NaN;
                readings.Add(new WindowReading {
                    Title = fields[0].Substring(percent + 1).Trim(),
                    Source = fields.Length >= 3 ? fields[2] : "",
                    Brightness = brightness, Dim = dim,
                    Active = fields.Length >= 4 && fields[fields.Length - 1].Trim() == "active"
                });
            }
            return readings;
        }

        static bool TryNumber(string text, out float value) {
            return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static WindowReading ActiveReading(string report) {
            return Parse(report).Find(reading => reading.Active);
        }

        internal static string FormatList(string report) {
            var firefox = new StringBuilder();
            var others = new StringBuilder();
            foreach (var reading in Parse(report)) {
                if (reading.IsPlayer || reading.IsPage) {
                    string brightness = float.IsNaN(reading.Brightness) ? "?" : reading.Brightness.ToString("0", CultureInfo.InvariantCulture);
                    firefox.AppendFormat(CultureInfo.InvariantCulture, "{0} · Brightness {1}% · Dim {2:0}%\r\n", reading.Label, brightness, reading.Dim);
                } else {
                    others.AppendFormat(CultureInfo.InvariantCulture, "{0:0}%  {1}\r\n", reading.Dim, reading.Title);
                }
            }
            return firefox.ToString() + others;
        }
    }
}
