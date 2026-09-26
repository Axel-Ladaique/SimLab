using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Ground;

/// <summary>Depth (m, positive = below ground) and normal speed (m/s, negative = moving into the ground) of a
/// single wheel or hull point contact. Wheels carry Tag "wheel".</summary>
public readonly record struct ContactSample(string Name, bool IsWheel, string Tag, double Depth, double NormalSpeed);

/// <summary>Penalty-based contact for wheels (spring-damper + tire friction) and hull points.</summary>
public sealed class GroundContactModel
{
    const double FrictionVelocity = 0.05;
    // Brakes hold: a much sharper friction onset so an idling engine cannot creep the aircraft forward.
    const double BrakeVelocity = 0.005;
    const double HullFriction = 0.6;
    const double HullStiffnessPerKg = 3000;
    const double HullDampingPerKg = 80;

    readonly WheelSpec[] _wheels;
    readonly HullPointSpec[] _hull;
    readonly double _hullStiffness;
    readonly double _hullDamping;

    public GroundContactModel(IEnumerable<WheelSpec> wheels, IEnumerable<HullPointSpec> hull, double mass)
    {
        _wheels = wheels.ToArray();
        _hull = hull.ToArray();
        _hullStiffness = HullStiffnessPerKg * mass;
        _hullDamping = HullDampingPerKg * mass;
    }

    public IReadOnlyList<WheelSpec> Wheels => _wheels;

    /// <summary>False while retractable gear is up or travelling: the wheels then neither touch nor carry anything.</summary>
    public bool WheelsExtended { get; set; } = true;

    /// <summary>Brake command, 0–1, applied to every wheel that has a brake.</summary>
    public double BrakeCommand { get; set; }

    ReadOnlySpan<WheelSpec> ActiveWheels => WheelsExtended ? _wheels : [];

    readonly record struct Probe(Vec3 Point, double Depth, Vec3 Normal, Vec3 Velocity)
    {
        public double NormalSpeed => Vec3.Dot(Velocity, Normal);
    }

    public BodyLoad Evaluate(in RigidBodyState s, ITerrain terrain, IReadOnlyList<double> steerRad)
    {
        var total = BodyLoad.Zero;
        var wheels = ActiveWheels;
        for (int i = 0; i < wheels.Length; i++)
            total += WheelLoad(wheels[i], s, terrain, i < steerRad.Count ? steerRad[i] : 0, BrakeCommand);
        foreach (var h in _hull)
            total += HullLoad(h, s, terrain);
        return total;
    }

    public int WheelsInContact(in RigidBodyState s, ITerrain terrain)
    {
        int count = 0;
        foreach (var w in ActiveWheels)
            if (ProbePoint(w.Position, s, terrain).Depth > 0) count++;
        return count;
    }

    /// <summary>Depth (m, positive = below ground) and normal speed (m/s, negative = moving into the ground) of every
    /// wheel, then every hull point. Read-only; used by the sound layer.</summary>
    public IReadOnlyList<ContactSample> Contacts(in RigidBodyState s, ITerrain terrain)
    {
        var list = new List<ContactSample>(_wheels.Length + _hull.Length);
        foreach (var w in ActiveWheels)
        {
            var p = ProbePoint(w.Position, s, terrain);
            list.Add(new ContactSample(w.Name, true, "wheel", p.Depth, p.NormalSpeed));
        }
        foreach (var h in _hull)
        {
            var p = ProbePoint(h.Position, s, terrain);
            list.Add(new ContactSample(h.Name, false, h.Tag, p.Depth, p.NormalSpeed));
        }
        return list;
    }

    public CrashCause DetectCrash(in RigidBodyState s, ITerrain terrain, CrashLimits limits)
    {
        foreach (var w in ActiveWheels)
        {
            var p = ProbePoint(w.Position, s, terrain);
            if (p.Depth > 0 && -p.NormalSpeed > limits.MaxGearSinkRate) return CrashCause.HardLanding;
        }
        foreach (var h in _hull)
        {
            var p = ProbePoint(h.Position, s, terrain);
            if (terrain.HitsObstacle(p.Point)) return CrashCause.TreeStrike;
            if (p.Depth <= 0) continue;
            bool belly = h.Tag == "belly";
            double limit = belly ? limits.MaxBellyImpactSpeed : limits.MaxHullImpactSpeed;
            if (-p.NormalSpeed > limit) return CauseFor(h.Tag);
        }
        return CrashCause.None;
    }

    static CrashCause CauseFor(string tag) => tag switch
    {
        "wingtip" => CrashCause.WingtipStrike,
        "nose" => CrashCause.NoseOver,
        "belly" => CrashCause.HardLanding,
        _ => CrashCause.HullImpact,
    };

    static Probe ProbePoint(Vec3 bodyPoint, in RigidBodyState s, ITerrain terrain)
    {
        var p = s.Position + s.Orientation.Rotate(bodyPoint);
        var v = s.Velocity + s.Orientation.Rotate(Vec3.Cross(s.AngularVelocity, bodyPoint));
        return new Probe(p, terrain.Height(p.X, p.Y) - p.Z, terrain.Normal(p.X, p.Y), v);
    }

    static BodyLoad WheelLoad(WheelSpec w, in RigidBodyState s, ITerrain terrain, double steer, double brake)
    {
        var c = ProbePoint(w.Position, s, terrain);
        if (c.Depth <= 0) return BodyLoad.Zero;
        double vn = c.NormalSpeed;
        double normal = Math.Max(0, w.Stiffness * c.Depth - w.Damping * vn);

        var roll = s.Orientation.Rotate(BodyAxes.Forward * Math.Cos(steer) + BodyAxes.Right * Math.Sin(steer));
        roll = (roll - c.Normal * Vec3.Dot(roll, c.Normal)).Normalized();
        var lateral = Vec3.Cross(c.Normal, roll);
        var vt = c.Velocity - c.Normal * vn;

        double along = Vec3.Dot(vt, roll);
        var force = c.Normal * normal
            - roll * (w.RollingFriction * normal * Math.Tanh(along / FrictionVelocity) + w.BrakeFriction * brake * normal * Math.Tanh(along / BrakeVelocity))
            - lateral * (w.LateralFriction * normal * Math.Tanh(Vec3.Dot(vt, lateral) / FrictionVelocity));
        return ToBody(s, w.Position, force);
    }

    BodyLoad HullLoad(HullPointSpec h, in RigidBodyState s, ITerrain terrain)
    {
        var c = ProbePoint(h.Position, s, terrain);
        if (c.Depth <= 0) return BodyLoad.Zero;
        double vn = c.NormalSpeed;
        double normal = Math.Max(0, _hullStiffness * c.Depth - _hullDamping * vn);
        var vt = c.Velocity - c.Normal * vn;
        double slip = vt.Length;
        var force = c.Normal * normal - vt.Normalized() * (HullFriction * normal * Math.Tanh(slip / FrictionVelocity));
        return ToBody(s, h.Position, force);
    }

    static BodyLoad ToBody(in RigidBodyState s, Vec3 point, Vec3 forceWorld)
    {
        var f = s.Orientation.InverseRotate(forceWorld);
        return new BodyLoad(f, Vec3.Cross(point, f));
    }
}
