using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>
/// Switches tab: one row per function with its switch, each learned position and the state it selects; the position
/// the switch is on lights up. Learn runs a <see cref="SwitchLearner"/> on the selected radio.
/// </summary>
public partial class SwitchesTab : VBoxContainer
{
    static readonly Color Active = new(0.22f, 0.54f, 0.87f);
    static readonly Color Idle = new(1, 1, 1, 0.35f);

    sealed class Row
    {
        public required Label Source;
        public required HBoxContainer Positions;
        public required Button Clear;
        public readonly List<Label> Markers = [];
    }

    Services _services = null!;
    System.Action _saved = null!;
    readonly Dictionary<SwitchFunction, Row> _rows = new();
    Label _prompt = null!;
    Button _next = null!;
    Button _cancel = null!;
    SwitchLearner? _learner;
    SwitchFunction _learning;
    string? _learnGuid;
    RadioProfile? _shown;
    bool _shownInit;
    RadioProfile? _profile;
    JoypadSnapshot? _pad;

    public void Init(Services services, System.Action saved)
    {
        _services = services;
        _saved = saved;
        Name = Ui.T("RADIO_TAB_SWITCHES");
        AddThemeConstantOverride("separation", 6);

        foreach (var function in SwitchStates.All)
        {
            var name = Ui.RowLabel(Ui.T(SwitchStates.FunctionKey(function)), 18);
            name.CustomMinimumSize = new Vector2(150, 0);
            var source = Ui.Text("", 16);
            source.CustomMinimumSize = new Vector2(110, 0);
            var positions = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            positions.AddThemeConstantOverride("separation", 10);
            var learn = Ui.FlatButton(Ui.T("RADIO_LEARN"), () => StartLearn(function));
            var clear = Ui.FlatButton(Ui.T("RADIO_CLEAR"), () => Clear(function));
            _rows[function] = new Row { Source = source, Positions = positions, Clear = clear };
            var row = Ui.Row(name, source, positions, learn, clear);
            row.CustomMinimumSize = new Vector2(0, 44);
            AddChild(row);
            AddChild(new HSeparator());
        }

        _prompt = Ui.Text(Ui.T("RADIO_SWITCHES_HELP"), 18);
        AddChild(_prompt);
        _next = Ui.Button(Ui.T("RADIO_NEXT"), FinishLearn);
        _cancel = Ui.Button(Ui.T("RADIO_CANCEL"), CancelLearn);
        AddChild(Ui.Row(_next, _cancel));
        SetLearning(false);
    }

    /// <summary>Called every frame: rebuilds rows when the profile changed, feeds the learner, lights the positions.</summary>
    public void Refresh(double dt, JoypadSnapshot? pad, RadioProfile? profile, SwitchBoard? board)
    {
        _pad = pad;
        _profile = profile;
        if (!_shownInit || !ReferenceEquals(profile, _shown))
        {
            _shownInit = true;
            _shown = profile;
            foreach (var function in SwitchStates.All) Rebuild(function);
        }

        if (_learner is not null)
        {
            if (pad is not { } p || p.Guid != _learnGuid)
            {
                CancelLearn();
                _prompt.Text = pad is null ? Ui.T("RADIO_CANCEL") + " — " + Ui.T("RADIO_NO_DEVICE") : Ui.T("RADIO_CANCEL");
            }
            else _learner.Feed(p.Frame, dt);
        }

        foreach (var (function, row) in _rows)
        {
            int? at = board?.Position(function);
            for (int i = 0; i < row.Markers.Count; i++) row.Markers[i].Modulate = at == i ? Active : Idle;
        }
    }

    void Rebuild(SwitchFunction function)
    {
        var row = _rows[function];
        foreach (var child in row.Positions.GetChildren()) child.QueueFree();
        row.Markers.Clear();
        var assignment = _profile?.Switches.FirstOrDefault(s => s.Function == function);
        row.Clear.Visible = assignment is not null;
        if (assignment is null)
        {
            row.Source.Text = Ui.T("RADIO_SWITCH_NONE");
            row.Source.Modulate = Idle;
            return;
        }
        row.Source.Text = string.Format(Ui.T(SwitchSummary.SourceKey(assignment.Source)), SwitchSummary.SourceNumber(assignment.Source));
        row.Source.Modulate = Colors.White;
        for (int i = 0; i < assignment.Positions.Count; i++)
        {
            int index = i;
            var marker = Ui.Text("●", 18);
            marker.Modulate = Idle;
            row.Markers.Add(marker);
            var picker = new OptionButton { FocusMode = FocusModeEnum.None };
            picker.AddItem(Ui.T("SWITCH_NO_EFFECT"), 0);
            for (int s = 0; s < SwitchStates.Count(function); s++) picker.AddItem(Ui.T(SwitchStates.StateKey(function, s)), s + 1);
            picker.Selected = (assignment.Positions[i].State ?? -1) + 1;
            picker.ItemSelected += item => SetState(function, index, item == 0 ? null : (int)item - 1);
            row.Positions.AddChild(Ui.Row(marker, picker));
        }
    }

    void StartLearn(SwitchFunction function)
    {
        if (_pad is not { } pad)
        {
            _prompt.Text = Ui.T("RADIO_NO_DEVICE");
            return;
        }
        if (_profile is null)
        {
            _prompt.Text = Ui.T("RADIO_DEVICE_UNCALIBRATED");
            return;
        }
        _learner = new SwitchLearner();
        _learning = function;
        _learnGuid = pad.Guid;
        _prompt.Text = string.Format(Ui.T("RADIO_LEARN_PROMPT"), Ui.T(SwitchStates.FunctionKey(function)));
        SetLearning(true);
    }

    void FinishLearn()
    {
        if (_learner is null || _learnGuid is null) return;
        if (_learner.Result() is not { } learned)
        {
            _learner = new SwitchLearner();
            _prompt.Text = Ui.T("RADIO_LEARN_NONE");
            return;
        }
        var defaults = SwitchStates.Defaults(_learning, learned.Positions.Count);
        var assignment = new SwitchAssignment(_learning, learned.Source,
            learned.Positions.Select((value, i) => new SwitchPosition(value, defaults[i])).ToList());
        Save(_learnGuid, p => p.SetSwitch(assignment));
        _learner = null;
        _prompt.Text = Ui.T("RADIO_LEARN_DONE");
        SetLearning(false);
    }

    void CancelLearn()
    {
        _learner = null;
        _prompt.Text = Ui.T("RADIO_SWITCHES_HELP");
        SetLearning(false);
    }

    void Clear(SwitchFunction function)
    {
        if (_pad is { } pad) Save(pad.Guid, p => p.ClearSwitch(function));
    }

    void SetState(SwitchFunction function, int position, int? state)
    {
        if (_pad is not { } pad) return;
        Save(pad.Guid, p =>
        {
            var assignment = p.Switches.FirstOrDefault(s => s.Function == function);
            if (assignment is null || position >= assignment.Positions.Count) return;
            assignment.Positions[position] = assignment.Positions[position] with { State = state };
        });
    }

    /// <summary>Loads the device's profile fresh, edits it, saves it and lets the screen reload it everywhere.</summary>
    void Save(string guid, System.Action<RadioProfile> edit)
    {
        var profile = _services.Radios.Load(guid, out _);
        if (profile is null) return;
        edit(profile);
        _services.Radios.Save(profile);
        _services.Router.InvalidateProfiles();
        _saved();
    }

    void SetLearning(bool learning)
    {
        _next.Visible = learning;
        _cancel.Visible = learning;
    }
}
