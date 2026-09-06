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
    internal static class Theme {
        internal static Color Background = Color.FromArgb(19, 22, 28), Card = Color.FromArgb(28, 33, 41);
        internal static Color Text = Color.FromArgb(213, 219, 230), Muted = Color.FromArgb(143, 155, 172);
        internal static Color Accent = Color.FromArgb(116, 202, 177), Border = Color.FromArgb(47, 56, 69);
        internal static Font Font(float size, FontStyle style) { return new Font("Segoe UI", size, style); }
    }
    internal sealed class RoundedPanel : Panel {
        internal RoundedPanel() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e) {
            PaintSurface(this, e, BackColor);
        }
        internal static void PaintSurface(Control control, PaintEventArgs e, Color color) {
            int Width = control.Width, Height = control.Height;
            e.Graphics.Clear(control.Parent == null ? Theme.Background : control.Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float d = Math.Min(12f * e.Graphics.DpiX / 96f, Math.Min(Width, Height) - 1f);
            if (d <= 0) return;
            using (GraphicsPath path = new GraphicsPath()) {
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(Width - 1 - d, 0, d, d, 270, 90);
                path.AddArc(Width - 1 - d, Height - 1 - d, d, d, 0, 90);
                path.AddArc(0, Height - 1 - d, d, d, 90, 90);
                path.CloseFigure();
                using (SolidBrush brush = new SolidBrush(color)) e.Graphics.FillPath(brush, path);
            }
        }
    }
    internal sealed class DarkButton : Button {
        internal bool Selected;
        internal DarkButton() {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = Theme.Border; ForeColor = Theme.Text;
            Font = Theme.Font(10, FontStyle.Regular); Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e) {
            Color bg = Selected ? Theme.Accent : (ClientRectangle.Contains(PointToClient(Cursor.Position)) ? Color.FromArgb(58, 69, 83) : Theme.Border);
            RoundedPanel.PaintSurface(this, e, bg);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Selected ? Theme.Background : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }
    internal sealed class PanelCloseButton : Button {
        internal PanelCloseButton() {
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
            Cursor=Cursors.Hand;Text="×";TabStop=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override void OnPaint(PaintEventArgs e) {
            bool hover=ClientRectangle.Contains(PointToClient(Cursor.Position));
            e.Graphics.Clear(hover?Color.FromArgb(190,45,55):Theme.Card);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float radius=Width*.14f;
            using(var pen=new Pen(Theme.Text,Math.Max(1,Width/28f))) {
                e.Graphics.DrawLine(pen,Width/2f-radius,Height/2f-radius,Width/2f+radius,Height/2f+radius);
                e.Graphics.DrawLine(pen,Width/2f+radius,Height/2f-radius,Width/2f-radius,Height/2f+radius);
            }
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4));
        }
    }
    // Retain native CheckBox input and accessibility; draw a high-contrast glyph.
    internal sealed class ThemeCheckBox : CheckBox {
        internal ThemeCheckBox() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            Cursor=Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(BackColor);
            float scale=Height/21f;int box=Math.Max(12,(int)Math.Round(13*scale));
            var rect=new Rectangle(1,(Height-box)/2,box,box);
            using(var fill=new SolidBrush(Checked?(Enabled?Theme.Accent:Theme.Border):Theme.Background))e.Graphics.FillRectangle(fill,rect);
            using(var edge=new Pen(Checked?Theme.Accent:Theme.Muted,Math.Max(1,scale)))e.Graphics.DrawRectangle(edge,rect);
            if(Checked) {
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                using(var mark=new Pen(Enabled?Theme.Background:Theme.Muted,Math.Max(2,2*scale))) {
                    mark.StartCap=mark.EndCap=LineCap.Round;mark.LineJoin=LineJoin.Round;
                    e.Graphics.DrawLines(mark,new[]{new PointF(rect.Left+box*.22f,rect.Top+box*.51f),new PointF(rect.Left+box*.43f,rect.Top+box*.72f),new PointF(rect.Left+box*.80f,rect.Top+box*.28f)});
                }
            }
            var textRect=new Rectangle(box+(int)(5*scale),0,Width-box-(int)(5*scale),Height);
            TextRenderer.DrawText(e.Graphics,Text,Font,textRect,Enabled?ForeColor:Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,textRect,Theme.Text,BackColor);
        }
        protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    }
    internal sealed class Slider : Control {
        int value; internal int Maximum=95; internal bool CenterMark=false;
        internal event EventHandler ValueChanged;
        internal int Value {
            get { return value; }
            set { int next = Math.Max(0, Math.Min(Maximum, value)); if (this.value == next) return; this.value = next; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        }
        internal Slider() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Slider; BackColor = Theme.Card;
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Height / 35f;
            float left = 12 * scale, right = Width - left, y = Height / 2f, x = left + (right - left) * Value / (float)Maximum;
            using (Pen p = new Pen(Theme.Border, 5 * scale)) { p.StartCap = p.EndCap = LineCap.Round; e.Graphics.DrawLine(p, left, y, right, y); }
            using (Pen p = new Pen(Theme.Accent, 5 * scale)) { p.StartCap = p.EndCap = LineCap.Round; e.Graphics.DrawLine(p, left, y, x, y); }
            using (SolidBrush b = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(b, x - 8*scale, y - 8*scale, 16*scale, 16*scale);
            if(CenterMark) using(Pen mark=new Pen(Theme.Muted)) e.Graphics.DrawLine(mark,(left+right)/2,y-7*scale,(left+right)/2,y+7*scale);
            if (Focused) using (Pen p = new Pen(Theme.Text)) e.Graphics.DrawEllipse(p, x - 11*scale, y - 11*scale, 22*scale, 22*scale);
        }
        void FromMouse(int x) { double margin = 12 * Height / 35.0; Value = (int)Math.Round((x - margin) * Maximum / Math.Max(1, Width - margin*2)); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; FromMouse(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (Capture) FromMouse(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }
        protected override bool IsInputKey(Keys keyData) { Keys k = keyData & Keys.KeyCode; return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value -= e.Shift ? 5 : 1;
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value += e.Shift ? 5 : 1;
            if (e.KeyCode == Keys.Home) Value = 0; if (e.KeyCode == Keys.End) Value = Maximum;
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
    internal sealed class Preview : Control {
        internal Settings Settings;
        internal Preview(Settings s) { Settings = s; DoubleBuffered = true; BackColor = Theme.Card; }
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Width / 496f;
            g.ScaleTransform(scale, scale);
            float w = Width / scale;
            RectangleF plot = new RectangleF(34, 14, w - 55, 114);
            using (Pen grid = new Pen(Theme.Border)) {
                for (int i = 0; i <= 4; i++) {
                    float x = plot.Left + i * plot.Width / 4, y = plot.Top + i * plot.Height / 4;
                    g.DrawLine(grid, x, plot.Top, x, plot.Bottom); g.DrawLine(grid, plot.Left, y, plot.Right, y);
                }
            }
            double t = Settings.Threshold / 100.0, s = Settings.Strength / 100.0;
            using (Pen p = new Pen(Theme.Muted, 1)) { p.DashStyle = DashStyle.Dash; g.DrawLine(p, plot.Left, plot.Bottom, plot.Right, plot.Top); }
            float tx = plot.Left + (float)t * plot.Width;
            using (Pen p = new Pen(Color.FromArgb(100, Theme.Accent))) { p.DashStyle = DashStyle.Dash; g.DrawLine(p, tx, plot.Top, tx, plot.Bottom); }
            PointF[] pts = new PointF[201];
            for (int i = 0; i <= 200; i++) pts[i] = new PointF(plot.Left + i / 200f * plot.Width,
                plot.Bottom - (float)Tone.Map(i / 200.0, t, s, Settings.Curve) * plot.Height);
            using (Pen p = new Pen(Theme.Accent, 2.5f)) g.DrawLines(p, pts);
            using (Font font = new Font("Segoe UI", 10.67f, FontStyle.Regular, GraphicsUnit.Pixel)) using (SolidBrush b = new SolidBrush(Theme.Muted)) {
                g.DrawString("100", font, b, 1, plot.Top - 4); g.DrawString("0", font, b, 19, plot.Bottom - 8);
                g.DrawString(Language.Text("ciemne",Settings.Language), font, b, plot.Left, plot.Bottom + 4);
                g.DrawString(Language.Text("jasne →",Settings.Language), font, b, plot.Right - 51, plot.Bottom + 4);
                g.DrawString(Language.Text("PRZED",Settings.Language), font, b, 3, 161); g.DrawString(Language.Text("PO",Settings.Language), font, b, 3, 189);
            }
            Color[] colors = {Color.FromArgb(18, 21, 28), Color.FromArgb(49, 54, 63), Color.FromArgb(100, 106, 117), Color.FromArgb(165, 170, 179), Color.FromArgb(216, 222, 231), Color.White, Color.FromArgb(248, 212, 129), Color.FromArgb(146, 223, 244)};
            float sw = (w - 61) / colors.Length;
            for (int i = 0; i < colors.Length; ++i) {
                using (SolidBrush b = new SolidBrush(colors[i])) g.FillRectangle(b, 57 + i * sw, 158, sw - 4, 22);
                using (SolidBrush b = new SolidBrush(Tone.MapColor(colors[i], Settings))) g.FillRectangle(b, 57 + i * sw, 186, sw - 4, 22);
            }
        }
    }

}
