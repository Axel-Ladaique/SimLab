using System.Collections.Generic;
using Godot;

namespace SimLab.Game.Radio;

/// <summary>
/// Modal "learn a switch" dialog: a dark scrim that swallows clicks and a centred glass box with the function to learn,
/// the instruction, one chip per position found so far, and Save (enabled from two positions) / Cancel.
/// The owner toggles <see cref="CanvasItem.Visible"/> and feeds <see cref="Display"/> while the pilot flips the switch.
/// </summary>
public partial class LearnDialog : Control
{
    Label _title = null!, _waiting = null!;
    HFlowContainer _positions = null!;
    Button _save = null!;
    System.Action _onSave = () => { }, _onCancel = () => { };
    int _shown = -1;
    bool _built;

    /// <summary>Opens the dialog for one function (callable again for the next one); the box is built once.</summary>
    public void Init(string functionName, System.Action save, System.Action cancel)
    {
        (_onSave, _onCancel) = (save, cancel);
        if (!_built) Build();
        _title.Text = string.Format(Ui.T("RADIO_LEARN_TITLE"), functionName);
        _shown = -1;
        Display(null);
    }

    void Build()
    {
        _built = true;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var scrim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Stop };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scrim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
        box.AddThemeStyleboxOverride("panel", Ui.Glass(0.92f, 14, 24));
        center.AddChild(box);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        box.AddChild(column);

        _title = Ui.Text("", 26);
        column.AddChild(_title);
        var prompt = Ui.Text(Ui.T("RADIO_LEARN_PROMPT"), 16);
        prompt.AddThemeColorOverride("font_color", Ui.Muted);
        column.AddChild(prompt);

        _positions = new HFlowContainer { CustomMinimumSize = new Vector2(0, 32) };
        _positions.AddThemeConstantOverride("h_separation", 8);
        _positions.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(_positions);
        _waiting = Ui.Text(Ui.T("RADIO_LEARN_WAITING"), 15);
        _waiting.AddThemeColorOverride("font_color", Ui.Muted);
        _positions.AddChild(_waiting);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var cancelButton = Ui.FlatButton(Ui.T("RADIO_CANCEL"), () => _onCancel());
        cancelButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _save = Ui.PrimaryButton(Ui.T("RADIO_SAVE"), () => _onSave());
        _save.CustomMinimumSize = new Vector2(180, 50);
        _save.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(Ui.Row(spacer, cancelButton, _save));
    }

    /// <summary>The positions found so far (raw values, lowest first; null or empty: none yet). Only their count is
    /// shown, as "Position 1", "Position 2"…</summary>
    public void Display(IReadOnlyList<double>? positions)
    {
        int count = positions?.Count ?? 0;
        if (count == _shown) return;
        _shown = count;
        foreach (var child in _positions.GetChildren())
        {
            if (child == _waiting) continue;
            _positions.RemoveChild(child);
            child.QueueFree();
        }
        _waiting.Visible = count == 0;
        for (int i = 0; i < count; i++)
            _positions.AddChild(Ui.Pill(string.Format(Ui.T("RADIO_POSITION"), i + 1), Ui.Accent, 16));
        _save.Disabled = count < 2;
    }
}
