using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>
/// Step 3: a <see cref="SwitchCard"/> per assigned function (click a chip to step the state of that position, × to
/// remove it; the position the switch rests in lights up live), then "+ Name" buttons for the unassigned functions,
/// each opening the <see cref="LearnDialog"/> with a <see cref="SwitchLearner"/> on the selected radio.
/// </summary>
public partial class SwitchesStep : VBoxContainer, IRadioStep
{
    RadioProfiles _profiles = null!;
    LearnDialog _dialog = null!;
    Label _chipHint = null!;
    VBoxContainer _cards = null!;
    Label _empty = null!, _addTitle = null!;
    HFlowContainer _add = null!;
    readonly Dictionary<SwitchFunction, SwitchCard> _shownCards = new();
    RadioProfile? _shown;
    bool _shownInit;
    RadioFrame _frame;

    SwitchLearner? _learner;
    SwitchFunction _learning;
    string? _learnGuid;
    /// <summary>Demo mode: positions shown (and saved) instead of the learner's.</summary>
    IReadOnlyList<double>? _demoPositions;

    public void Init(RadioProfiles profiles, LearnDialog dialog)
    {
        _profiles = profiles;
        _dialog = dialog;
        AddThemeConstantOverride("separation", 12);

        _empty = Ui.Text("", 17);
        _empty.AddThemeColorOverride("font_color", Ui.Muted);
        AddChild(_empty);
        _chipHint = Ui.Text(Ui.T("RADIO_CHIP_HINT"), 14);
        _chipHint.AddThemeColorOverride("font_color", Ui.Muted);
        _chipHint.Visible = false;
        AddChild(_chipHint);
        _cards = new VBoxContainer();
        _cards.AddThemeConstantOverride("separation", 10);
        AddChild(_cards);

        _addTitle = Ui.Text(Ui.T("RADIO_ADD_SWITCH"), 15);
        _addTitle.AddThemeColorOverride("font_color", Ui.Muted);
        AddChild(_addTitle);
        _add = new HFlowContainer();
        _add.AddThemeConstantOverride("h_separation", 8);
        _add.AddThemeConstantOverride("v_separation", 8);
        AddChild(_add);
    }

    public string? PrimaryText => null;

    public void Primary() { }

    public void Leave() => CloseDialog();

    public void Refresh(in RadioFrame frame)
    {
        _frame = frame;
        if (!_shownInit || !ReferenceEquals(frame.Profile, _shown))
        {
            _shownInit = true;
            _shown = frame.Profile;
            Rebuild(frame);
        }

        if (_dialog.Visible)
        {
            if (frame.Pad is not { } pad || pad.Guid != _learnGuid) CloseDialog();
            else if (_demoPositions is not null) _dialog.Display(_demoPositions);
            else if (_learner is not null)
            {
                _learner.Feed(pad.Frame, frame.Delta);
                _dialog.Display(_learner.Result()?.Positions);
            }
        }

        foreach (var (function, card) in _shownCards) card.Light(frame.Board?.Position(function));
    }

    void Rebuild(in RadioFrame frame)
    {
        foreach (var parent in new Container[] { _cards, _add })
            foreach (var child in parent.GetChildren())
            {
                parent.RemoveChild(child);
                child.QueueFree();
            }
        _shownCards.Clear();

        var profile = frame.Profile;
        _empty.Text = frame.Pad is null ? Ui.T("RADIO_EMPTY_TITLE") : profile is null ? Ui.T("RADIO_DEVICE_UNCALIBRATED") : "";
        _empty.Visible = _empty.Text != "";
        _addTitle.Visible = _add.Visible = profile is not null;
        if (profile is null)
        {
            _chipHint.Visible = false;
            return;
        }

        foreach (var function in SwitchStates.All)
        {
            if (profile.Switches.FirstOrDefault(s => s.Function == function) is not { } assignment)
            {
                var add = Ui.FlatButton("+  " + Ui.T(SwitchStates.FunctionKey(function)), () => OpenDialog(function));
                _add.AddChild(add);
                continue;
            }
            var card = new SwitchCard();
            card.Init(assignment, position => CycleState(function, position), () => Edit(p => p.ClearSwitch(function)));
            _cards.AddChild(card);
            _shownCards[function] = card;
        }
        _chipHint.Visible = _shownCards.Count > 0;
    }

    void CycleState(SwitchFunction function, int position) => Edit(p =>
    {
        var assignment = p.Switches.FirstOrDefault(s => s.Function == function);
        if (assignment is null || position >= assignment.Positions.Count) return;
        var current = assignment.Positions[position];
        assignment.Positions[position] = current with { State = SwitchStates.Next(function, current.State) };
    });

    void Edit(System.Action<RadioProfile> edit)
    {
        if (_frame.Pad is { } pad) _profiles.Edit(pad.Guid, edit);
    }

    void OpenDialog(SwitchFunction function)
    {
        if (_frame.Pad is not { } pad || _frame.Profile is null) return;
        _learner = new SwitchLearner();
        _learning = function;
        _learnGuid = pad.Guid;
        _dialog.Init(Ui.T(SwitchStates.FunctionKey(function)), SaveLearned, CloseDialog);
        _dialog.Visible = true;
    }

    /// <summary>Demo mode: opens the dialog for <paramref name="function"/> with these positions already found.</summary>
    public void DemoLearn(SwitchFunction function, IReadOnlyList<double> positions)
    {
        OpenDialog(function);
        _demoPositions = positions;
    }

    void SaveLearned()
    {
        if (_learnGuid is null) return;
        LearnedSwitch? learned = _demoPositions is { } demo
            ? new LearnedSwitch(new SwitchSource(AxisIndex: 6), demo)
            : _learner?.Result();
        if (learned is null || learned.Positions.Count < 2) return;
        var defaults = SwitchStates.Defaults(_learning, learned.Positions.Count);
        var assignment = new SwitchAssignment(_learning, learned.Source,
            learned.Positions.Select((value, i) => new SwitchPosition(value, defaults[i])).ToList());
        string guid = _learnGuid;
        CloseDialog();
        _profiles.Edit(guid, p => p.SetSwitch(assignment));
    }

    void CloseDialog()
    {
        _learner = null;
        _learnGuid = null;
        _demoPositions = null;
        _dialog.Visible = false;
    }
}
