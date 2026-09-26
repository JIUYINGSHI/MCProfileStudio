using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace McProfileStudio;

public class MinecraftTextBlock : TextBlock
{
    public static readonly DependencyProperty MinecraftTextProperty = DependencyProperty.Register(nameof(MinecraftText), typeof(string), typeof(MinecraftTextBlock), new PropertyMetadata("", (d, e) => ((MinecraftTextBlock)d).RenderText()));
    public string MinecraftText { get => (string)GetValue(MinecraftTextProperty); set => SetValue(MinecraftTextProperty, value); }
    private static readonly Dictionary<char, Color> Colors = new()
    {
        ['0']=Color.FromRgb(0,0,0),['1']=Color.FromRgb(0,0,170),['2']=Color.FromRgb(0,170,0),['3']=Color.FromRgb(0,170,170),['4']=Color.FromRgb(170,0,0),['5']=Color.FromRgb(170,0,170),['6']=Color.FromRgb(255,170,0),['7']=Color.FromRgb(170,170,170),
        ['8']=Color.FromRgb(85,85,85),['9']=Color.FromRgb(85,85,255),['a']=Color.FromRgb(85,255,85),['b']=Color.FromRgb(85,255,255),['c']=Color.FromRgb(255,85,85),['d']=Color.FromRgb(255,85,255),['e']=Color.FromRgb(255,255,85),['f']=Color.FromRgb(255,255,255)
    };
    private void RenderText()
    {
        Inlines.Clear(); var text = MinecraftText ?? ""; var color = Foreground; var bold = false; var italic = false; var underline = false; var strike = false; var buffer = "";
        void Flush() { if (buffer.Length == 0) return; var run = new Run(buffer) { Foreground = color, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal }; if (underline) run.TextDecorations.Add(System.Windows.TextDecorations.Underline[0]); if (strike) run.TextDecorations.Add(System.Windows.TextDecorations.Strikethrough[0]); Inlines.Add(run); buffer = ""; }
        for (var i = 0; i < text.Length; i++) { if (text[i] == '§' && i + 1 < text.Length) { Flush(); var code = char.ToLowerInvariant(text[++i]); if (Colors.TryGetValue(code, out var c)) { color = new SolidColorBrush(c); bold = italic = underline = strike = false; } else switch (code) { case 'l': bold = true; break; case 'o': italic = true; break; case 'n': underline = true; break; case 'm': strike = true; break; case 'r': color = Foreground; bold = italic = underline = strike = false; break; } } else buffer += text[i]; } Flush();
    }
}
