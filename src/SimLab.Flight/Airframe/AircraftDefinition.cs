using SimLab.Flight.Aero;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Ground;
using SimLab.Flight.Propulsion;

namespace SimLab.Flight.Airframe;

/// <summary>
/// Everything needed to build an <see cref="Aircraft"/>. Body origin is the CG: aircraft.json may give an optional
/// <c>"cg": [x, y, z]</c> datum (default [0, 0, 0]) in the file's own frame, and the loader subtracts it from every
/// body-frame position (surface roots, body positions, wheel positions, hull points and the power position).
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
