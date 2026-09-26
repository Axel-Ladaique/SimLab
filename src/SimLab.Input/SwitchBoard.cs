namespace SimLab.Input;

/// <summary>An entry function (camera, OSD, wind, pause, reset) whose switch just entered a position with a state.</summary>
public readonly record struct SwitchEvent(SwitchFunction Function, int State);

/// <summary>
/// Reads the assigned switches every frame. A switch sits on its learned position nearest to the raw value (with
/// hysteresis, so a noisy 3-position switch never chatters). Held functions (gear, flaps, throttle cut) report the
/// state of that position; the others emit a <see cref="SwitchEvent"/> when the switch enters a position that has a
/// state. The first frame emits those entry events too, so a flight starts as the radio says, except reset.
/// </summary>
public sealed class SwitchBoard
{
    /// <summary>Raw-value margin by which a new position must be nearer than the current one to take over.</summary>
    public const double Hysteresis = 0.1;

    readonly SwitchAssignment[] _assignments;
    readonly int?[] _current;
    readonly Dictionary<SwitchFunction, int> _held = new();
    bool _primed;

    public SwitchBoard(IEnumerable<SwitchAssignment> assignments)
    {
        _assignments = assignments.Where(a => a.Positions.Count > 0).ToArray();
        _current = new int?[_assignments.Length];
    }

    public IReadOnlyList<SwitchEvent> Update(RawInputFrame frame)
    {
        var events = new List<SwitchEvent>();
        _held.Clear();
        for (int i = 0; i < _assignments.Length; i++)
        {
            var assignment = _assignments[i];
            if (assignment.Source.Read(frame) is not double value) continue;
            int position = Nearest(assignment.Positions, value, _current[i]);
            bool entered = _current[i] != position;
            _current[i] = position;
            if (assignment.Positions[position].State is not int state) continue;
            if (SwitchStates.IsHeld(assignment.Function)) _held[assignment.Function] = state;
            else if (entered && (_primed || assignment.Function != SwitchFunction.Reset))
                events.Add(new SwitchEvent(assignment.Function, state));
        }
        _primed = true;
        return events;
    }

    /// <summary>State of a held function this frame; null when unassigned or on a no-effect position.</summary>
    public int? Held(SwitchFunction function) => _held.TryGetValue(function, out var state) ? state : null;

    /// <summary>Index of the position the function's switch is on; null when unassigned or not read yet.</summary>
    public int? Position(SwitchFunction function)
    {
        for (int i = 0; i < _assignments.Length; i++)
            if (_assignments[i].Function == function) return _current[i];
        return null;
    }

    public static int Nearest(IReadOnlyList<SwitchPosition> positions, double value, int? current)
    {
        int best = 0;
        for (int i = 1; i < positions.Count; i++)
            if (Math.Abs(value - positions[i].Value) < Math.Abs(value - positions[best].Value)) best = i;
        if (current is int c && c < positions.Count && c != best
            && Math.Abs(value - positions[c].Value) - Math.Abs(value - positions[best].Value) <= Hysteresis)
            return c;
        return best;
    }
}
