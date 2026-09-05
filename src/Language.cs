using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NocnyFiltr {
    internal static class Language {
        static readonly Dictionary<string,string> English = new Dictionary<string,string> {
            {"Zamknij panel","Close panel"},
            {"Panel przypięty — odznacz, aby ukrywać.","Panel pinned — uncheck to allow hiding."},
            {"Standardowo · 4 Hz","Standard · 4 Hz"}, {"Na bieżąco · 30 Hz","Real-time · 30 Hz"}, {"Częstotliwość","Frequency"}, {"Szybkość reakcji","Speed"}, {"Reakcja na nagłą zmianę","Sudden change"}, {"Zawsze na wierzchu","Always on top"},
            {"Nocny Filtr · Okna","Softlight · Windows"},
            {"Siła automatycznego przyciemniania","Strength"},
            {"Jedna jasność dla całego okna — także cieni.","Uniform dimming for the whole window, including shadows."},
            {"ROZPOZNANE OKNA","Windows"},
            {"Procent oznacza przyciemnienie całego okna.","Percentage shows whole-window dimming."},
            {"Jasne okna są wykrywane automatycznie. Zmiany są płynne, aby film nie pulsował.","Bright windows are detected automatically. Dimming adjusts gradually for steady playback."},
            {"Nocny Filtr","Softlight"}, {"Próg jasności","Brightness threshold"},
            {"Siła przyciemniania","Dimming strength"}, {"Ciemniejsze piksele pozostają bez zmian.","Darker pixels stay unchanged."},
            {"Liniowa","Linear"}, {"Pochyła · łagodna","Soft curve"},
            {"Stałe nachylenie powyżej progu.","Constant slope above the threshold."},
            {"Łagodne wejście; coraz silniej ścina jasne odcienie.","Smooth transition; stronger dimming of highlights."},
            {"KRZYWA I PODGLĄD","CURVE AND PREVIEW"}, {"Uruchamiaj z Windows","Start with Windows"},
            {"Ochrona przed błyskami","Flash protection"},
            {"Oszczędzaj energię (30 kl./s)","Save power (30 fps)"},
            {"Alt + F11: pokaż / ukryj panel","Alt + F11: show / hide panel"},
            {"Skrót jest zajęty — użyj ikony obok zegara.","Shortcut unavailable — use the tray icon."},
            {"Otwórz ustawienia","Open settings"}, {"Pokaż / ukryj panel","Show / hide panel"},
            {"Włącz filtr","Enable filter"}, {"Wyłącz filtr","Disable filter"}, {"Zakończ","Exit"}, {"Język","Language"},
            {"○  Włącz filtr","○  Enable filter"}, {"Uruchamianie…","Starting…"},
            {"●  Filtr działa","Active"}, {"Filtr nie działa","Filter unavailable"},
            {"Wstrzymany · oryginalny obraz","Paused · original image"}, {"Siła wynosi 0%","Strength is 0%"},
            {"Wstrzymany · zablokowana sesja","Paused · session locked"}, {"Podgląd ustawień","Settings preview"},
            {"Filtr wyłączony: silnik nie odpowiada.","Filter stopped: engine is not responding."},
            {"Działa · ","Active · "}, {" · ekrany: "," · displays: "}, {"\nDo ","\nUp to "}, {" kl./s"," fps"},
            {"Brak dostępu do obrazu\nKod: 0x","Cannot access display\nCode: 0x"}, {"Uruchamianie filtra…","Starting filter…"},
            {"Biel 100% → ","White 100% → "}, {"% jasności obrazu","% image brightness"},
            {"Nocny Filtr — wstrzymany","Softlight — paused"}, {"Nocny Filtr — działa","Softlight — active"},
            {"Nocny Filtr — oczekiwanie","Softlight — waiting"}, {"Nie zapisano ustawień: ","Settings could not be saved: "},
            {"Nie udało się zmienić autostartu.\n","Could not change startup settings.\n"},
            {"Próg jasności w procentach","Brightness threshold in percent"}, {"Siła przyciemniania nad progiem w procentach","Dimming strength above threshold in percent"},
            {"ciemne","dark"}, {"jasne →","bright →"}, {"PRZED","BEFORE"}, {"PO","AFTER"}
        };
        internal static string Text(string value,string language) { string translated;return language=="en" && English.TryGetValue(value,out translated) ? translated : value; }
        internal static void Apply(Control control,string language) {
            if(control.Tag is string) control.Text=Text((string)control.Tag,language);
            foreach(Control child in control.Controls) Apply(child,language);
        }
    }
    internal sealed class TrayRenderer : ToolStripProfessionalRenderer {
        internal TrayRenderer() { RoundedEdges=false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.Card); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { using(SolidBrush b=new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b,e.AffectedBounds); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            using(SolidBrush b=new SolidBrush(e.Item.Selected && e.Item.Enabled ? Theme.Accent : Theme.Card)) e.Graphics.FillRectangle(b,new Rectangle(Point.Empty,e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            e.TextColor=!e.Item.Enabled ? Theme.Muted : e.Item.Selected ? Theme.Background : Theme.Text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor=e.Item.Selected ? Theme.Background : Theme.Text; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            Rectangle r=e.ImageRectangle;
            using(Pen p=new Pen(e.Item.Selected ? Theme.Background : Theme.Accent,2)) {
                e.Graphics.DrawLines(p,new Point[]{new Point(r.Left+2,r.Top+r.Height/2),new Point(r.Left+r.Width/2-1,r.Bottom-3),new Point(r.Right-2,r.Top+3)});
            }
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { using(Pen p=new Pen(Theme.Border)) e.Graphics.DrawLine(p,5,e.Item.Height/2,e.Item.Width-5,e.Item.Height/2); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using(Pen p=new Pen(Theme.Border)) e.Graphics.DrawRectangle(p,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1); }
    }
}
