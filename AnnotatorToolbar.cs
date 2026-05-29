using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

internal sealed partial class AnnotatorForm
{
    private void BuildToolbar()
    {
        int toolbarX = AppStyles.ToolbarStartX;
        AddIconToolButton(ref toolbarX, ToolMode.Rect, "矩形");
        AddIconToolButton(ref toolbarX, ToolMode.Ellipse, "椭圆");
        AddIconToolButton(ref toolbarX, ToolMode.Arrow, "箭头");
        AddIconToolButton(ref toolbarX, ToolMode.Pen, "画笔");
        AddIconToolButton(ref toolbarX, ToolMode.Mosaic, "马赛克");
        AddIconToolButton(ref toolbarX, ToolMode.Text, "文字");
        AddToolbarSeparator(ref toolbarX);
        AddActionButton(ref toolbarX, ToolbarIconKind.Undo, "撤销", delegate
        {
            if (CancelInlineTextInput())
            {
                return;
            }

            UndoAnnotationAction();
        });
        AddActionButton(ref toolbarX, ToolbarIconKind.Clear, "清空", delegate
        {
            if (!ConfirmClearAnnotations())
            {
                return;
            }

            CancelInlineTextInput();
            ClearAnnotations();
        });
        AddActionButton(ref toolbarX, ToolbarIconKind.Fit, "适合窗口", delegate
        {
            CommitInlineTextInput();
            ResetZoom();
        });
        topMostButton = AddActionButton(ref toolbarX, ToolbarIconKind.Pin, "固定置顶", delegate
        {
            CommitInlineTextInput();
            ToggleTopMost();
        });
        AddToolbarSeparator(ref toolbarX);
        AddActionButton(ref toolbarX, ToolbarIconKind.Cancel, "取消", delegate { Close(); });
        AddActionButton(ref toolbarX, ToolbarIconKind.Save, "保存", delegate { SaveAndClose(); });
        UpdateToolButtons();
        BuildToolOptionsPanel();
    }

    private void AddIconToolButton(ref int left, ToolMode tool, string tooltip)
    {
        var button = CreateToolbarButton(GetToolIconKind(tool), tooltip);
        button.Left = left;
        button.Tag = tool;
        button.Click += delegate
        {
            CommitInlineTextInput();
            bool sameTool = currentTool == tool;
            currentTool = tool;
            UpdateToolButtons();
            if (ToolSupportsOptions(tool))
            {
                if (sameTool && toolOptionsPanel.Visible)
                {
                    toolOptionsPanel.Visible = false;
                }
                else
                {
                    ShowToolOptions(tool);
                }
            }
            else
            {
                toolOptionsPanel.Visible = false;
            }
        };
        toolButtons[tool] = button;
        toolbar.Controls.Add(button);
        left += AppStyles.ToolbarButtonSize + AppStyles.ToolbarButtonGap;
    }

    private ModernIconButton AddActionButton(ref int left, ToolbarIconKind iconKind, string tooltip, EventHandler onClick)
    {
        var button = CreateToolbarButton(iconKind, tooltip);
        button.Left = left;
        button.Click += onClick;
        toolbar.Controls.Add(button);
        left += AppStyles.ToolbarButtonSize + AppStyles.ToolbarButtonGap;
        return button;
    }

    private void ToggleTopMost()
    {
        TopMost = !TopMost;
        UpdateTopMostButton();
    }

    private void UpdateTopMostButton()
    {
        if (topMostButton == null)
        {
            return;
        }

        topMostButton.Selected = TopMost;
        toolbarToolTip.SetToolTip(topMostButton, TopMost ? "取消置顶" : "固定置顶");
        topMostButton.Invalidate();
    }

    private ModernIconButton CreateToolbarButton(ToolbarIconKind iconKind, string tooltip)
    {
        var button = new ModernIconButton(iconKind);
        button.Top = AppStyles.ToolbarButtonTop;
        toolbarToolTip.SetToolTip(button, tooltip);
        return button;
    }

    private void AddToolbarSeparator(ref int left)
    {
        left += 8;
        var separator = new Panel();
        separator.Left = left;
        separator.Top = AppStyles.ToolbarSeparatorTop;
        separator.Width = 1;
        separator.Height = AppStyles.ToolbarSeparatorHeight;
        separator.BackColor = AppStyles.ToolbarSeparator;
        toolbar.Controls.Add(separator);
        left += 17;
    }

