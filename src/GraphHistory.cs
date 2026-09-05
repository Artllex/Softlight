using System;
using System.Collections.Generic;

namespace NocnyFiltr {
    // Time is supplied by the caller, making retention and transitions deterministic.
    internal sealed class GraphHistory {
        internal const double DurationSeconds = 10;
        internal struct Sample {
            internal double Time;
            internal float Brightness, Dim;
            internal bool ContextChanged;
        }
        internal readonly List<Sample> Samples = new List<Sample>();
        internal bool Frozen;
        internal string Latest = "Waiting for player";
        string source = "";

        internal void Clear() {
            Samples.Clear();
            source = "";
            Latest = "Waiting for measurement";
        }

        internal void Observe(WindowReading reading, bool active, double now) {
            if (Frozen) return;
            if (!active) reading = null;
            string nextSource = reading == null ? "" : reading.Source;
            bool changed = Samples.Count > 0 && source != nextSource;
            source = nextSource;
            float brightness = reading == null ? float.NaN : reading.Brightness;
            float dim = reading == null ? float.NaN : reading.Dim;
            Samples.Add(new Sample { Time = now, Brightness = brightness, Dim = dim, ContextChanged = changed });
            Samples.RemoveAll(sample => sample.Time < now - DurationSeconds);
            string label = reading == null ? "Active window" : reading.Label;
            if (label.Length > 22) label = label.Substring(0, 21) + "…";
            Latest = float.IsNaN(brightness)
                ? (active ? "No visible active window" : "Filter paused")
                : label + " · Brightness " + brightness.ToString("0") + "%    Dim " + dim.ToString("0") + "%";
        }
    }
}
