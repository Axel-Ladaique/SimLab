using System.Text.Json;
using System.Text.Json.Serialization;
using Symlab.Flight.Aero;
using Symlab.Flight.Controls;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Ground;
using Symlab.Flight.Numerics;
using Symlab.Flight.Propulsion;

namespace Symlab.Flight.Airframe;

public static class AircraftLoader
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new Vec3JsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static AircraftDefinition Load(string folder)
    {
        var path = Path.Combine(folder, "aircraft.json");
        var dto = Read<AircraftDto>(path);
        if (dto.Mass <= 0) throw Invalid(path, "mass must be positive.");
        if (dto.Surfaces.Count == 0) throw Invalid(path, "at least one surface is required.");
        var mass = Mass(path, dto);

        foreach (var s in dto.Surfaces)
        {
            if (s.Segments < 1) throw Invalid(path, $"surface '{s.Name}' needs at least one segment.");
            if (s.Span <= 0 || s.RootChord <= 0 || s.TipChord <= 0)
                throw Invalid(path, $"surface '{s.Name}' span and chords must be positive.");
            if (s.Oswald <= 0) throw Invalid(path, $"surface '{s.Name}' oswald must be positive.");
        }

        var surfaces = dto.Surfaces.Select(s => new SurfaceSpec(
            s.Name, s.Role, s.Root, s.Span, s.RootChord, s.TipChord, s.SweepDeg, s.DihedralDeg,
            s.IncidenceDeg, s.TwistDeg, s.Airfoil, s.Segments, s.Mirror, s.Oswald)).ToList();

        var airfoils = surfaces.Select(s => s.Airfoil).Distinct()
            .ToDictionary(name => name, name => LoadAirfoil(folder, name));

        var surfaceNames = surfaces.Select(s => s.Name).ToHashSet();
        foreach (var c in dto.Controls)
        {
            if (!surfaceNames.Contains(c.Surface)) throw Invalid(path, $"control '{c.Name}' references unknown surface '{c.Surface}'.");
            if (c.Mix is null) throw Invalid(path, $"control '{c.Name}' needs a mix (use {{}} for none).");
            if (c.ServoSecondsPer60Deg <= 0) throw Invalid(path, $"control '{c.Name}' servoSecondsPer60Deg must be positive.");
            RequireChannels(path, c.Name, c.Mix.Keys);
        }
        foreach (var w in dto.Gear)
        {
            if (w.SteerMix is null) throw Invalid(path, $"wheel '{w.Name}' needs a steerMix (use {{}} for none).");
            RequireChannels(path, w.Name, w.SteerMix.Keys);
        }

        var controls = dto.Controls.Select(c => new ControlSurfaceSpec(
            c.Name, c.Surface, c.Side, c.ChordFraction, c.SpanStart, c.SpanEnd,
            c.MaxPositiveDeg, c.MaxNegativeDeg, c.ServoSecondsPer60Deg, c.Mix)).ToList();
        var bodies = dto.Bodies.Select(b => new BodySpec(b.Name, b.Position, b.CdA)).ToList();

        // Build the parts an Aircraft builds, so geometry and control-layout errors surface here with the file name.
        try
        {
            _ = new SurfaceAeroModel(surfaces, airfoils, controls, bodies);
            foreach (var c in controls) _ = new Servo(c.ServoSecondsPer60Deg);
        }
        catch (ArgumentException ex)
        {
            throw Invalid(path, ex.Message);
        }

        return new AircraftDefinition(
            dto.Name,
            dto.Description,
            folder,
            mass,
            surfaces,
            airfoils,
            controls,
            bodies,
            dto.Power is null ? null : LoadPower(Path.Combine(folder, dto.Power)),
            dto.Gear.Select(w => new WheelSpec(w.Name, w.Position, w.Stiffness, w.Damping, w.RollingFriction,
                w.LateralFriction, w.MaxSteerDeg, w.SteerMix)).ToList(),
            dto.Hull.Select(h => new HullPointSpec(h.Name, h.Position, h.Tag)).ToList(),
            new CrashLimits(dto.Crash.MaxGearSinkRate, dto.Crash.MaxHullImpactSpeed, dto.Crash.MaxBellyImpactSpeed),
            dto.Provenance);
    }

    static MassProperties Mass(string path, AircraftDto dto)
    {
        var i = dto.Inertia;
        // Positive definite: principal terms positive and the roll-yaw block non-singular.
        if (i.Roll <= 0 || i.Yaw <= 0 || i.Pitch <= 0 || i.Roll * i.Yaw - i.RollYaw * i.RollYaw <= 1e-12 * i.Roll * i.Yaw)
            throw Invalid(path, "inertia must be positive definite (roll, yaw, pitch > 0 and rollYaw² < roll·yaw).");
        try
        {
            return MassProperties.FromPrincipal(dto.Mass, i.Roll, i.Yaw, i.Pitch, i.RollYaw);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw Invalid(path, ex.Message);
        }
    }

    public static Airfoil LoadAirfoil(string aircraftFolder, string name)
    {
        var candidates = new[]
        {
            Path.Combine(aircraftFolder, "airfoils", name + ".json"),
            Path.Combine(aircraftFolder, "..", "airfoils", name + ".json"),
        };
        var path = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Airfoil '{name}' not found (looked in {string.Join(", ", candidates)}).");
        var dto = Read<AirfoilDto>(path);
        try
        {
            return new Airfoil(dto.Name, dto.Tables.Select(t => new AirfoilTable(t.Reynolds, t.AlphaDeg, t.Cl, t.Cd, t.Cm)));
        }
        catch (ArgumentException ex)
        {
            throw Invalid(path, ex.Message);
        }
    }

    public static PowerPlantSpec LoadPower(string path)
    {
        var dto = Read<PowerDto>(path);
        if (dto.Motor is null || dto.Battery is null || dto.Propeller is null)
            throw Invalid(path, "motor, battery and propeller are required.");
        if (dto.Motor.Kv <= 0 || dto.Motor.RotorInertia <= 0) throw Invalid(path, "motor kv and rotorInertia must be positive.");
        if (dto.Battery.Cells < 1) throw Invalid(path, "battery cells must be at least 1.");
        if (dto.Battery.CapacityAh <= 0) throw Invalid(path, "battery capacityAh must be positive.");

        var esc = dto.Esc is null ? EscSpec.Linear() : new EscSpec(dto.Esc.ThrottleIn, dto.Esc.ThrottleOut, dto.Esc.Brake);
        Interpolation.RequireIncreasing(esc.ThrottleIn, "esc.throttleIn");

        var spec = new PowerPlantSpec(
            new MotorSpec(dto.Motor.Kv, dto.Motor.ResistanceOhm, dto.Motor.NoLoadCurrentA, dto.Motor.MaxCurrentA, dto.Motor.RotorInertia),
            BatterySpec.Lipo(dto.Battery.Cells, dto.Battery.CapacityAh, dto.Battery.InternalResistanceOhm),
            esc,
            Propeller(path, dto.Propeller),
            dto.Position,
            dto.ThrustAxis.Normalized(),
            dto.SpinDirection >= 0 ? 1 : -1,
            dto.PFactor);

        if (dto.ThrustStand is null) return spec;
        var csv = Path.Combine(Path.GetDirectoryName(path)!, dto.ThrustStand);
        return ThrustStand.Calibrate(spec, ThrustStand.ParseCsv(File.ReadAllText(csv)));
    }

    static PropellerSpec Propeller(string path, PropellerDto p)
    {
        if (p.GenericDiameterIn is double d && p.GenericPitchIn is double pitch) return PropellerSpec.Generic(d, pitch);
        if (p.DiameterM <= 0) throw Invalid(path, "propeller needs genericDiameterIn/genericPitchIn or diameterM.");
        if (p.ApcFile is not null)
        {
            var apc = Path.Combine(Path.GetDirectoryName(path)!, p.ApcFile);
            return ApcPerformanceFile.Parse(File.ReadAllText(apc), p.DiameterM, p.PitchM, p.ApcRpm);
        }
        if (p.J is null || p.Ct is null || p.Cp is null || p.J.Length != p.Ct.Length || p.J.Length != p.Cp.Length || p.J.Length < 2)
            throw Invalid(path, "propeller tables j, ct and cp must have the same length (at least 2).");
        Interpolation.RequireIncreasing(p.J, "propeller.j");
        return new PropellerSpec(p.DiameterM, p.PitchM, p.J, p.Ct, p.Cp);
    }

    static void RequireChannels(string path, string owner, IEnumerable<string> channels)
    {
        foreach (var ch in channels)
            if (!ControlInputs.IsChannel(ch)) throw Invalid(path, $"'{owner}' mixes unknown channel '{ch}'.");
    }

    static T Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}", path);
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw Invalid(path, "document is empty.");
        }
        catch (JsonException ex)
        {
            throw Invalid(path, ex.Message);
        }
    }

    static InvalidDataException Invalid(string path, string message) => new($"{path}: {message}");
}
