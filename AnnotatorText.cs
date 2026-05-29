using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal sealed partial class AnnotatorForm
{
    private bool inlineTextEditing;
    private InlineImeTextBox inlineTextBox;
    private string inlineText = string.Empty;
    private string inlineCompositionText = string.Empty;
    private bool inlineTextSelecting;
    private int inlineTextSelectionAnchor;
    private bool inlineCaretVisible;
    private readonly Timer inlineCaretTimer = new Timer();
    private PointF textInputPosition;
    private Bitmap textMeasurementBitmap;
    private Graphics textMeasurementGraphics;
    private Font textMeasurementFont;
    private float textMeasurementFontSize = -1f;
    private StringFormat textMeasurementFormat;
    private string textWidthCacheText;
    private float textWidthCacheFontSize = -1f;
    private float[] textWidthCache;
    private string textSizeCacheText;
    private float textSizeCacheFontSize = -1f;
    private SizeF textSizeCache;
    private float textHeightCacheFontSize = -1f;
    private float textHeightCache;

    private sealed class InlineImeTextBox : TextBox
    {
        private const int WmImeComposition = 0x010F;
        private const int WmImeEndComposition = 0x010E;
        private const int GcsCompReadStr = 0x0001;
        private const int GcsCompStr = 0x0008;

        public event EventHandler CompositionChanged;
        public string CompositionText = string.Empty;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmImeComposition)
            {
                SetCompositionText(GetCurrentCompositionText(Handle));
            }
            else if (m.Msg == WmImeEndComposition)
            {
                SetCompositionText(string.Empty);
            }
        }

        private void SetCompositionText(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(CompositionText, value, StringComparison.Ordinal))
            {
                return;
            }

            CompositionText = value;
            EventHandler handler = CompositionChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static string GetCurrentCompositionText(IntPtr handle)
        {
            string readingText = GetCompositionString(handle, GcsCompReadStr);
            if (!string.IsNullOrEmpty(readingText))
            {
                return readingText;
            }
            return GetCompositionString(handle, GcsCompStr);
        }

        private static string GetCompositionString(IntPtr handle, int kind)
        {
            IntPtr context = ImmGetContext(handle);
            if (context == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                int byteCount = ImmGetCompositionString(context, kind, null, 0);
                if (byteCount <= 0)
                {
                    return string.Empty;
                }

                byte[] buffer = new byte[byteCount];
                int copied = ImmGetCompositionString(context, kind, buffer, buffer.Length);
                if (copied <= 0)
                {
                    return string.Empty;
                }

                return Encoding.Unicode.GetString(buffer, 0, copied).TrimEnd('\0');
            }
            finally
            {
                ImmReleaseContext(handle, context);
            }
        }

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
        private static extern int ImmGetCompositionString(IntPtr hIMC, int dwIndex, byte[] lpBuf, int dwBufLen);
    }

    private void DrawInlineTextInput(Graphics g, float scale)
    {
        float fontSize = Math.Max(1f, strokeWidth * 6f);
        string text = inlineText ?? string.Empty;
        string composition = inlineCompositionText ?? string.Empty;
        using (Font font = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold))
        using (Brush textBrush = new SolidBrush(strokeColor))
        {
            int selectionStart = GetInlineSelectionStart();
            int selectionLength = GetInlineSelectionLength();
            selectionStart = Math.Max(0, Math.Min(text.Length, selectionStart));
            selectionLength = Math.Max(0, Math.Min(text.Length - selectionStart, selectionLength));
            int caretIndex = GetInlineCaretIndex();
            float textHeight = MeasureInlineTextHeightCached(fontSize);

            if (composition.Length > 0)
            {
                int insertionIndex = selectionLength > 0 ? selectionStart : caretIndex;
                insertionIndex = Math.Max(0, Math.Min(text.Length, insertionIndex));
                int replaceEnd = selectionLength > 0 ? selectionStart + selectionLength : insertionIndex;
                string beforeText = text.Substring(0, insertionIndex);
                string afterText = text.Substring(replaceEnd);
                float beforeWidth = MeasureInlineTextWidthCached(beforeText, beforeText.Length, fontSize);
                float compositionWidth = MeasureInlineTextWidthCached(composition, composition.Length, fontSize);
                float compositionX = textInputPosition.X + beforeWidth;

                if (beforeText.Length > 0)
                {
                    g.DrawString(beforeText, font, textBrush, textInputPosition);
                }
                g.DrawString(composition, font, textBrush, new PointF(compositionX, textInputPosition.Y));
                if (afterText.Length > 0)
                {
                    g.DrawString(afterText, font, textBrush, new PointF(compositionX + compositionWidth, textInputPosition.Y));
                }
                using (Pen compositionPen = new Pen(strokeColor, Math.Max(0.8f, 1f / Math.Max(0.001f, scale))))
                {
                    float underlineY = textInputPosition.Y + textHeight - Math.Max(1.2f, 2f / Math.Max(0.001f, scale));
                    g.DrawLine(compositionPen, compositionX, underlineY, compositionX + Math.Max(1f, compositionWidth), underlineY);
                }

                if (inlineCaretVisible)
                {
                    DrawInlineCaret(g, compositionX + compositionWidth, textHeight, scale);
                }
                return;
            }

            DrawCommittedInlineText(g, font, textBrush, text, selectionStart, selectionLength, textHeight, fontSize);

            if (inlineCaretVisible && selectionLength == 0)
            {
                float caretX = textInputPosition.X + MeasureInlineTextWidthCached(text, caretIndex, fontSize);
                DrawInlineCaret(g, caretX, textHeight, scale);
            }
        }
    }

    private void DrawCommittedInlineText(
        Graphics g,
        Font font,
        Brush textBrush,
        string text,
        int selectionStart,
        int selectionLength,
        float textHeight,
        float fontSize)
    {
        if (selectionLength > 0)
        {
            float selectionX = textInputPosition.X + MeasureInlineTextWidthCached(text, selectionStart, fontSize);
            string selectedText = text.Substring(selectionStart, selectionLength);
            float selectionWidth = MeasureInlineTextWidthCached(selectedText, selectedText.Length, fontSize);
            using (Brush selectionBrush = new SolidBrush(Color.FromArgb(190, AppStyles.OptionSelectedForeground)))
            {
                g.FillRectangle(selectionBrush, selectionX, textInputPosition.Y, Math.Max(1f, selectionWidth), textHeight);
            }
        }

        if (text.Length > 0)
        {
            g.DrawString(text, font, textBrush, textInputPosition);
            if (selectionLength > 0)
            {
                string selectedText = text.Substring(selectionStart, selectionLength);
                float selectionX = textInputPosition.X + MeasureInlineTextWidthCached(text, selectionStart, fontSize);
                using (Brush selectedTextBrush = new SolidBrush(Color.White))
                {
                    g.DrawString(selectedText, font, selectedTextBrush, new PointF(selectionX, textInputPosition.Y));
                }
            }
        }
    }

    private void DrawInlineCaret(Graphics g, float caretX, float textHeight, float scale)
    {
        float caretWidth = Math.Max(1f, 1f / Math.Max(0.001f, scale));
        using (Pen caretPen = new Pen(strokeColor, caretWidth))
        {
            g.DrawLine(caretPen, caretX, textInputPosition.Y, caretX, textInputPosition.Y + textHeight);
        }
    }

    private RectangleF GetTextBounds(AnnotationItem item)
    {
        string text = item.Text ?? string.Empty;
        float fontSize = Math.Max(1f, item.StrokeWidth * 6f);
        if (text.Length == 0)
        {
            return new RectangleF(item.Start.X, item.Start.Y, fontSize, fontSize);
        }

        SizeF size = MeasureTextSizeCached(text, fontSize);
        return new RectangleF(item.Start, size);
    }

    private static StringFormat CreateInlineTextFormat()
    {
        StringFormat format = new StringFormat(StringFormat.GenericTypographic);
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return format;
    }

    private Graphics GetTextMeasurementGraphics()
    {
        if (textMeasurementBitmap == null)
        {
            textMeasurementBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
            textMeasurementGraphics = Graphics.FromImage(textMeasurementBitmap);
        }

        return textMeasurementGraphics;
    }

    private Font GetTextMeasurementFont(float fontSize)
    {
        fontSize = Math.Max(1f, fontSize);
        if (textMeasurementFont == null || Math.Abs(textMeasurementFontSize - fontSize) > 0.001f)
        {
            if (textMeasurementFont != null)
            {
                textMeasurementFont.Dispose();
            }

            textMeasurementFont = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold);
            textMeasurementFontSize = fontSize;
            ResetTextMeasurementCaches();
        }

        return textMeasurementFont;
    }

    private StringFormat GetTextMeasurementFormat()
    {
        if (textMeasurementFormat == null)
        {
            textMeasurementFormat = CreateInlineTextFormat();
        }

        return textMeasurementFormat;
    }

    private void ResetTextMeasurementCaches()
    {
        textWidthCacheText = null;
        textWidthCacheFontSize = -1f;
        textWidthCache = null;
        textSizeCacheText = null;
        textSizeCacheFontSize = -1f;
        textSizeCache = SizeF.Empty;
        textHeightCacheFontSize = -1f;
        textHeightCache = 0f;
    }

    private float MeasureInlineTextWidthCached(string text, int count, float fontSize)
    {
        text = text ?? string.Empty;
        if (text.Length == 0 || count <= 0)
        {
            return 0f;
        }

        count = Math.Min(count, text.Length);
        fontSize = Math.Max(1f, fontSize);
        Graphics graphics = GetTextMeasurementGraphics();
        Font font = GetTextMeasurementFont(fontSize);
        StringFormat format = GetTextMeasurementFormat();
        if (!string.Equals(textWidthCacheText, text, StringComparison.Ordinal) ||
            Math.Abs(textWidthCacheFontSize - fontSize) > 0.001f ||
            textWidthCache == null ||
            textWidthCache.Length != text.Length + 1)
        {
            textWidthCacheText = text;
            textWidthCacheFontSize = fontSize;
            textWidthCache = new float[text.Length + 1];
            for (int i = 1; i < textWidthCache.Length; i++)
            {
                textWidthCache[i] = -1f;
            }
        }

        if (textWidthCache[count] >= 0f)
        {
            return textWidthCache[count];
        }

        float width = MeasureInlineTextWidthCore(
            graphics,
            font,
            format,
            text,
            count);
        textWidthCache[count] = width;
        return width;
    }

    private float MeasureInlineTextHeightCached(float fontSize)
    {
        fontSize = Math.Max(1f, fontSize);
        if (Math.Abs(textHeightCacheFontSize - fontSize) <= 0.001f && textHeightCache > 0f)
        {
            return textHeightCache;
        }

        Graphics graphics = GetTextMeasurementGraphics();
        Font font = GetTextMeasurementFont(fontSize);
        StringFormat format = GetTextMeasurementFormat();
        textHeightCacheFontSize = fontSize;
        textHeightCache = graphics.MeasureString(
            "M",
            font,
            int.MaxValue,
            format).Height;
        return textHeightCache;
    }

    private SizeF MeasureTextSizeCached(string text, float fontSize)
    {
        text = text ?? string.Empty;
        fontSize = Math.Max(1f, fontSize);
        if (string.Equals(textSizeCacheText, text, StringComparison.Ordinal) &&
            Math.Abs(textSizeCacheFontSize - fontSize) <= 0.001f)
        {
            return textSizeCache;
        }

        Graphics graphics = GetTextMeasurementGraphics();
        Font font = GetTextMeasurementFont(fontSize);
        StringFormat format = GetTextMeasurementFormat();
        textSizeCacheText = text;
        textSizeCacheFontSize = fontSize;
        textSizeCache = graphics.MeasureString(
            text,
            font,
            int.MaxValue,
            format);
        return textSizeCache;
    }

    private static float MeasureInlineTextWidthCore(
        Graphics g,
        Font font,
        StringFormat format,
        string text,
        int count)
    {
        if (string.IsNullOrEmpty(text) || count <= 0)
        {
            return 0f;
        }

        count = Math.Min(count, text.Length);
        string measuredText = text.Substring(0, count);
        using (StringFormat measureFormat = (StringFormat)format.Clone())
        {
            measureFormat.SetMeasurableCharacterRanges(new CharacterRange[] { new CharacterRange(0, measuredText.Length) });
            RectangleF layout = new RectangleF(0, 0, 100000, 10000);
            Region[] regions = g.MeasureCharacterRanges(measuredText, font, layout, measureFormat);
            try
            {
                if (regions.Length > 0)
                {
                    RectangleF bounds = regions[0].GetBounds(g);
                    if (bounds.Right > 0)
                    {
                        return bounds.Right;
                    }
                }
            }
            finally
            {
                for (int i = 0; i < regions.Length; i++)
                {
                    regions[i].Dispose();
                }
            }
        }

        return g.MeasureString(measuredText, font, int.MaxValue, format).Width;
    }

    private float MeasureInlineEditingWidth(string text, string composition, float fontSize)
    {
        text = text ?? string.Empty;
        composition = composition ?? string.Empty;
        if (composition.Length == 0)
        {
            return Math.Max(fontSize, MeasureInlineTextWidthCached(text, text.Length, fontSize));
        }

        int selectionStart = Math.Max(0, Math.Min(text.Length, GetInlineSelectionStart()));
        int selectionLength = Math.Max(0, Math.Min(text.Length - selectionStart, GetInlineSelectionLength()));
        int caretIndex = Math.Max(0, Math.Min(text.Length, GetInlineCaretIndex()));
        int insertionIndex = selectionLength > 0 ? selectionStart : caretIndex;
        int replaceEnd = selectionLength > 0 ? selectionStart + selectionLength : insertionIndex;
        float beforeWidth = MeasureInlineTextWidthCached(text, insertionIndex, fontSize);
        float compositionWidth = MeasureInlineTextWidthCached(composition, composition.Length, fontSize);
        string afterText = text.Substring(replaceEnd);
        float afterWidth = MeasureInlineTextWidthCached(afterText, afterText.Length, fontSize);
        return Math.Max(fontSize, beforeWidth + compositionWidth + afterWidth);
    }

    private void ShowInlineTextInput(Point screenLocation)
    {
        if (inlineTextEditing)
        {
            HideInlineTextInput(true);
        }

        textInputPosition = ToImagePoint(screenLocation);
        inlineText = string.Empty;
        inlineCompositionText = string.Empty;
        inlineTextEditing = true;
        inlineTextSelecting = false;
        inlineTextSelectionAnchor = 0;
        ResetTextMeasurementCaches();
        inlineTextBox = CreateImeHostTextBox(screenLocation);
        inlineTextBox.KeyDown += InlineTextBox_KeyDown;
        inlineTextBox.KeyUp += InlineTextBox_KeyUp;
        inlineTextBox.TextChanged += InlineTextBox_TextChanged;
        inlineTextBox.CompositionChanged += InlineTextBox_CompositionChanged;
        canvas.Controls.Add(inlineTextBox);
        inlineTextBox.BringToFront();
        RestartInlineCaret();
        inlineTextBox.Focus();
        RequestCanvasRender();
    }

    private void HideInlineTextInput(bool saveText)
    {
        if (!inlineTextEditing)
        {
            return;
        }

        string text = inlineTextBox == null ? (inlineText ?? string.Empty).Trim() : inlineTextBox.Text.Trim();
        inlineTextEditing = false;
        inlineTextSelecting = false;
        inlineText = string.Empty;
        inlineCompositionText = string.Empty;
        inlineCaretVisible = false;
        inlineCaretTimer.Stop();
        ResetTextMeasurementCaches();
        DisposeInlineTextBox();

        if (saveText && !string.IsNullOrWhiteSpace(text))
        {
            var item = new AnnotationItem();
            item.Tool = ToolMode.Text;
            item.Start = textInputPosition;
            item.End = textInputPosition;
            item.StrokeColor = strokeColor;
            item.StrokeWidth = strokeWidth;
            item.Text = text;
            AddAnnotation(item);
        }
        if (!IsDisposed && canvas.IsHandleCreated)
        {
            canvas.Focus();
        }
        RequestCanvasRender();
    }

    private void DisposeInlineTextBox()
    {
        if (inlineTextBox == null)
        {
            return;
        }

        InlineImeTextBox textBox = inlineTextBox;
        inlineTextBox = null;
        try
        {
            textBox.KeyDown -= InlineTextBox_KeyDown;
            textBox.KeyUp -= InlineTextBox_KeyUp;
            textBox.TextChanged -= InlineTextBox_TextChanged;
            textBox.CompositionChanged -= InlineTextBox_CompositionChanged;
            if (textBox.Parent != null)
            {
                textBox.Parent.Controls.Remove(textBox);
            }

            Font hostFont = textBox.Font;
            textBox.Dispose();
            if (hostFont != null)
            {
                hostFont.Dispose();
            }
        }
        catch
        {
        }
    }

    private void DisposeInlineTextResources()
    {
        DisposeInlineTextBox();
        if (textMeasurementFont != null)
        {
            textMeasurementFont.Dispose();
            textMeasurementFont = null;
        }
        if (textMeasurementFormat != null)
        {
            textMeasurementFormat.Dispose();
            textMeasurementFormat = null;
        }
        if (textMeasurementGraphics != null)
        {
            textMeasurementGraphics.Dispose();
            textMeasurementGraphics = null;
        }
        if (textMeasurementBitmap != null)
        {
            textMeasurementBitmap.Dispose();
            textMeasurementBitmap = null;
        }
        ResetTextMeasurementCaches();
    }

    private void CommitInlineTextInput()
    {
        if (inlineTextEditing)
        {
            HideInlineTextInput(true);
        }
    }

    private bool CancelInlineTextInput()
    {
        if (!inlineTextEditing)
        {
            return false;
        }

        HideInlineTextInput(false);
        return true;
    }

    private bool IsPointInInlineTextInput(PointF point)
    {
        RectangleF bounds = GetInlineTextInputBounds();
        if (bounds.IsEmpty)
        {
            return false;
        }

        float tolerance = GetSelectionHitTolerance();
        bounds.Inflate(tolerance, tolerance);
        return bounds.Contains(point);
    }

    private RectangleF GetInlineTextInputBounds()
    {
        string text = inlineTextBox == null ? (inlineText ?? string.Empty) : inlineTextBox.Text;
        string composition = inlineCompositionText ?? string.Empty;
        float fontSize = Math.Max(1f, strokeWidth * 6f);
        float width = MeasureInlineEditingWidth(text, composition, fontSize);
        float height = MeasureInlineTextHeightCached(fontSize);
        return new RectangleF(textInputPosition.X, textInputPosition.Y, width, height);
    }

    private int GetInlineTextIndexAtPoint(PointF point)
    {
        string text = inlineTextBox == null ? (inlineText ?? string.Empty) : inlineTextBox.Text;
        string composition = inlineCompositionText ?? string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        float localX = point.X - textInputPosition.X;
        if (localX <= 0)
        {
            return 0;
        }

        float fontSize = Math.Max(1f, strokeWidth * 6f);
        if (composition.Length > 0)
        {
            int insertionIndex = Math.Max(0, Math.Min(text.Length, GetInlineSelectionStart()));
            float beforeWidth = MeasureInlineTextWidthCached(text, insertionIndex, fontSize);
            float compositionWidth = MeasureInlineTextWidthCached(composition, composition.Length, fontSize);
            if (localX >= beforeWidth && localX <= beforeWidth + compositionWidth)
            {
                return insertionIndex;
            }

            if (localX > beforeWidth + compositionWidth)
            {
                localX -= compositionWidth;
            }
        }

        float previousWidth = 0f;
        for (int i = 1; i <= text.Length; i++)
        {
            float width = MeasureInlineTextWidthCached(text, i, fontSize);
            if (localX <= (previousWidth + width) / 2f)
            {
                return i - 1;
            }
            previousWidth = width;
        }
        return text.Length;
    }

    private void BeginInlineTextSelection(PointF point)
    {
        if (inlineTextBox == null)
        {
            return;
        }

        int index = GetInlineTextIndexAtPoint(point);
        inlineTextSelectionAnchor = index;
        inlineTextSelecting = true;
        inlineTextBox.Select(index, 0);
        inlineTextBox.Focus();
        canvas.Cursor = Cursors.IBeam;
        canvas.Capture = true;
        SyncInlineTextInputState();
    }

    private void UpdateInlineTextSelection(PointF point)
    {
        if (inlineTextBox == null)
        {
            return;
        }

        int index = GetInlineTextIndexAtPoint(point);
        int start = Math.Min(inlineTextSelectionAnchor, index);
        int length = Math.Abs(index - inlineTextSelectionAnchor);
        inlineTextBox.Select(start, length);
        SyncInlineTextInputState();
    }

    private InlineImeTextBox CreateImeHostTextBox(Point screenLocation)
    {
        float fontSize = Math.Max(1f, strokeWidth * 6f);
        var textBox = new InlineImeTextBox();
        textBox.Multiline = false;
        textBox.BorderStyle = BorderStyle.None;
        textBox.AutoSize = false;
        textBox.ShortcutsEnabled = true;
        textBox.TabStop = false;
        textBox.Font = new Font(AppStyles.UiFontName, fontSize, FontStyle.Bold);
        textBox.BackColor = strokeColor;
        textBox.ForeColor = strokeColor;
        textBox.Left = Math.Max(0, Math.Min(canvas.ClientSize.Width - 1, screenLocation.X));
        textBox.Top = Math.Max(0, Math.Min(canvas.ClientSize.Height - 1, screenLocation.Y));
        textBox.Width = 1;
        textBox.Height = 1;
        return textBox;
    }

    private int GetInlineSelectionStart()
    {
        return inlineTextBox == null ? 0 : inlineTextBox.SelectionStart;
    }

    private int GetInlineSelectionLength()
    {
        return inlineTextBox == null ? 0 : inlineTextBox.SelectionLength;
    }

    private int GetInlineCaretIndex()
    {
        if (inlineTextBox == null)
        {
            return (inlineText ?? string.Empty).Length;
        }

        return Math.Max(0, Math.Min(inlineTextBox.TextLength, inlineTextBox.SelectionStart + inlineTextBox.SelectionLength));
    }

    private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (!HandleInlineTextKeyDown(e) && !IsDisposed && IsHandleCreated)
        {
            BeginInvoke((MethodInvoker)SyncInlineTextInputState);
        }
    }

    private void InlineTextBox_KeyUp(object sender, KeyEventArgs e)
    {
        SyncInlineTextInputState();
    }

    private void InlineTextBox_TextChanged(object sender, EventArgs e)
    {
        SyncInlineTextInputState();
    }

    private void InlineTextBox_CompositionChanged(object sender, EventArgs e)
    {
        SyncInlineTextInputState();
    }

    private void SyncInlineTextInputState()
    {
        if (inlineTextBox == null)
        {
            return;
        }

        inlineText = inlineTextBox.Text;
        inlineCompositionText = inlineTextBox.CompositionText;
        ResetTextMeasurementCaches();
        PositionImeHostAtCaret();
        RestartInlineCaret();
        RequestCanvasRender();
    }

    private void PositionImeHostAtCaret()
    {
        if (inlineTextBox == null)
        {
            return;
        }

        Point caret = GetInlineCaretCanvasPoint();
        inlineTextBox.Left = Math.Max(0, Math.Min(canvas.ClientSize.Width - 1, caret.X));
        inlineTextBox.Top = Math.Max(0, Math.Min(canvas.ClientSize.Height - 1, caret.Y));
    }

    private Point GetInlineCaretCanvasPoint()
    {
        RectangleF view = GetView();
        float scale = GetScale();
        float x = view.X + textInputPosition.X * scale;
        float y = view.Y + textInputPosition.Y * scale;
        string text = inlineText ?? string.Empty;
        string composition = inlineCompositionText ?? string.Empty;
        int caretIndex = GetInlineCaretIndex();
        float fontSize = Math.Max(1f, strokeWidth * 6f);
        if ((text.Length > 0 && caretIndex > 0) || composition.Length > 0)
        {
            if (composition.Length > 0)
            {
                int selectionStart = Math.Max(0, Math.Min(text.Length, GetInlineSelectionStart()));
                int selectionLength = Math.Max(0, Math.Min(text.Length - selectionStart, GetInlineSelectionLength()));
                int insertionIndex = selectionLength > 0 ? selectionStart : caretIndex;
                x += MeasureInlineTextWidthCached(text, insertionIndex, fontSize) * scale;
                x += MeasureInlineTextWidthCached(composition, composition.Length, fontSize) * scale;
            }
            else
            {
                x += MeasureInlineTextWidthCached(text, caretIndex, fontSize) * scale;
            }
        }
        return new Point((int)Math.Round(x), (int)Math.Round(y));
    }

    private void RestartInlineCaret()
    {
        inlineCaretVisible = true;
        inlineCaretTimer.Stop();
        inlineCaretTimer.Start();
    }

    private bool HandleInlineTextKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            HideInlineTextInput(true);
            SaveAndClose();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        if (e.Control && e.KeyCode == Keys.A && inlineTextBox != null)
        {
            inlineTextBox.SelectAll();
            SyncInlineTextInputState();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        if (e.KeyCode == Keys.Enter)
        {
            HideInlineTextInput(true);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        if (e.KeyCode == Keys.Escape)
        {
            HideInlineTextInput(false);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        return false;
    }
}
