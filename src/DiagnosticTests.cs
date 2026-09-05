using System;
using System.Globalization;
using System.Threading;

namespace NocnyFiltr {
    internal static class DiagnosticTests {
        static void Check(bool condition, string message) {
            if (!condition) throw new InvalidOperationException(message);
        }
        internal static void Run() {
            var culture=Thread.CurrentThread.CurrentCulture;
            try {
                Thread.CurrentThread.CurrentCulture=new CultureInfo("pl-PL");
                string report="52%  Firefox video\t35\t1:1:4\tactive\r\n0%  ChatGPT\t?\t2:0\r\nmalformed\r\n";
                var parsed=WindowReport.Parse(report);
                Check(parsed.Count==2 && parsed[0].Dim==52 && parsed[0].Brightness==35,"Native report parse");
                Check(float.IsNaN(parsed[1].Brightness),"Unavailable brightness must remain unknown");
                Check(WindowReport.ActiveReading(report).Source=="1:1:4","Active context identity");
                Check(WindowReport.FormatList(report)=="Player · Brightness 35% · Dim 52%\r\n0%  ChatGPT\r\n","Presentation must not expose protocol metadata");
                Check(WindowReport.Parse(null).Count==0 && WindowReport.Parse("bad% text").Count==0,"Invalid report safety");
                Check(WindowReport.Parse("10% X\t12.5\t1\tactive")[0].Brightness==12.5f,"Culture-independent protocol");
                var history=new GraphHistory();
                var a=parsed[0];
                history.Observe(a,true,0);
                history.Observe(a,true,1);
                var b=new WindowReading {Title="ChatGPT",Source="2:0",Brightness=5,Dim=0,Active=true};
                history.Observe(b,true,2);
                Check(history.Samples.Count==3 && !history.Samples[1].ContextChanged && history.Samples[2].ContextChanged,"Retained history and context boundary");
                a.Source="1:1:5";history.Observe(a,true,3);
                Check(history.Samples[3].ContextChanged,"Firefox tab generation boundary");
                history.Frozen=true;history.Observe(null,false,50);
                Check(history.Samples.Count==4,"Freeze preserves entire timeline");
                history.Frozen=false;history.Observe(null,true,10.5);
                Check(history.Samples.Count==4 && history.Samples[0].Time==1,"Ten-second retention");
                Check(float.IsNaN(history.Samples[3].Brightness),"Missing data creates a gap");
                history.Clear();history.Observe(a,true,11);
                Check(history.Samples.Count==1 && !history.Samples[0].ContextChanged,"Clear resets context");
            } finally {Thread.CurrentThread.CurrentCulture=culture;}
        }
    }
}
