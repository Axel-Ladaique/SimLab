using System.Globalization;
using Godot;

namespace SimLab.Game;

/// <summary>Small factory for the code-built UI, so every screen looks the same.</summary>
public static class Ui
{
    /// <summary>Secondary text over glass panels.</summary>
    public static readonly Color Muted = new(0.72f, 0.73f, 0.76f);
    /// <summary>Chip / selection blue (outline and live markers).</summary>
    public static readonly Color Accent = new(0.22f, 0.54f, 0.87f);
    /// <summary>The primary button's orange (targets, calls to action).</summary>
    public static readonly Color Orange = new(0.85f, 0.35f, 0.19f);
    /// <summary>Done / OK green.</summary>
    public static readonly Color Good = new(0.35f, 0.85f, 0.45f);
    /// <summary>Missing / wrong red.</summary>
    public static readonly Color Bad = new(0.95f, 0.40f, 0.35f);

    static readonly Color ChipFill = new(0.09f, 0.37f, 0.65f);

    public static string T(string key) => TranslationServer.Translate(key).ToString();

    public static VBoxContainer Screen(Control owner, string title)
    {
        owner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0.10f, 0.12f, 0.15f) };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        owner.AddChild(background);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        owner.AddChild(scroll);

        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 48);
        scroll.AddChild(margin);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);
        column.AddChild(Text(title, 34));
        return column;
    }

    public static Label Text(string text, int size = 18)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    /// <summary>Label meant to sit at the start of a Row next to a control (OptionButton, slider…):
    /// no word-wrap and a minimum width, so it never collapses to one letter per line.</summary>
    public static Label RowLabel(string text, int size = 18)
    {
        var label = Text(text, size);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.CustomMinimumSize = new Vector2(220, 0);
        return label;
    }

    public static Button Button(string text, System.Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(240, 44) };
        button.Pressed += pressed;
        return button;
    }

    public static HBoxContainer Slider(string label, double min, double max, double step, double value, System.Action<double> changed, string format,
        float nameWidth = 320, float sliderWidth = 360)
    {
        var name = Text(label);
        name.CustomMinimumSize = new Vector2(nameWidth, 0);
        var valueLabel = Text(value.ToString(format, CultureInfo.InvariantCulture));
        valueLabel.CustomMinimumSize = new Vector2(80, 0);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(sliderWidth, 24),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        slider.ValueChanged += v =>
        {
            valueLabel.Text = v.ToString(format, CultureInfo.InvariantCulture);
            changed(v);
        };
        return Row(name, slider, valueLabel);
    }

    public static CheckBox Check(string text, bool on, System.Action<bool> toggled)
    {
        var check = new CheckBox { Text = text, ButtonPressed = on };
        check.Toggled += v => toggled(v);
        return check;
    }

    public static HBoxContainer Row(params Control[] children)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        foreach (var child in children) row.AddChild(child);
        return row;
    }

    /// <summary>Dark translucent rounded panel laid over a 3D scene.</summary>
    public static StyleBoxFlat Glass(float alpha = 0.78f, int radius = 14, int padding = 20)
    {
        var box = new StyleBoxFlat { BgColor = new Color(0.07f, 0.08f, 0.10f, alpha) };
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(padding);
        return box;
    }

    static StyleBoxFlat Fill(Color color, int radius, int padX, int padY, Color? border = null)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(radius);
        box.ContentMarginLeft = box.ContentMarginRight = padX;
        box.ContentMarginTop = box.ContentMarginBottom = padY;
        if (border is { } b)
        {
            box.BorderColor = b;
            box.SetBorderWidthAll(1);
        }
        return box;
    }

    static void Style(Button button, StyleBox normal, StyleBox hover, StyleBox pressed)
    {
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("hover_pressed", pressed);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>Pill toggle for one choice among a few (the group keeps one pressed): outlined when off, blue when on.
    /// Not focusable, so the arrow keys keep flying the menu's live view.</summary>
    public static Button Chip(string text, ButtonGroup group)
    {
        var chip = new Button { Text = text, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
        Style(chip, ChipOff(), ChipHover(), ChipOn());
        chip.AddThemeFontSizeOverride("font_size", 16);
        return chip;
    }

    /// <summary>A plain (non-toggle, non-focusable) button shaped like <see cref="Chip"/>; its look is set with
    /// <see cref="ChipLook"/>, so the caller decides which chip is lit.</summary>
    public static Button ChipButton(string text, System.Action pressed)
    {
        var chip = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        chip.AddThemeFontSizeOverride("font_size", 16);
        ChipLook(chip, false);
        chip.Pressed += pressed;
        return chip;
    }

    /// <summary>Restyles a chip-shaped button: blue fill when <paramref name="on"/>, otherwise outlined (in
    /// <paramref name="outline"/> when given, with the text in that colour too).</summary>
    public static void ChipLook(Button chip, bool on, Color? outline = null)
    {
        if (on) Style(chip, ChipOn(), ChipOn(), ChipOn());
        else if (outline is { } c) Style(chip, Fill(new Color(c, 0.10f), 14, 14, 5, c), Fill(new Color(c, 0.20f), 14, 14, 5, c), ChipOn());
        else Style(chip, ChipOff(), ChipHover(), ChipOn());
        var text = !on && outline is { } t ? t : Colors.White;
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
            chip.AddThemeColorOverride(name, text);
    }

    /// <summary>Non-interactive pill tag: tinted fill and outline in <paramref name="color"/>, text in that colour.</summary>
    public static PanelContainer Pill(string text, Color color, int size = 15)
    {
        var pill = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pill.AddThemeStyleboxOverride("panel", Fill(new Color(color, 0.14f), 12, 12, 3, color));
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        pill.AddChild(label);
        return pill;
    }

    static StyleBoxFlat ChipOff() => Fill(new Color(1, 1, 1, 0.06f), 14, 14, 5, new Color(1, 1, 1, 0.25f));
    static StyleBoxFlat ChipHover() => Fill(new Color(1, 1, 1, 0.14f), 14, 14, 5, new Color(1, 1, 1, 0.40f));
    static StyleBoxFlat ChipOn() => Fill(ChipFill, 14, 14, 5, Accent);

    /// <summary>Styles a TabContainer's tab bar like <see cref="Chip"/>: outlined pill when unselected, blue fill
    /// when selected (same colours). Call once after building the tabs.</summary>
    public static void TabsAsChips(TabContainer tabs)
    {
        tabs.AddThemeStyleboxOverride("tab_unselected", Fill(new Color(1, 1, 1, 0.06f), 14, 16, 8, new Color(1, 1, 1, 0.25f)));
        tabs.AddThemeStyleboxOverride("tab_hovered", Fill(new Color(1, 1, 1, 0.14f), 14, 16, 8, new Color(1, 1, 1, 0.40f)));
        tabs.AddThemeStyleboxOverride("tab_selected", Fill(ChipFill, 14, 16, 8, Accent));
        tabs.AddThemeColorOverride("font_selected_color", Colors.White);
        tabs.AddThemeColorOverride("font_unselected_color", Colors.White);
        tabs.AddThemeColorOverride("font_hovered_color", Colors.White);
    }

    /// <summary>The one filled, coloured call to action of a screen.</summary>
    public static Button PrimaryButton(string text, System.Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 58), FocusMode = Control.FocusModeEnum.None };
        Style(button,
            Fill(Orange, 10, 20, 8),
            Fill(new Color(0.93f, 0.45f, 0.28f), 10, 20, 8),
            Fill(new Color(0.60f, 0.24f, 0.11f), 10, 20, 8));
        button.AddThemeStyleboxOverride("disabled", Fill(new Color(Orange, 0.30f), 10, 20, 8));
        button.AddThemeFontSizeOverride("font_size", 26);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", new Color(1, 1, 1, 0.45f));
        button.Pressed += pressed;
        return button;
    }

    /// <summary>Small translucent text button for secondary actions over a 3D scene.</summary>
    public static Button FlatButton(string text, System.Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), FocusMode = Control.FocusModeEnum.None };
        Style(button,
            Fill(new Color(0.07f, 0.08f, 0.10f, 0.62f), 8, 14, 6),
            Fill(new Color(0.16f, 0.18f, 0.22f, 0.80f), 8, 14, 6),
            Fill(new Color(0.03f, 0.04f, 0.05f, 0.85f), 8, 14, 6));
        button.AddThemeFontSizeOverride("font_size", 17);
        button.Pressed += pressed;
        return button;
    }
}
