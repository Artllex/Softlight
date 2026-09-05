using System;
using System.Drawing;
using System.Windows.Forms;
namespace NocnyFiltr {
    internal sealed class WindowList : Control {
        int offset;
        internal WindowList() { DoubleBuffered=true;TabStop=true; }
        string[] Lines { get {return (Text??"").Split(new[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries);} }
        int Rows {get {return Math.Max(1,Height/Math.Max(1,Font.Height+2));}}
        protected override void OnTextChanged(EventArgs e) {base.OnTextChanged(e);offset=Math.Min(offset,Math.Max(0,Lines.Length-Rows));Invalidate();}
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(BackColor);string[] lines=Lines;int rowHeight=Font.Height+2;
            for(int i=offset;i<lines.Length && i-offset<Rows;i++)
                TextRenderer.DrawText(e.Graphics,lines[i],Font,new Rectangle(2,(i-offset)*rowHeight,Math.Max(0,Width-4),rowHeight),ForeColor,
                    TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter);
        }
        void ScrollRows(int delta) {offset=Math.Max(0,Math.Min(Math.Max(0,Lines.Length-Rows),offset+delta));Invalidate();}
        protected override void OnMouseWheel(MouseEventArgs e) {ScrollRows(e.Delta>0?-1:1);base.OnMouseWheel(e);}
        protected override void OnMouseDown(MouseEventArgs e) {Focus();base.OnMouseDown(e);}
        protected override bool IsInputKey(Keys keyData) {return keyData==Keys.Up||keyData==Keys.Down||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e) {if(e.KeyCode==Keys.Down)ScrollRows(1);if(e.KeyCode==Keys.Up)ScrollRows(-1);base.OnKeyDown(e);}
    }
}
