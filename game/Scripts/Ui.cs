using System.Globalization;
using Godot;

namespace Symlab.Game;

/// <summary>Small factory for the code-built UI, so every screen looks the same.</summary>
public static class Ui
{
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

    public static Button Button(string text, System.Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(240, 44) };
        button.Pressed += pressed;
        return button;
    }

    public static HBoxContainer Slider(string label, double min, double max, double step, double value, System.Action<double> changed, string format)
    {
        var name = Text(label);
        name.CustomMinimumSize = new Vector2(320, 0);
        var valueLabel = Text(value.ToString(format, CultureInfo.InvariantCulture));
        valueLabel.CustomMinimumSize = new Vector2(80, 0);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(360, 24),
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
}
