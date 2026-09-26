using SimLab.App.Audio;
using SimLab.App.Settings;

namespace SimLab.App.Tests.Audio;

public class EngineSynthTests
{
    const int Rate = 44100;

    static SynthParams Prop(double bladeHz) =>
        SynthParams.Silent with { BladePassHz = bladeHz, ShaftHz = bladeHz / 2, ElectricalHz = bladeHz * 3.5, PropGain = 1 };

    static double Rms(ReadOnlySpan<float> x)
    {
        double sum = 0;
        foreach (var v in x) sum += (double)v * v;
        return Math.Sqrt(sum / x.Length);
    }

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
    public void Blade_pass_harmonics_above_nyquist_are_left_out_instead_of_aliasing()
    {
        // A 12-blade ducted fan at 47 500 rpm: 9.5 kHz blade pass. Its 4th and 5th harmonics (38 and 47.5 kHz) would fold
        // back to 6.1 and 3.4 kHz at 44.1 kHz.
        var synth = new EngineSynth(Rate);
        var buffer = new float[Rate];
        synth.Render(buffer.AsSpan(0, 512), Prop(9500));
        synth.Render(buffer, Prop(9500));
        double fundamental = Power(buffer, 9500);
        // What remains (about -38 dB) is the output soft clip's intermodulation; the folded 4th harmonic alone is -14 dB.
        Assert.True(Power(buffer, 6100) < 1e-3 * fundamental);
        Assert.True(Power(buffer, 3400) < 1e-3 * fundamental);
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

    [Fact]
    public void Wind_only_params_are_silent_when_the_wind_voice_is_muted()
    {
        var synth = new EngineSynth(Rate) { Mix = VoiceMix.Full with { Wind = 0 } };
        var buffer = new float[Rate / 2];
        // Warm up so _lastMix already equals the muted target before the assertion buffer.
        synth.Render(buffer, SynthParams.Silent with { WindGain = 1, WindCutoffHz = 1500 });
        synth.Render(buffer, SynthParams.Silent with { WindGain = 1, WindCutoffHz = 1500 });
        Assert.All(buffer, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Halving_the_propeller_mix_halves_the_rms_within_ten_percent()
    {
        var full = new EngineSynth(Rate);
        var half = new EngineSynth(Rate) { Mix = VoiceMix.Full with { Propeller = 0.5 } };
        var bufferFull = new float[Rate];
        var bufferHalf = new float[Rate];
        // Two renders each: the first settles the ramp from the initial silent/full mix state.
        full.Render(bufferFull, Prop(200));
        half.Render(bufferHalf, Prop(200));
        full.Render(bufferFull, Prop(200));
        half.Render(bufferHalf, Prop(200));
        double rmsFull = Rms(bufferFull);
        double rmsHalf = Rms(bufferHalf);
        Assert.InRange(rmsHalf, rmsFull * 0.45, rmsFull * 0.55);
    }

    [Fact]
    public void A_mid_stream_mix_change_keeps_the_max_sample_step_bounded()
    {
        var synth = new EngineSynth(Rate);
        var loud = Prop(300) with { WhineGain = 1, WindGain = 1, WindCutoffHz = 2000, RollGain = 1 };
        var all = new List<float>();
        var buffer = new float[735];
        for (int i = 0; i < 40; i++)
        {
            synth.Mix = i % 2 == 0 ? VoiceMix.Full : new VoiceMix(0.1, 0.1, 0.1, 0.1);
            synth.Render(buffer, loud);
            all.AddRange(buffer);
        }
        Assert.All(all, v => Assert.InRange(v, -1f, 1f));
        double maxStep = all.Zip(all.Skip(1), (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep < 0.35, $"max sample step {maxStep:F3}");
    }

    [Fact]
    public void Voice_mix_from_audio_settings_copies_and_clamps_the_four_voices()
    {
        var mix = VoiceMix.From(new AudioSettings(Propeller: 0.4, Motor: 2, Wind: -1, Rolling: 0.9));
        Assert.Equal(new VoiceMix(0.4, 1, 0, 0.9), mix);
        Assert.Equal(new VoiceMix(1, 1, 1, 1), VoiceMix.From(new AudioSettings()));
    }

    [Theory]
    [InlineData(0, 1, 0.25)]
    [InlineData(1, 1, 1.0)]
    [InlineData(1, 0, 0.0)]
    [InlineData(0.5, 0.5, 0.3125)]
    public void Impact_mix_scales_intensity_by_the_impacts_volume(double intensity, double impactsVolume, double expected) =>
        Assert.Equal(expected, ImpactMix.Linear(intensity, impactsVolume), 6);

    [Fact]
    public void Exhaust_voice_pops_at_the_firing_frequency()
    {
        var synth = new EngineSynth(Rate);
        var buffer = new float[Rate];
        var bark = SynthParams.Silent with { ShaftHz = 120, ExhaustGain = 1 };
        synth.Render(buffer.AsSpan(0, 512), bark);
        synth.Render(buffer, bark);
        double fundamental = Power(buffer, 120);
        Assert.True(fundamental > 10 * Power(buffer, 90));
        Assert.True(Power(buffer, 360) > 0.05 * fundamental, "rich in harmonics");
    }

    /// <summary>Coefficient of variation of the per-turn peaks of an exhaust-only render at 30 Hz firing.</summary>
    static double FiringScatter(double exhaustGain)
    {
        var synth = new EngineSynth(Rate);
        var bark = SynthParams.Silent with { ShaftHz = 30, ExhaustGain = exhaustGain };
        var buffer = new float[2 * Rate];
        synth.Render(buffer.AsSpan(0, 512), bark);
        synth.Render(buffer, bark);
        int period = Rate / 30;
        var peaks = new List<double>();
        for (int start = 0; start + period <= buffer.Length; start += period)
            peaks.Add(buffer.AsSpan(start, period).ToArray().Max(v => Math.Abs(v)) / exhaustGain);
        double mean = peaks.Average();
        return Math.Sqrt(peaks.Average(p => (p - mean) * (p - mean))) / mean;
    }

    [Fact]
    public void Exhaust_firings_scatter_at_idle_and_run_even_at_full_power()
    {
        double idle = FiringScatter(0.3), full = FiringScatter(1);
        Assert.True(full < 0.05, $"full-power scatter {full:F3}");
        Assert.True(idle > 3 * full + 0.1, $"idle scatter {idle:F3} vs full {full:F3}");
    }

    [Fact]
    public void Exhaust_voice_is_silent_while_the_engine_is_muted()
    {
        var synth = new EngineSynth(Rate) { EngineMuted = true };
        var buffer = new float[Rate / 4];
        synth.Render(buffer, SynthParams.Silent with { ShaftHz = 120, ExhaustGain = 1, RoarGain = 1 });
        Assert.All(buffer, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Roar_voice_is_broadband_noise()
    {
        var synth = new EngineSynth(Rate);
        var buffer = new float[Rate / 2];
        synth.Render(buffer, SynthParams.Silent with { RoarGain = 1 });
        Assert.True(Rms(buffer) > 0.05);
        Assert.True(Power(buffer, 300) > 0 && Power(buffer, 1500) > 0);
    }
}
