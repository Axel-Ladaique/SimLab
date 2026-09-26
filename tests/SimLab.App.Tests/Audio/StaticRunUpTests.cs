using SimLab.App.Audio;

namespace SimLab.App.Tests.Audio;

public class StaticRunUpTests
{
    static StaticRunUp For(string id) => new(TestData.Aircraft(id).Power!, new SoundSpec(Blades: 2, PolePairs: 7));

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    [InlineData("jet")]
    [InlineData("p51")]
    [InlineData("f18")]
    public void Zero_throttle_is_silent(string id)
    {
        var runUp = For(id);
        Assert.Equal(0, runUp.At(0).Rpm);
        Assert.Equal(SynthParams.Silent, runUp.Synth(0));
    }

    [Fact]
    public void Rpm_and_loudness_rise_with_the_throttle()
    {
        var runUp = For("trainer");
        var low = runUp.Synth(0.3);
        var high = runUp.Synth(0.8);
        Assert.True(runUp.At(0.3).Rpm > 0);
        Assert.True(runUp.At(0.8).Rpm > runUp.At(0.3).Rpm);
        Assert.True(high.BladePassHz > low.BladePassHz);
        Assert.True(high.PropGain > low.PropGain);
        Assert.Equal(0, high.WindGain);
        Assert.Equal(0, high.RollGain);
    }

    [Fact]
    public void Full_throttle_is_the_static_full_power_point()
    {
        var runUp = For("sport");
        var p = runUp.Synth(1);
        Assert.Equal(runUp.Engine.StaticRpm * 2 / 60, p.BladePassHz, 6);
        Assert.Equal(1, p.PropGain, 6);
    }

    [Fact]
    public void Piston_and_turbine_run_ups_carry_their_exhaust_and_roar()
    {
        Assert.True(For("p51").Synth(0.5).ExhaustGain > 0);
        Assert.True(For("f18").Synth(0.5).RoarGain > 0);
    }
}
