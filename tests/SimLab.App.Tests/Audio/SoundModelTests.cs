using SimLab.App.Audio;
using SimLab.Flight.Airframe;

namespace SimLab.App.Tests.Audio;

public class SoundModelTests
{
    static readonly EngineSoundModel Model = new(new SoundSpec(Blades: 2, PolePairs: 7), staticRpm: 9000, staticThrust: 20, maxCurrent: 60);

    [Fact]
    public void Frequencies_follow_rpm_blades_and_pole_pairs()
    {
        var v = Model.Evaluate(rpm: 6000, thrust: 10, motorCurrent: 20);
        Assert.Equal(200, v.BladePassHz, 9);
        Assert.Equal(100, v.ShaftHz, 9);
        Assert.Equal(700, v.ElectricalHz, 9);
    }

    [Fact]
    public void Silent_when_the_motor_is_stopped()
    {
        var v = Model.Evaluate(rpm: 0, thrust: 0, motorCurrent: 0);
        Assert.Equal(0, v.PropGain);
        Assert.Equal(0, v.WhineGain);
    }

    [Fact]
    public void Gains_rise_with_thrust_and_current_and_stay_within_one()
    {
        var low = Model.Evaluate(3000, 2, 5);
        var high = Model.Evaluate(9000, 20, 60);
        Assert.True(high.PropGain > low.PropGain);
        Assert.True(high.WhineGain > low.WhineGain);
        Assert.InRange(high.PropGain, 0, 1);
        Assert.InRange(Model.Evaluate(12000, 40, 120).WhineGain, 0, 1);
    }

    [Fact]
    public void For_uses_the_static_full_throttle_point_of_the_power_plant()
    {
        var power = TestData.Aircraft("sport").Power!;
        var model = EngineSoundModel.For(power, SoundSpec.Default);
        Assert.True(model.StaticRpm > 5000 && model.StaticRpm < 20000, $"{model.StaticRpm}");
        Assert.True(model.StaticThrust > 5, $"{model.StaticThrust}");
    }

    [Fact]
    public void Wind_is_silent_when_slow_and_rises_with_airspeed()
    {
        Assert.Equal(0, WindSoundModel.Evaluate(2).Gain);
        var slow = WindSoundModel.Evaluate(10);
        var fast = WindSoundModel.Evaluate(25);
        Assert.True(fast.Gain > slow.Gain && fast.CutoffHz > slow.CutoffHz);
        Assert.InRange(WindSoundModel.Evaluate(60).Gain, 0, 1);
    }

    [Fact]
    public void Rolling_needs_contact_and_speed()
    {
        Assert.Equal(0, RollingSoundModel.Gain(0, 10));
        Assert.Equal(0, RollingSoundModel.Gain(3, 0));
        Assert.True(RollingSoundModel.Gain(3, 10) > RollingSoundModel.Gain(3, 2));
        Assert.InRange(RollingSoundModel.Gain(3, 50), 0, 1);
    }

    static readonly EngineSoundModel Piston = new(new SoundSpec(Blades: 2, PolePairs: 1), staticRpm: 7000, staticThrust: 200, maxCurrent: 0,
        source: SimLab.Flight.Propulsion.PowerSource.Piston);
    static readonly EngineSoundModel Turbine = new(new SoundSpec(Blades: 1, PolePairs: 1), staticRpm: 117000, staticThrust: 220, maxCurrent: 0,
        source: SimLab.Flight.Propulsion.PowerSource.Turbine);

    [Fact]
    public void Piston_engine_barks_at_its_firing_rate_already_at_idle_and_louder_with_power()
    {
        var idle = Piston.Evaluate(rpm: 1800, thrust: 10, motorCurrent: 0);
        var full = Piston.Evaluate(rpm: 7000, thrust: 200, motorCurrent: 0);
        Assert.Equal(30, idle.ShaftHz, 9);   // a single-cylinder two-stroke fires once per revolution
        Assert.InRange(idle.ExhaustGain, 0.2, 0.5);
        Assert.Equal(1, full.ExhaustGain, 9);
        Assert.Equal(0, full.WhineGain);
        Assert.Equal(0, full.RoarGain);
        Assert.Equal(0, Piston.Evaluate(0, 0, 0).ExhaustGain);
        Assert.True(full.PropGain < 0.5 * full.ExhaustGain, "the exhaust drowns the propeller");
    }

    [Fact]
    public void Turbine_whines_with_its_spool_and_roars_with_its_thrust()
    {
        var idle = Turbine.Evaluate(rpm: 33000, thrust: 9, motorCurrent: 0);
        var full = Turbine.Evaluate(rpm: 117000, thrust: 220, motorCurrent: 0);
        Assert.Equal(550, idle.BladePassHz, 9);
        Assert.True(idle.PropGain > 0 && idle.RoarGain > 0);
        Assert.True(full.RoarGain > 3 * idle.RoarGain);
        Assert.Equal(1, full.RoarGain, 9);
        Assert.Equal(0, full.WhineGain);
        Assert.Equal(0, full.ExhaustGain);
    }
}
