using System.Globalization;
using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Propulsion;

namespace SimLab.App.Ui;

public enum TakeoffKind { Tricycle, TailDragger, HandLaunch }

public enum SheetChannel { Ailerons, Elevons, Elevator, Rudder, Flaps, Throttle }

/// <summary>Power plant facts: Kv and pack for electric motors, peak power for piston engines, thrust for turbines;
/// propeller size when there is one, tank size for fuel engines.</summary>
public sealed record PowerSummary(double Kv, int Cells, double CapacityMah, double PropDiameterIn, double PropPitchIn,
    PowerSource Source = PowerSource.Electric, double MaxPowerW = 0, double MaxThrustN = 0, double TankMl = 0);

public readonly record struct SheetLine(string Key, string Value);

/// <summary>Facts about an aircraft for the main menu, all read or computed exactly from its definition.</summary>
/// <param name="SpanM">Largest span of its wing surfaces.</param>
/// <param name="LengthM">Fore-aft extent of its hull points.</param>
/// <param name="WingAreaM2">Total area of its wing surfaces (tails excluded).</param>
public sealed record AircraftSheet(
    double SpanM,
    double LengthM,
    double MassKg,
    double WingAreaM2,
    PowerSummary? Power,
    TakeoffKind Takeoff,
    IReadOnlyList<SheetChannel> Channels)
{
    const double MetersPerInch = 0.0254;
    /// <summary>A wheel closer than this to the centreline is a nose or tail wheel.</summary>
    const double CentrelineM = 0.01;

    public double WingLoadingGPerDm2 => WingAreaM2 > 0 ? MassKg * 1000 / (WingAreaM2 * 100) : 0;

    public static AircraftSheet From(AircraftDefinition definition)
    {
        var wings = definition.Surfaces.Where(s => s.Role == SurfaceRole.Wing).ToList();
        var hullX = definition.Hull.Select(h => h.Position.X).DefaultIfEmpty(0).ToList();
        var power = definition.Power is { } p
            ? new PowerSummary(p.Motor?.Kv ?? 0, p.Battery?.Cells ?? 0, (p.Battery?.CapacityAh ?? 0) * 1000,
                (p.Propeller?.DiameterM ?? 0) / MetersPerInch, (p.Propeller?.PitchM ?? 0) / MetersPerInch,
                p.Source, p.Piston?.MaxPowerW ?? 0, p.Turbine?.MaxThrustN ?? 0, p.TankMl ?? 0)
            : null;
        return new AircraftSheet(
            wings.Select(s => s.TotalSpan).DefaultIfEmpty(0).Max(),
            hullX.Max() - hullX.Min(),
            definition.Mass.Mass,
            wings.Sum(s => s.TotalArea),
            power,
            TakeoffOf(definition),
            ChannelsOf(definition));
    }

    /// <summary>Tricycle when the foremost wheel (body x points back) is on the centreline, tail-dragger otherwise;
    /// hand launch without wheels.</summary>
    static TakeoffKind TakeoffOf(AircraftDefinition definition)
    {
        if (definition.Wheels.Count == 0) return TakeoffKind.HandLaunch;
        var foremost = definition.Wheels.MinBy(w => w.Position.X)!;
        return Math.Abs(foremost.Position.Y) < CentrelineM ? TakeoffKind.Tricycle : TakeoffKind.TailDragger;
    }

    static IReadOnlyList<SheetChannel> ChannelsOf(AircraftDefinition definition)
    {
        var found = new HashSet<SheetChannel>();
        foreach (var control in definition.Controls)
        {
            bool aileron = control.Mix.ContainsKey("aileron"), elevator = control.Mix.ContainsKey("elevator");
            if (aileron && elevator) found.Add(SheetChannel.Elevons);
            else if (aileron) found.Add(SheetChannel.Ailerons);
            else if (elevator) found.Add(SheetChannel.Elevator);
            if (control.Mix.ContainsKey("rudder")) found.Add(SheetChannel.Rudder);
            if (control.Mix.ContainsKey("flap")) found.Add(SheetChannel.Flaps);
        }
        if (definition.Power is not null) found.Add(SheetChannel.Throttle);
        return Enum.GetValues<SheetChannel>().Where(found.Contains).ToList();
    }

    public IReadOnlyList<SheetLine> Lines(Func<string, string> translate)
    {
        var inv = CultureInfo.InvariantCulture;
        string power = Power switch
        {
            null => translate("SHEET_GLIDER"),
            { Source: PowerSource.Turbine } t =>
                $"{translate("SHEET_TURBINE")} {t.MaxThrustN.ToString("0", inv)} N · {(t.TankMl / 1000).ToString("0.0", inv)} L",
            { Source: PowerSource.Piston } g =>
                $"{translate("SHEET_PISTON")} {(g.MaxPowerW / 1000).ToString("0.0", inv)} kW · " +
                $"{g.PropDiameterIn.ToString("0.#", inv)}×{g.PropPitchIn.ToString("0.#", inv)} in · {g.TankMl.ToString("0", inv)} ml",
            var e => $"{e.Kv.ToString("0", inv)} kV · {e.Cells}S {e.CapacityMah.ToString("0", inv)} mAh · " +
                     $"{e.PropDiameterIn.ToString("0.#", inv)}×{e.PropPitchIn.ToString("0.#", inv)} in",
        };
        return
        [
            new("SHEET_SPAN", SpanM.ToString("0.00", inv) + " m"),
            new("SHEET_LENGTH", LengthM.ToString("0.00", inv) + " m"),
            new("SHEET_MASS", MassKg.ToString("0.00", inv) + " kg"),
            new("SHEET_WING_AREA", (WingAreaM2 * 100).ToString("0.0", inv) + " dm²"),
            new("SHEET_WING_LOADING", WingLoadingGPerDm2.ToString("0", inv) + " g/dm²"),
            new("SHEET_POWER", power),
            new("SHEET_TAKEOFF", translate(TakeoffKey(Takeoff))),
            new("SHEET_CHANNELS", string.Join(", ", Channels.Select(c => translate(ChannelKey(c))))),
        ];
    }

    public static string TakeoffKey(TakeoffKind kind) => kind switch
    {
        TakeoffKind.TailDragger => "TAKEOFF_TAILDRAGGER",
        TakeoffKind.HandLaunch => "TAKEOFF_HAND",
        _ => "TAKEOFF_TRICYCLE",
    };

    public static string ChannelKey(SheetChannel channel) => "CHANNEL_" + channel.ToString().ToUpperInvariant();
}
