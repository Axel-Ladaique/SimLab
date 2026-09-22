using Symlab.Flight.Aero;
using Symlab.Flight.Airframe;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Tests.Aero;

namespace Symlab.Flight.Tests.Airframe;

internal static class TestDefinitions
{
    /// <summary>1 kg unpowered glider, statically stable, used for simulation plumbing tests.</summary>
    public static AircraftDefinition Glider() => new(
        Name: "Test glider",
        Description: "",
        Folder: "",
        Mass: MassProperties.FromPrincipal(1.0, roll: 0.05, yaw: 0.08, pitch: 0.04),
        Surfaces:
        [
            new SurfaceSpec("wing", SurfaceRole.Wing, new Vec3(0.02, 0.05, 0), 0.75, 0.2, 0.2, 0, 4, 3, 0, "linear", 6, true),
            new SurfaceSpec("stab", SurfaceRole.HorizontalTail, new Vec3(-0.6, 0, 0), 0.2, 0.12, 0.12, 0, 0, 0, 0, "linear", 2, true),
            new SurfaceSpec("fin", SurfaceRole.VerticalTail, new Vec3(-0.6, 0, 0), 0.15, 0.12, 0.12, 0, 90, 0, 0, "linear", 2, false),
        ],
        Airfoils: TestAirfoils.Map(),
        Controls:
        [
            new ControlSurfaceSpec("elevator", "stab", Side.Both, 0.4, 0, 1, 20, 20, 0.1, new Dictionary<string, double> { ["elevator"] = -1 }),
        ],
        Bodies: [],
        Power: null,
        Wheels: [],
        Hull:
        [
            new HullPointSpec("nose", new Vec3(0.3, 0, 0), "nose"),
            new HullPointSpec("belly", new Vec3(0, -0.05, 0), "belly"),
            new HullPointSpec("tail", new Vec3(-0.65, 0, 0), "tail"),
        ],
        Crash: new CrashLimits(3, 1.5, 3),
        Provenance: new Dictionary<string, string>());
}
