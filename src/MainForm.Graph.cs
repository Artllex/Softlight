using System;
using System.Drawing;
using System.Windows.Forms;

namespace NocnyFiltr {
    internal sealed partial class MainForm {
        void BuildGraphUi(float dpiScale) {
            toggle.SetBounds((int)(12*dpiScale),(int)(40*dpiScale),(int)(164*dpiScale),(int)(50*dpiScale));
            graphToggle=ButtonAt(this,"▸ Live graph",184,40,164,50,delegate {ToggleGraph();});
            graphToggle.SetBounds((int)(184*dpiScale),(int)(40*dpiScale),(int)(164*dpiScale),(int)(50*dpiScale));
            graphPanel=new RoundedPanel {BackColor=Theme.Card,Visible=false};
            graphPanel.SetBounds((int)(12*dpiScale),(int)(40*dpiScale),(int)(336*dpiScale),(int)(174*dpiScale));Controls.Add(graphPanel);
            liveGraph=new LiveGraph();liveGraph.SetBounds(0,(int)(28*dpiScale),graphPanel.Width,(int)(142*dpiScale));graphPanel.Controls.Add(liveGraph);
            var target=new DarkButton {Text="Auto",Font=Theme.Font(8,FontStyle.Regular)};
            target.SetBounds((int)(8*dpiScale),(int)(3*dpiScale),(int)(85*dpiScale),(int)(24*dpiScale));
            target.Click+=delegate {liveGraph.Clear();};graphPanel.Controls.Add(target);
            var freeze=new CheckBox {Text="Freeze",ForeColor=Theme.Muted,Font=Theme.Font(8,FontStyle.Regular)};freeze.SetBounds((int)(106*dpiScale),(int)(3*dpiScale),(int)(72*dpiScale),(int)(24*dpiScale));
            freeze.CheckedChanged+=delegate {liveGraph.Frozen=freeze.Checked;};graphPanel.Controls.Add(freeze);
            var legend=new Label {Text="Brightness",ForeColor=Color.FromArgb(242,200,110),Font=Theme.Font(7,FontStyle.Regular)};legend.SetBounds((int)(192*dpiScale),(int)(7*dpiScale),(int)(82*dpiScale),(int)(20*dpiScale));graphPanel.Controls.Add(legend);
            var dimLegend=new Label {Text="Dim",ForeColor=Theme.Accent,Font=Theme.Font(7,FontStyle.Regular)};dimLegend.SetBounds((int)(282*dpiScale),(int)(7*dpiScale),(int)(45*dpiScale),(int)(20*dpiScale));graphPanel.Controls.Add(dimLegend);dimLegend.BringToFront();
            graphTimer=new System.Windows.Forms.Timer {Interval=33};
            graphTimer.Tick+=delegate {if(!previewOnly && Visible && graphExpanded && !liveGraph.Frozen) {liveGraph.ReadNative(settings.Enabled && settings.Strength>0 && !suspended);}};
            graphTimer.Start();
        }
        internal void ToggleGraph() {
            // Present the final layout once, instead of exposing the intermediate
            // child positions, resized window and subsequent screen clamping.
            bool redraw=IsHandleCreated && Visible;
            if(redraw)SendMessage(Handle,0x000B,IntPtr.Zero,IntPtr.Zero);
            SuspendLayout();
            try {
                graphExpanded=!graphExpanded;int shift=(int)(180*layoutScale)*(graphExpanded?1:-1);
                foreach(Control control in Controls) if(control!=graphPanel && control.Top>32*layoutScale)control.Top+=shift;
                graphPanel.Visible=graphExpanded;graphToggle.Text=graphExpanded?"▾ Live graph":"▸ Live graph";
                liveGraph.Clear();AnchorPanel();
            } finally {
                ResumeLayout(true);
                if(redraw) {
                    SendMessage(Handle,0x000B,new IntPtr(1),IntPtr.Zero);
                    RedrawWindow(Handle,IntPtr.Zero,IntPtr.Zero,0x0585);
                }
            }
        }
    }
}
