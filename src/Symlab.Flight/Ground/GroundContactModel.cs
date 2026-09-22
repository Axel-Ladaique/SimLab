using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;
using Symlab.Flight.Terrain;

namespace Symlab.Flight.Ground;

/// <summary>Penalty-based contact for wheels (spring-damper + tire friction) and hull points.</summary>
public sealed class GroundContactModel
{
    const double FrictionVelocity = 0.05;
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

    readonly record struct Probe(Vec3 Point, double Depth, Vec3 Normal, Vec3 Velocity)
    {
        public double NormalSpeed => Vec3.Dot(Velocity, Normal);
    }

    public BodyLoad Evaluate(in RigidBodyState s, ITerrain terrain, IReadOnlyList<double> steerRad)
    {
        var total = BodyLoad.Zero;
        for (int i = 0; i < _wheels.Length; i++)
            total += WheelLoad(_wheels[i], s, terrain, i < steerRad.Count ? steerRad[i] : 0);
        foreach (var h in _hull)
            total += HullLoad(h, s, terrain);
        return total;
    }

    public int WheelsInContact(in RigidBodyState s, ITerrain terrain)
    {
        int count = 0;
        foreach (var w in _wheels)
            if (ProbePoint(w.Position, s, terrain).Depth > 0) count++;
        return count;
    }

    public CrashCause DetectCrash(in RigidBodyState s, ITerrain terrain, CrashLimits limits)
    {
        foreach (var w in _wheels)
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
        return new Probe(p, terrain.Height(p.X, p.Z) - p.Y, terrain.Normal(p.X, p.Z), v);
    }

    static BodyLoad WheelLoad(WheelSpec w, in RigidBodyState s, ITerrain terrain, double steer)
    {
        var c = ProbePoint(w.Position, s, terrain);
        if (c.Depth <= 0) return BodyLoad.Zero;
        double vn = c.NormalSpeed;
        double normal = Math.Max(0, w.Stiffness * c.Depth - w.Damping * vn);

        var roll = s.Orientation.Rotate(new Vec3(Math.Cos(steer), 0, Math.Sin(steer)));
        roll = (roll - c.Normal * Vec3.Dot(roll, c.Normal)).Normalized();
        var lateral = Vec3.Cross(c.Normal, roll);
        var vt = c.Velocity - c.Normal * vn;

        var force = c.Normal * normal
            - roll * (w.RollingFriction * normal * Math.Tanh(Vec3.Dot(vt, roll) / FrictionVelocity))
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
