using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace NocnyFiltr {
    internal sealed class LiveGraph : Control {
        struct Sample { internal double Time; internal float Brightness,Dim; internal bool ContextChanged; }
        readonly List<Sample> samples=new List<Sample>();
        readonly Stopwatch clock=Stopwatch.StartNew();
        internal bool Frozen;


        string source="";
        string latest="Waiting for player";
        internal LiveGraph() {DoubleBuffered=true;BackColor=Theme.Card;ForeColor=Theme.Muted;Font=Theme.Font(8,FontStyle.Regular);}
        internal void Clear() {samples.Clear();source="";latest="Waiting for measurement";Invalidate();}
        internal void Observe(string report,bool active) {
            if(Frozen)return;
            float brightness=float.NaN,dim=float.NaN;
            string chosen=null;
            if(active) foreach(string line in report.Split('\n')) {
                if(line.TrimEnd().EndsWith("\tactive")) {chosen=line;break;}
            }
            string[] parts=chosen==null?new string[0]:chosen.TrimEnd().Split('\t');
            string nextSource=parts.Length>=3?parts[2]:"";
            string label="Active window";
            if(parts.Length>0) {
                int percent=parts[0].IndexOf('%');
                if(percent>=0)label=parts[0].Substring(percent+1).Trim();
                if(label=="Firefox video")label="Player";
                else if(label.StartsWith("Firefox page:"))label="Page";
                if(label.Length>22)label=label.Substring(0,21)+"…";
            }
            bool changed=samples.Count>0 && source!=nextSource;
            source=nextSource;
            if(chosen!=null) {
                string[] fields=chosen.Split('\t');int percent=fields[0].IndexOf('%');float b,d;
                if(fields.Length>=2 && percent>0 && float.TryParse(fields[1].Trim(),out b) && float.TryParse(fields[0].Substring(0,percent),out d)) {brightness=b;dim=d;}
            }
            double now=clock.Elapsed.TotalSeconds;
            samples.Add(new Sample {Time=now,Brightness=brightness,Dim=dim,ContextChanged=changed});
            samples.RemoveAll(s=>s.Time<now-10);
            latest=float.IsNaN(brightness)?(active?"No visible active window":"Filter paused"):
                label+" · Brightness "+brightness.ToString("0")+"%    Dim "+dim.ToString("0")+"%";
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=g.DpiX/96f;
            var plot=new RectangleF(30*scale,26*scale,Math.Max(1,Width-40*scale),Math.Max(1,Height-46*scale));
            float ceiling=100;foreach(var s in samples)if(!float.IsNaN(s.Brightness))ceiling=Math.Max(ceiling,(float)Math.Ceiling(s.Brightness/100)*100);
            using(var grid=new Pen(Color.FromArgb(50,60,72))) for(int i=0;i<=4;i++) {float y=plot.Top+plot.Height*i/4;g.DrawLine(grid,plot.Left,y,plot.Right,y);}
            using(var text=new SolidBrush(ForeColor)) {
                g.DrawString(latest,Font,text,4*scale,2*scale);
                g.DrawString(ceiling.ToString("0"),Font,text,0,plot.Top);
                g.DrawString("0",Font,text,8*scale,plot.Bottom-12*scale);
                g.DrawString("−10 s",Font,text,plot.Left,plot.Bottom+2*scale);
                g.DrawString("now",Font,text,plot.Right-26*scale,plot.Bottom+2*scale);
            }
            double end=samples.Count>0?samples[samples.Count-1].Time:clock.Elapsed.TotalSeconds;
            using(var boundary=new Pen(Color.FromArgb(150,160,180),scale)) {
                boundary.DashStyle=DashStyle.Dash;
                foreach(var s in samples) if(s.ContextChanged) {
                    float x=plot.Right-(float)(end-s.Time)/10*plot.Width;
                    g.DrawLine(boundary,x,plot.Top,x,plot.Bottom);
                }
            }
            using(var bright=new Pen(Color.FromArgb(242,200,110),1.5f*scale))
            using(var dim=new Pen(Theme.Accent,1.5f*scale)) {
                for(int i=1;i<samples.Count;i++) {
                    var a=samples[i-1];var b=samples[i];
                    if(b.ContextChanged || b.Time-a.Time>.15 || float.IsNaN(a.Brightness)||float.IsNaN(b.Brightness))continue;
                    float x1=plot.Right-(float)(end-a.Time)/10*plot.Width,x2=plot.Right-(float)(end-b.Time)/10*plot.Width;
                    g.DrawLine(bright,x1,plot.Bottom-a.Brightness/ceiling*plot.Height,x2,plot.Bottom-b.Brightness/ceiling*plot.Height);
                    g.DrawLine(dim,x1,plot.Bottom-a.Dim/ceiling*plot.Height,x2,plot.Bottom-b.Dim/ceiling*plot.Height);
                }
            }
        }
    }
}