    private static ToolbarIconKind GetToolIconKind(ToolMode tool)
    {
        switch (tool)
        {
            case ToolMode.Rect:
                return ToolbarIconKind.Rect;
            case ToolMode.Ellipse:
                return ToolbarIconKind.Ellipse;
            case ToolMode.Arrow:
                return ToolbarIconKind.Arrow;
            case ToolMode.Pen:
                return ToolbarIconKind.Pen;
            case ToolMode.Text:
                return ToolbarIconKind.Text;
            case ToolMode.Mosaic:
                return ToolbarIconKind.Mosaic;
            default:
                return ToolbarIconKind.Rect;
        }
    }

    private static bool ToolSupportsOptions(ToolMode tool)
    {
        return tool == ToolMode.Rect ||
            tool == ToolMode.Ellipse ||
            tool == ToolMode.Arrow ||
            tool == ToolMode.Pen ||
            tool == ToolMode.Text;
    }

    private void UpdateToolButtons()
    {
        foreach (KeyValuePair<ToolMode, Button> pair in toolButtons)
        {
            ModernIconButton button = pair.Value as ModernIconButton;
            if (button != null)
            {
                button.Selected = pair.Key == currentTool;
                button.Invalidate();
            }
        }
    }

    private void BuildToolOptionsPanel()
    {
        toolOptionsPanel.Width = AppStyles.ToolOptionsWidth;
        toolOptionsPanel.Height = AppStyles.ToolOptionsHeight;
        toolOptionsPanel.BackColor = AppStyles.ToolbarSurface;
        toolOptionsPanel.BorderStyle = BorderStyle.FixedSingle;
        toolOptionsPanel.Visible = false;
        Controls.Add(toolOptionsPanel);
        toolOptionsPanel.BringToFront();

        int x = 12;
        foreach (Color color in AppStyles.AnnotationPalette)
        {
            Button button = new Button();
            button.Left = x;
            button.Top = 12;
            button.Width = 24;
            button.Height = 24;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = AppStyles.OptionBorder;
            button.BackColor = color;
            button.Cursor = Cursors.Hand;
            button.Tag = color;
            button.Click += delegate(object sender, EventArgs e)
            {
                strokeColor = (Color)((Button)sender).Tag;
                UpdateOptionButtons();
            };
            colorButtons.Add(button);
            toolOptionsPanel.Controls.Add(button);
            x += 34;
        }

        x += 8;
        foreach (int width in AppStyles.StrokeWidths)
        {
            Button button = new Button();
            button.Left = x;
            button.Top = 9;
            button.Width = 34;
            button.Height = 30;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = AppStyles.ToolbarSurface;
            button.ForeColor = AppStyles.OptionText;
            button.Text = width.ToString();
            button.Cursor = Cursors.Hand;
            button.Tag = (float)width;
            button.Click += delegate(object sender, EventArgs e)
            {
                strokeWidth = (float)((Button)sender).Tag;
                UpdateOptionButtons();
            };
            widthButtons.Add(button);
            toolOptionsPanel.Controls.Add(button);
            x += 42;
        }

        UpdateOptionButtons();
    }

    private void ShowToolOptions(ToolMode tool)
    {
        Button button = toolButtons[tool];
        toolOptionsPanel.Left = Math.Max(8, Math.Min(button.Left, ClientSize.Width - toolOptionsPanel.Width - 8));
        toolOptionsPanel.Top = toolbar.Bottom - 1;
        toolOptionsPanel.Visible = true;
        toolOptionsPanel.BringToFront();
    }

    private void UpdateOptionButtons()
    {
        foreach (Button button in colorButtons)
        {
            Color color = (Color)button.Tag;
            bool selected = color.ToArgb() == strokeColor.ToArgb();
            button.FlatAppearance.BorderColor = selected ? AppStyles.OptionSelectedBorder : AppStyles.OptionBorder;
            button.FlatAppearance.BorderSize = selected ? 2 : 1;
        }

        foreach (Button button in widthButtons)
        {
            float width = (float)button.Tag;
            bool selected = Math.Abs(width - strokeWidth) < 0.1f;
            button.BackColor = selected ? AppStyles.OptionSelectedBackground : AppStyles.ToolbarSurface;
            button.ForeColor = selected ? AppStyles.OptionSelectedForeground : AppStyles.OptionText;
            button.FlatAppearance.BorderColor = selected ? AppStyles.OptionSelectedBorder : AppStyles.OptionBorder;
        }
    }
}
