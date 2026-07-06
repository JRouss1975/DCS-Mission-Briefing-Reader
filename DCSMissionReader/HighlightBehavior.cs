using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DCSMissionReader
{
    /// <summary>
    /// Attached property that renders text containing «...» markers
    /// (produced by the FTS snippet function) with the marked parts highlighted.
    /// </summary>
    public static class HighlightBehavior
    {
        private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A));

        public static readonly DependencyProperty HighlightedTextProperty =
            DependencyProperty.RegisterAttached(
                "HighlightedText",
                typeof(string),
                typeof(HighlightBehavior),
                new PropertyMetadata("", OnHighlightedTextChanged));

        public static string GetHighlightedText(DependencyObject obj) => (string)obj.GetValue(HighlightedTextProperty);
        public static void SetHighlightedText(DependencyObject obj, string value) => obj.SetValue(HighlightedTextProperty, value);

        private static void OnHighlightedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            tb.Inlines.Clear();
            string text = e.NewValue as string ?? "";
            if (text.Length == 0) return;

            int pos = 0;
            while (pos < text.Length)
            {
                int open = text.IndexOf('«', pos);
                if (open < 0)
                {
                    tb.Inlines.Add(new Run(text.Substring(pos)));
                    break;
                }
                int close = text.IndexOf('»', open + 1);
                if (close < 0)
                {
                    tb.Inlines.Add(new Run(text.Substring(pos)));
                    break;
                }
                if (open > pos)
                    tb.Inlines.Add(new Run(text.Substring(pos, open - pos)));
                tb.Inlines.Add(new Run(text.Substring(open + 1, close - open - 1))
                {
                    Foreground = HighlightBrush,
                    FontWeight = FontWeights.SemiBold
                });
                pos = close + 1;
            }
        }
    }
}
