using SimLab.Flight.Aero;
using SimLab.Flight.Dynamics;
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
    IReadOnlyDictionary<string, string> Provenance);
