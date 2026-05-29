using System.Drawing;

internal static class AppStyles
{
    public const string UiFontName = "Microsoft YaHei UI";
    public const string IconFontName = "Segoe UI";

    public static readonly Color AppBackground = Color.FromArgb(18, 18, 18);
    public static readonly Color CanvasBackground = Color.FromArgb(24, 24, 24);
    public static readonly Color ToolbarSurface = Color.White;
    public static readonly Color ToolbarBorder = Color.FromArgb(232, 235, 239);
    public static readonly Color ToolbarSeparator = Color.FromArgb(226, 230, 235);
    public static readonly Color ToolbarShadow = Color.FromArgb(32, 0, 0, 0);
    public static readonly Color ToolbarIcon = Color.FromArgb(31, 35, 40);
    public static readonly Color ToolbarIconDisabled = Color.FromArgb(170, 176, 184);
    public static readonly Color ToolbarSelectedBackground = Color.FromArgb(229, 242, 255);
    public static readonly Color ToolbarSelectedForeground = Color.FromArgb(0, 112, 224);
    public static readonly Color ToolbarPressedBackground = Color.FromArgb(232, 236, 242);
    public static readonly Color ToolbarHoverBackground = Color.FromArgb(245, 247, 250);
    public static readonly Color SaveAccent = Color.FromArgb(0, 172, 111);
    public static readonly Color CancelAccent = Color.FromArgb(255, 78, 78);

    public static readonly Color OptionSelectedBackground = Color.FromArgb(228, 241, 255);
    public static readonly Color OptionSelectedForeground = Color.FromArgb(0, 96, 201);
    public static readonly Color OptionSelectedBorder = Color.FromArgb(0, 120, 215);
    public static readonly Color OptionBorder = Color.FromArgb(190, 196, 204);
    public static readonly Color OptionText = Color.FromArgb(36, 39, 43);
    public static readonly Color ToolTipBackground = Color.White;
    public static readonly Color ToolTipText = Color.FromArgb(31, 35, 40);
    public static readonly Color ToolTipBorder = Color.FromArgb(210, 216, 224);
    public static readonly Color ToolTipShadow = Color.FromArgb(24, 0, 0, 0);

    public static readonly Color DefaultStroke = Color.FromArgb(255, 78, 78);
    public static readonly Color[] AnnotationPalette = new Color[]
    {
        Color.FromArgb(255, 78, 78),
        Color.FromArgb(0, 122, 255),
        Color.FromArgb(39, 174, 96),
        Color.FromArgb(255, 193, 7),
        Color.FromArgb(45, 48, 53),
        Color.White
    };

    public static readonly int[] StrokeWidths = new int[] { 2, 4, 8, 12 };

    public const int ToolbarHeight = 82;
    public const int ToolbarStartX = 32;
    public const int ToolbarButtonTop = 23;
    public const int ToolbarButtonSize = 36;
    public const int ToolbarButtonGap = 8;
    public const int ToolbarSeparatorTop = 27;
    public const int ToolbarSeparatorHeight = 28;
    public const int ToolOptionsWidth = 346;
    public const int ToolOptionsHeight = 48;
    public const int ToolTipHorizontalPadding = 10;
    public const int ToolTipVerticalPadding = 7;
    public const int ToolTipMaxWidth = 240;
    public const int ToolTipCornerRadius = 7;
    public const int AnimationFrameMilliseconds = 16;
}
