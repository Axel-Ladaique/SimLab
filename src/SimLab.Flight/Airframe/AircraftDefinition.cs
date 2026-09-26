using SimLab.Flight.Aero;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Flight.Propulsion;

namespace SimLab.Flight.Airframe;

/// <summary>
/// Everything needed to build an <see cref="Aircraft"/>, in body axes (x back, y right, z up) with the origin at the CG.
/// aircraft.json and power.json measure positions from a free datum (the shipped aircraft use the nose) in those axes;
/// the required <c>"cg": [x, y, z]</c> gives the CG from the same datum, and the loader subtracts it from every
/// position (surface roots, body positions, wheel positions, hull points and the power position).
/// </summary>
/// <param name="Folder">Folder the definition was loaded from (holds model.glb for the game layer).</param>
/// <param name="Provenance">Parameter path → source (estimated, measured, xfoil, vspaero, cfd).</param>
/// <param name="FpvCamera">Onboard FPV camera from aircraft.json, or null for the default mount: see <see cref="FpvCameraSpec.For"/>.</param>
/// <param name="GearRetract">Retractable landing gear, or null for fixed gear.</param>
public sealed record AircraftDefinition(
    string Name,
    string Description,
    string Folder,
    MassProperties Mass,
    IReadOnlyList<SurfaceSpec> Surfaces,
    IReadOnlyDictionary<string, Airfoil> Airfoils,
    IReadOnlyList<ControlSurfaceSpec> Controls,
    IReadOnlyList<BodySpec> Bodies,
    PowerPlantSpec? Power,
    IReadOnlyList<WheelSpec> Wheels,
    IReadOnlyList<HullPointSpec> Hull,
    CrashLimits Crash,
    IReadOnlyDictionary<string, string> Provenance,
    FpvCameraSpec? FpvCamera = null,
    GearRetractSpec? GearRetract = null);

/// <summary>Retractable landing gear: every wheel travels together.</summary>
/// <param name="Seconds">Time for a full retraction or extension.</param>
/// <param name="CdA">Drag area of the extended gear, m², body-axis order [frontal, side, top] like a body's.</param>
/// <param name="DragPosition">Where that drag acts: the wheels' centroid, body axes from the CG.</param>
public sealed record GearRetractSpec(double Seconds, Vec3 CdA, Vec3 DragPosition);
