using Symlab.Flight.Aero;
using Symlab.Flight.Airframe;

namespace Symlab.Flight.Tests.Airframe;

public sealed class AircraftLoaderTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("symlab-loader-").FullName;

    const string Airfoil = """
        { "name": "flat", "tables": [ { "reynolds": 100000, "alphaDeg": [-10, 10], "cl": [-1.1, 1.1], "cd": [0.02, 0.02], "cm": [0, 0] } ] }
        """;

    const string Power = """
        {
          "position": [0.3, 0, 0], "thrustAxis": [2, 0, 0], "spinDirection": 1, "pFactor": 0.1,
          "motor": { "kv": 1000, "resistanceOhm": 0.05, "noLoadCurrentA": 1, "maxCurrentA": 40, "rotorInertia": 1e-4 },
          "battery": { "cells": 3, "capacityAh": 2.2, "internalResistanceOhm": 0.03 },
          "propeller": { "genericDiameterIn": 10, "genericPitchIn": 5 },
        }
        """;

    static string Aircraft(string controlSurface = "wing", string mixChannel = "aileron") => $$"""
        {
          // comments are allowed
          "name": "Loader test", "description": "tiny", "mass": 1.2,
          "inertia": { "roll": 0.05, "yaw": 0.08, "pitch": 0.04 },
          "surfaces": [
            { "name": "wing", "role": "wing", "root": [0, 0, 0], "span": 0.6, "rootChord": 0.2, "tipChord": 0.15,
              "dihedralDeg": 3, "airfoil": "flat", "segments": 4, "mirror": true }
          ],
          "controls": [
            { "name": "aileronRight", "surface": "{{controlSurface}}", "side": "right", "chordFraction": 0.25,
              "spanStart": 0.5, "spanEnd": 1, "maxPositiveDeg": 15, "maxNegativeDeg": 15, "mix": { "{{mixChannel}}": -1 } }
          ],
          "power": "power.json",
          "gear": [ { "name": "main", "position": [0, -0.1, 0], "stiffness": 800, "damping": 15, "steerMix": { "rudder": 1 }, "maxSteerDeg": 20 } ],
          "hull": [ { "name": "nose", "position": [0.3, 0, 0], "tag": "nose" } ],
          "provenance": { "mass": "measured" },
        }
        """;

    string Write(string aircraftJson, bool sharedAirfoil = false)
    {
        var folder = Path.Combine(_root, "plane");
        Directory.CreateDirectory(folder);
        var airfoilDir = sharedAirfoil ? Path.Combine(_root, "airfoils") : Path.Combine(folder, "airfoils");
        Directory.CreateDirectory(airfoilDir);
        File.WriteAllText(Path.Combine(airfoilDir, "flat.json"), Airfoil);
        File.WriteAllText(Path.Combine(folder, "power.json"), Power);
        File.WriteAllText(Path.Combine(folder, "aircraft.json"), aircraftJson);
        return folder;
    }

    [Fact]
    public void Loads_a_complete_definition()
    {
        var def = AircraftLoader.Load(Write(Aircraft()));
        Assert.Equal("Loader test", def.Name);
        Assert.Equal(1.2, def.Mass.Mass);
        Assert.Equal(SurfaceRole.Wing, def.Surfaces[0].Role);
        Assert.Equal(Side.Right, def.Controls[0].Side);
        Assert.Equal(-1, def.Controls[0].Mix["aileron"]);
        Assert.Equal(4, def.Surfaces[0].Segments);
        Assert.NotNull(def.Power);
        Assert.Equal(1.0, def.Power!.ThrustAxis.X, 12);
        Assert.Equal("measured", def.Provenance["mass"]);
        Assert.Equal(20, def.Wheels[0].MaxSteerDeg);
        Assert.Equal(3, def.Crash.MaxGearSinkRate);
        Assert.True(def.Airfoils.ContainsKey("flat"));
    }

    [Fact]
    public void Finds_airfoils_in_the_shared_folder()
        => Assert.True(AircraftLoader.Load(Write(Aircraft(), sharedAirfoil: true)).Airfoils.ContainsKey("flat"));

    [Fact]
    public void Control_on_unknown_surface_is_rejected()
        => Assert.Throws<InvalidDataException>(() => AircraftLoader.Load(Write(Aircraft(controlSurface: "canard"))));

    [Fact]
    public void Unknown_mix_channel_is_rejected()
        => Assert.Throws<InvalidDataException>(() => AircraftLoader.Load(Write(Aircraft(mixChannel: "gear"))));

    [Fact]
    public void Malformed_json_reports_the_file()
    {
        var ex = Assert.Throws<InvalidDataException>(() => AircraftLoader.Load(Write("{ not json")));
        Assert.Contains("aircraft.json", ex.Message);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
