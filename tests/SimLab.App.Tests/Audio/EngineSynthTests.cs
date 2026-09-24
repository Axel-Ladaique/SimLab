using SimLab.App.Audio;

namespace SimLab.App.Tests.Audio;

public class EngineSynthTests
{
    const int Rate = 44100;

    static SynthParams Prop(double bladeHz) =>
        SynthParams.Silent with { BladePassHz = bladeHz, ShaftHz = bladeHz / 2, ElectricalHz = bladeHz * 3.5, PropGain = 1 };

    /// <summary>Goertzel power of one frequency in a signal.</summary>
    static double Power(ReadOnlySpan<float> x, double hz)
    {
        double w = 2 * Math.PI * hz / Rate, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        foreach (var v in x) { double s0 = v + c * s1 - s2; s2 = s1; s1 = s0; }
        return s1 * s1 + s2 * s2 - c * s1 * s2;
    }

    [Fact]
    public void Silent_parameters_render_silence()
    {
        var synth = new EngineSynth(Rate);
        var buffer = new float[1024];
        synth.Render(buffer, SynthParams.Silent);
        Assert.All(buffer, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Propeller_voice_peaks_at_the_blade_pass_frequency()
    {
        var synth = new EngineSynth(Rate);
        var buffer = new float[Rate];
        synth.Render(buffer.AsSpan(0, 512), Prop(200));
        synth.Render(buffer, Prop(200));
        double fundamental = Power(buffer, 200);
        Assert.True(fundamental > 10 * Power(buffer, 170));
        Assert.True(fundamental > 10 * Power(buffer, 230));
        Assert.True(fundamental > Power(buffer, 400));
    }

    [Fact]
    public void Output_stays_bounded_and_continuous_across_buffers_when_parameters_jump()
    {
        var synth = new EngineSynth(Rate);
        var all = new List<float>();
        var buffer = new float[735];
        var loud = Prop(300) with { WhineGain = 1, WindGain = 1, WindCutoffHz = 2000, RollGain = 1 };
        for (int i = 0; i < 40; i++)
        {
            synth.Render(buffer, i % 2 == 0 ? Prop(80) : loud);
            all.AddRange(buffer);
        }
        Assert.All(all, v => Assert.InRange(v, -1f, 1f));
        double maxStep = all.Zip(all.Skip(1), (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep < 0.35, $"max sample step {maxStep:F3}");
    }

    [Fact]
    public void Engine_mute_keeps_wind_but_drops_the_motor()
    {
        var synth = new EngineSynth(Rate) { EngineMuted = true };
        var buffer = new float[Rate / 2];
        synth.Render(buffer, Prop(200));
        Assert.All(buffer, v => Assert.Equal(0f, v));
        synth.Render(buffer, SynthParams.Silent with { WindGain = 1, WindCutoffHz = 1500 });
        Assert.Contains(buffer, v => Math.Abs(v) > 0.01f);
    }

    [Fact]
    public void Smoother_converges_without_overshoot_and_releases_slower_than_it_attacks()
    {
        var s = new ParameterSmoother(attackSeconds: 0.03, releaseSeconds: 0.12);
        var target = Prop(200);
        double previous = 0;
        for (int i = 0; i < 60; i++)
        {
            var p = s.Update(target, 1.0 / 60);
            Assert.True(p.PropGain >= previous && p.PropGain <= 1 + 1e-12);
            previous = p.PropGain;
        }
        Assert.Equal(1, s.Current.PropGain, 3);
        Assert.Equal(200, s.Current.BladePassHz, 1);
        double afterAttack = new ParameterSmoother(0.03, 0.12).Update(target, 0.03).PropGain;
        s.Update(SynthParams.Silent, 0.03);
        Assert.True(1 - s.Current.PropGain < afterAttack);
    }
}
