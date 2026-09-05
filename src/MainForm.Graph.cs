using System;
using System.Drawing;
using System.Windows.Forms;

namespace NocnyFiltr {
    internal sealed partial class MainForm {
        void BuildGraphUi(float dpiScale) {
            graphToggle=ButtonAt(this,"▸ Live graph",12,34,336,24,delegate {ToggleGraph();});
            graphToggle.SetBounds((int)(12*dpiScale),(int)(34*dpiScale),(int)(336*dpiScale),(int)(24*dpiScale));
            foreach(Control control in Controls) if(control!=graphToggle && control.Top>32*dpiScale)control.Top+=(int)(30*dpiScale);
            graphPanel=new RoundedPanel {BackColor=Theme.Card,Visible=false};
            graphPanel.SetBounds((int)(12*dpiScale),(int)(62*dpiScale),(int)(336*dpiScale),(int)(174*dpiScale));Controls.Add(graphPanel);
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
            graphExpanded=!graphExpanded;int shift=(int)(180*layoutScale)*(graphExpanded?1:-1);
            foreach(Control control in Controls) if(control!=graphPanel && control!=graphToggle && control.Top>graphToggle.Bottom)control.Top+=shift;
            graphPanel.Visible=graphExpanded;graphToggle.Text=graphExpanded?"▾ Live graph":"▸ Live graph";
            liveGraph.Clear();AnchorPanel();
        }
    }
}
