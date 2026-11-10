using Godot;

namespace Galaxus.Ui;

/// <summary>Shared look for the station and map screens: small pixel text, dark panels, HUD green.</summary>
public static class Ui
{
    public static readonly Color Green = new(0.45f, 1f, 0.55f);
    public static readonly Color Dim = new(0.55f, 1f, 0.62f, 0.62f);
    public static readonly Color Amber = new(1f, 0.75f, 0.3f);
    public static readonly Color Red = new(1f, 0.4f, 0.35f);
    public static readonly Color Background = new(0.02f, 0.05f, 0.04f, 0.94f);

    private static Theme? _theme;

    public static Theme Theme => _theme ??= BuildTheme();

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFont = ThemeDB.FallbackFont, DefaultFontSize = 8 };

        var panel = Box(Background, Dim, 1);
        theme.SetStylebox("panel", "PanelContainer", panel);
        theme.SetStylebox("panel", "Panel", panel);
        theme.SetStylebox("panel", "TabContainer", Box(new Color(0, 0, 0, 0.3f), Dim, 1));
        theme.SetStylebox("panel", "Tree", Box(new Color(0, 0, 0, 0.3f), new Color(0, 0, 0, 0), 0));
        theme.SetStylebox("selected", "Tree", Box(new Color(0.45f, 1f, 0.55f, 0.25f), new Color(0, 0, 0, 0), 0));
        theme.SetStylebox("selected_focus", "Tree", Box(new Color(0.45f, 1f, 0.55f, 0.35f), new Color(0, 0, 0, 0), 0));
        theme.SetStylebox("focus", "Tree", new StyleBoxEmpty());
        theme.SetStylebox("normal", "RichTextLabel", new StyleBoxEmpty());

        foreach (var (state, fill) in new[] { ("normal", 0.12f), ("hover", 0.25f), ("pressed", 0.4f), ("focus", 0.12f), ("disabled", 0.04f) })
            theme.SetStylebox(state, "Button", Box(new Color(0.45f, 1f, 0.55f, fill), Dim, 1, padding: 3));

        foreach (var (state, fill) in new[] { ("tab_selected", 0.3f), ("tab_unselected", 0.08f), ("tab_hovered", 0.18f) })
            theme.SetStylebox(state, "TabContainer", Box(new Color(0.45f, 1f, 0.55f, fill), Dim, 1, padding: 3));

        foreach (var type in new[] { "Label", "Button", "Tree", "TabContainer", "RichTextLabel" })
        {
            theme.SetColor("font_color", type, Green);
            theme.SetFontSize("font_size", type, 8);
        }
        theme.SetColor("default_color", "RichTextLabel", Green);
        theme.SetColor("font_disabled_color", "Button", Dim);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_selected_color", "Tree", Colors.White);
        theme.SetColor("title_button_color", "Tree", Amber);
        theme.SetColor("font_selected_color", "TabContainer", Colors.White);
        theme.SetColor("font_unselected_color", "TabContainer", Dim);
        theme.SetColor("font_hovered_color", "TabContainer", Green);
        theme.SetFontSize("title_button_font_size", "Tree", 8);
        theme.SetStylebox("title_button_normal", "Tree", Box(new Color(0, 0, 0, 0.4f), new Color(0, 0, 0, 0), 0));
        theme.SetConstant("v_separation", "Tree", 1);
        theme.SetConstant("separation", "VBoxContainer", 3);
        theme.SetConstant("separation", "HBoxContainer", 4);
        return theme;
    }

    private static StyleBoxFlat Box(Color fill, Color border, int borderWidth, int padding = 2)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        box.SetBorderWidthAll(borderWidth);
        box.SetContentMarginAll(padding);
        return box;
    }

    public static Label Label(string text, Color? colour = null, bool wrap = false)
    {
        var label = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        if (colour != null) label.AddThemeColorOverride("font_color", colour.Value);
        return label;
    }

    public static Button Button(string text, Action onPressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += () => AudioManager.Instance?.Ui("click");
        button.Pressed += onPressed;
        return button;
    }

    public static Tree Table(params (string Title, int Width)[] columns)
    {
        var tree = new Tree
        {
            Columns = columns.Length,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        for (int i = 0; i < columns.Length; i++)
        {
            tree.SetColumnTitle(i, columns[i].Title);
            tree.SetColumnTitleAlignment(i, HorizontalAlignment.Left);
            if (columns[i].Width > 0)
            {
                tree.SetColumnExpand(i, false);
                tree.SetColumnCustomMinimumWidth(i, columns[i].Width);
            }
        }
        return tree;
    }

    public static RichTextLabel Text()
    {
        return new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectionEnabled = false,
        };
    }

    public static string Hex(Color c) => c.ToHtml(false);
}
