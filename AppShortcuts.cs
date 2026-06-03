using System;

internal static class AppShortcuts
{
    public static bool TryGetToolbarCommand(int key, bool ctrl, bool shift, out ToolbarCommand command)
    {
        command = ToolbarCommand.ToolRect;
        if (shift)
        {
            return false;
        }

        if (!ctrl)
        {
            switch (key)
            {
                case Win32Api.VkD1:
                    command = ToolbarCommand.ToolRect;
                    return true;
                case Win32Api.VkD2:
                    command = ToolbarCommand.ToolEllipse;
                    return true;
                case Win32Api.VkD3:
                    command = ToolbarCommand.ToolArrow;
                    return true;
                case Win32Api.VkD4:
                    command = ToolbarCommand.ToolPen;
                    return true;
                case Win32Api.VkD5:
                    command = ToolbarCommand.ToolMosaic;
                    return true;
                case Win32Api.VkD6:
                    command = ToolbarCommand.ToolText;
                    return true;
                default:
                    return false;
            }
        }

        switch (key)
        {
            case Win32Api.VkZ:
                command = ToolbarCommand.Undo;
                return true;
            case Win32Api.VkD:
                command = ToolbarCommand.Clear;
                return true;
            case Win32Api.VkO:
                command = ToolbarCommand.Ocr;
                return true;
            case Win32Api.VkF:
                command = ToolbarCommand.Fit;
                return true;
            case Win32Api.VkP:
                command = ToolbarCommand.Pin;
                return true;
            case Win32Api.VkS:
                command = ToolbarCommand.Save;
                return true;
            default:
                return false;
        }
    }

    public static string GetTooltip(ToolbarCommand command)
    {
        string label = GetLabel(command);
        string shortcut = GetShortcutText(command);
        if (string.IsNullOrEmpty(shortcut))
        {
            return label;
        }
        return label + "  " + shortcut;
    }

    private static string GetLabel(ToolbarCommand command)
    {
        switch (command)
        {
            case ToolbarCommand.ToolRect:
                return UiText.Rect;
            case ToolbarCommand.ToolEllipse:
                return UiText.Ellipse;
            case ToolbarCommand.ToolArrow:
                return UiText.Arrow;
            case ToolbarCommand.ToolPen:
                return UiText.Pen;
            case ToolbarCommand.ToolMosaic:
                return UiText.Mosaic;
            case ToolbarCommand.ToolText:
                return UiText.Text;
            case ToolbarCommand.Ocr:
                return UiText.Ocr;
            case ToolbarCommand.Undo:
                return UiText.Undo;
            case ToolbarCommand.Clear:
                return UiText.Clear;
            case ToolbarCommand.Fit:
                return UiText.Fit;
            case ToolbarCommand.Pin:
                return UiText.Pin;
            case ToolbarCommand.Settings:
                return UiText.Settings;
            case ToolbarCommand.Cancel:
                return UiText.Cancel;
            case ToolbarCommand.Save:
                return UiText.Save;
            default:
                return string.Empty;
        }
    }

    private static string GetShortcutText(ToolbarCommand command)
    {
        switch (command)
        {
            case ToolbarCommand.ToolRect:
                return "1";
            case ToolbarCommand.ToolEllipse:
                return "2";
            case ToolbarCommand.ToolArrow:
                return "3";
            case ToolbarCommand.ToolPen:
                return "4";
            case ToolbarCommand.ToolMosaic:
                return "5";
            case ToolbarCommand.ToolText:
                return "6";
            case ToolbarCommand.Undo:
                return "Ctrl+Z";
            case ToolbarCommand.Clear:
                return "Ctrl+D";
            case ToolbarCommand.Fit:
                return "Ctrl+F";
            case ToolbarCommand.Pin:
                return "Ctrl+P";
            case ToolbarCommand.Ocr:
                return "Ctrl+O";
            case ToolbarCommand.Save:
                return "Ctrl+S";
            default:
                return string.Empty;
        }
    }
}
