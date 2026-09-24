using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Geometry;
using SimLab.Flight.Tests.Behavior;

namespace SimLab.Flight.Tests.Airframe;

public sealed class AircraftLoaderTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("simlab-loader-").FullName;

    const string Airfoil = """
        { "name": "flat", "tables": [ { "reynolds": 100000, "alphaDeg": [-10, 10], "cl": [-1.1, 1.1], "cd": [0.02, 0.02], "cm": [0, 0] } ] }
        """;

    const string Power = """
        {
          "position": [-0.3, 0, 0], "thrustAxis": [-2, 0, 0], "spinDirection": 1, "pFactor": 0.1,
          "motor": { "kv": 1000, "resistanceOhm": 0.05, "noLoadCurrentA": 1, "maxCurrentA": 40, "rotorInertia": 1e-4 },
          "battery": { "cells": 3, "capacityAh": 2.2, "internalResistanceOhm": 0.03 },
          "propeller": { "genericDiameterIn": 10, "genericPitchIn": 5 },
        }
        """;

    static string Aircraft(string controlSurface = "wing", string mixChannel = "aileron") => $$"""
        {
          // comments are allowed
          "name": "Loader test", "description": "tiny", "mass": 1.2, "cg": [0, 0, 0],
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
          "gear": [ { "name": "main", "position": [0, 0, -0.1], "stiffness": 800, "damping": 15, "steerMix": { "rudder": 1 }, "maxSteerDeg": 20 } ],
          "hull": [ { "name": "nose", "position": [-0.3, 0, 0], "tag": "nose" } ],
          "provenance": { "mass": "measured" },
        }
        """;

    string Write(string aircraftJson, bool sharedAirfoil = false, string power = Power)
    {
        var folder = Path.Combine(_root, "plane");
        Directory.CreateDirectory(folder);
        var airfoilDir = sharedAirfoil ? Path.Combine(_root, "airfoils") : Path.Combine(folder, "airfoils");
        Directory.CreateDirectory(airfoilDir);
        File.WriteAllText(Path.Combine(airfoilDir, "flat.json"), Airfoil);
        File.WriteAllText(Path.Combine(folder, "power.json"), power);
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
        Assert.Equal(-1.0, def.Power!.ThrustAxis.X, 12);
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

    static string Edit(string json, string find, string replace)
    {
        Assert.Contains(find, json);
        return json.Replace(find, replace);
    }

    void AssertRejected(string aircraftJson, string file = "aircraft.json", string power = Power)
    {
        var ex = Assert.Throws<InvalidDataException>(() => AircraftLoader.Load(Write(aircraftJson, power: power)));
        Assert.Contains(file, ex.Message);
    }

    [Fact]
    public void Overlapping_controls_are_rejected()
        => AssertRejected(Edit(Aircraft(), "\"controls\": [", """
            "controls": [ { "name": "flap", "surface": "wing", "side": "right", "spanStart": 0.4, "spanEnd": 0.8, "mix": { "flap": 1 } },
            """));

    [Fact]
    public void Zero_oswald_factor_is_rejected()
        => AssertRejected(Edit(Aircraft(), "\"segments\": 4,", "\"segments\": 4, \"oswald\": 0,"));

    [Fact]
    public void Surface_without_segments_is_rejected()
        => AssertRejected(Edit(Aircraft(), "\"segments\": 4,", "\"segments\": 0,"));

    [Fact]
    public void Zero_chord_is_rejected()
        => AssertRejected(Edit(Aircraft(), "\"tipChord\": 0.15", "\"tipChord\": 0"));

    [Fact]
    public void Singular_inertia_is_rejected()
        => AssertRejected(Edit(Aircraft(), "\"pitch\": 0.04 }", "\"pitch\": 0.04, \"rollYaw\": 0.06324555320336759 }"));

    [Fact]
    public void Non_positive_inertia_is_rejected()
        => AssertRejected(Edit(Aircraft(), "\"roll\": 0.05", "\"roll\": 0"));

    [Fact]
    public void Non_positive_servo_speed_is_rejected()
        => AssertRejected(Edit(Aircraft(), "\"maxNegativeDeg\": 15,", "\"maxNegativeDeg\": 15, \"servoSecondsPer60Deg\": 0,"));

    [Fact]
    public void Null_mix_is_rejected()
        => AssertRejected(Edit(Aircraft(), "\"mix\": { \"aileron\": -1 }", "\"mix\": null"));

    [Fact]
    public void Null_steer_mix_is_rejected()
        => AssertRejected(Edit(Aircraft(), "\"steerMix\": { \"rudder\": 1 }", "\"steerMix\": null"));

    [Fact]
    public void Battery_without_cells_is_rejected()
        => AssertRejected(Aircraft(), "power.json", Edit(Power, "\"cells\": 3", "\"cells\": 0"));

    [Fact]
    public void Battery_without_capacity_is_rejected()
        => AssertRejected(Aircraft(), "power.json", Edit(Power, "\"capacityAh\": 2.2", "\"capacityAh\": 0"));

    [Fact]
    public void Cg_datum_shifts_every_body_position()
    {
        var withBody = Edit(Aircraft(), "\"power\": \"power.json\",",
            "\"bodies\": [ { \"name\": \"pod\", \"position\": [-0.1, 0, 0], \"cdA\": [0.01, 0.02, 0.02] } ], \"power\": \"power.json\",");
        var plain = AircraftLoader.Load(Write(withBody));
        var shifted = AircraftLoader.Load(Write(Edit(withBody, "\"cg\": [0, 0, 0]", "\"cg\": [0.05, 0, 0]")));
        var d = new Vec3(-0.05, 0, 0);

        Assert.Equal(plain.Surfaces[0].Root + d, shifted.Surfaces[0].Root);
        Assert.Equal(plain.Bodies[0].Position + d, shifted.Bodies[0].Position);
        Assert.Equal(plain.Wheels[0].Position + d, shifted.Wheels[0].Position);
        Assert.Equal(plain.Hull[0].Position + d, shifted.Hull[0].Position);
        Assert.Equal(plain.Power!.Position + d, shifted.Power!.Position);
    }

    [Fact]
    public void Missing_cg_is_rejected()
        => AssertRejected(Edit(Aircraft(), "\"cg\": [0, 0, 0],", ""));

    [Fact]
    public void Positions_are_measured_from_the_datum_and_shifted_to_the_cg()
    {
        var json = Edit(Edit(Aircraft(), "\"cg\": [0, 0, 0]", "\"cg\": [0.5, 0, 0]"),
            "\"position\": [-0.3, 0, 0], \"tag\": \"nose\"", "\"position\": [0, 0, 0], \"tag\": \"nose\"");
        var def = AircraftLoader.Load(Write(json));
        Assert.Equal(new Vec3(-0.5, 0, 0), def.Hull.Single(h => h.Tag == "nose").Position);
    }

    [Fact]
    public void Shipped_aircraft_positions_are_relative_to_the_cg()
    {
        var def = Fleet.Load("trainer");
        AssertClose(new Vec3(-0.0175, 0, 0.12), def.Surfaces.Single(s => s.Name == "wing").Root);
        AssertClose(new Vec3(-0.40, 0, -0.21), def.Wheels.Single(w => w.Name == "nose").Position);
    }

    static void AssertClose(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, 12);
        Assert.Equal(expected.Y, actual.Y, 12);
        Assert.Equal(expected.Z, actual.Z, 12);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
