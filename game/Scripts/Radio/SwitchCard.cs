using System.Collections.Generic;
using Godot;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>
/// One assigned switch: a glass card with the function, its source (channel or button) and a × to remove it, then one
/// chip per switch position naming the state it selects. The chip of the position the switch rests in lights up blue.
/// </summary>
public partial class SwitchCard : PanelContainer
{
    readonly List<Button> _chips = [];
    int? _lit;

    /// <summary>Builds (or rebuilds, after a state change) the card. <paramref name="chipClicked"/> gets the position
    /// index; <paramref name="clear"/> is the × button.</summary>
    public void Init(SwitchAssignment assignment, System.Action<int> chipClicked, System.Action clear)
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _chips.Clear();
        AddThemeStyleboxOverride("panel", Ui.Glass(0.35f, 10, 12));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);

        var name = Ui.Text(Ui.T(SwitchStates.FunctionKey(assignment.Function)), 18);
        name.AutowrapMode = TextServer.AutowrapMode.Off;
        var source = Ui.Text(string.Format(Ui.T(SwitchSummary.SourceKey(assignment.Source)),
            SwitchSummary.SourceNumber(assignment.Source)), 14);
        source.AutowrapMode = TextServer.AutowrapMode.Off;
        source.AddThemeColorOverride("font_color", Ui.Muted);
        source.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        source.SizeFlagsVertical = name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var remove = Ui.FlatButton("×", clear);
        remove.CustomMinimumSize = new Vector2(38, 34);
        column.AddChild(Ui.Row(name, source, remove));

        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 8);
        chips.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(chips);
        for (int i = 0; i < assignment.Positions.Count; i++)
        {
            int index = i;
            var state = assignment.Positions[i].State;
            var text = state is int s ? Ui.T(SwitchStates.StateKey(assignment.Function, s)) : Ui.T("SWITCH_NO_EFFECT");
            var chip = Ui.ChipButton(text, () => chipClicked(index));
            chip.CustomMinimumSize = new Vector2(72, 0);
            chips.AddChild(chip);
            _chips.Add(chip);
        }
        _lit = null;
    }

    /// <summary>Lights the chip of <paramref name="position"/> (null: none, e.g. between positions).</summary>
    public void Light(int? position)
    {
        if (position == _lit) return;
        _lit = position;
        for (int i = 0; i < _chips.Count; i++) Ui.ChipLook(_chips[i], i == position);
    }
}
