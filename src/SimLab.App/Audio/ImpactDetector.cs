using SimLab.Flight.Ground;

namespace SimLab.App.Audio;

public enum ImpactKind { Gear, Hull, Crash }

/// <param name="Intensity">0..1, from the approach speed (1 at <see cref="ImpactDetector.FullApproachSpeed"/>).</param>
public readonly record struct ImpactEvent(ImpactKind Kind, double Intensity);

/// <summary>Turns per-frame contact samples into impact sounds: an event when a point enters the ground fast enough,
/// at most one per point per refractory time, plus one full-intensity event when a crash starts.</summary>
public sealed class ImpactDetector
{
    public const double MinApproachSpeed = 0.3;
    public const double FullApproachSpeed = 4;
    public const double Refractory = 0.25;

    readonly Dictionary<string, bool> _inContact = new();
    readonly Dictionary<string, double> _lastEvent = new();
    CrashCause _crash = CrashCause.None;

    public IReadOnlyList<ImpactEvent> Update(IReadOnlyList<ContactSample> contacts, CrashCause crash, double time)
    {
        var events = new List<ImpactEvent>();
        foreach (var c in contacts)
        {
            bool touching = c.Depth > 0;
            bool was = _inContact.GetValueOrDefault(c.Name);
            _inContact[c.Name] = touching;
            if (!touching || was) continue;
            double approach = -c.NormalSpeed;
            if (approach < MinApproachSpeed) continue;
            if (_lastEvent.TryGetValue(c.Name, out var last) && time - last < Refractory) continue;
            _lastEvent[c.Name] = time;
            events.Add(new ImpactEvent(c.IsWheel ? ImpactKind.Gear : ImpactKind.Hull, Math.Min(approach / FullApproachSpeed, 1)));
        }
        if (crash != CrashCause.None && _crash == CrashCause.None) events.Add(new ImpactEvent(ImpactKind.Crash, 1));
        _crash = crash;
        return events;
    }

    public void Reset()
    {
        _inContact.Clear();
        _lastEvent.Clear();
        _crash = CrashCause.None;
    }
}
