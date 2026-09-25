using System;
using System.Collections.Generic;
using System.Linq;
using SimLab.App.Audio;

namespace SimLab.App.Tests.Audio;

public class SoundPreviewTests
{
    static readonly EngineSoundModel Engine = new(new SoundSpec(Blades: 2, PolePairs: 7), staticRpm: 9000, staticThrust: 20, maxCurrent: 60);
    static readonly SoundSpec Spec = new(Blades: 2, PolePairs: 7);

    [Fact]
    public void Engine_is_silent_at_idle()
    {
        var p = SoundPreview.At(0.5, Engine, Spec);
        Assert.Equal(0, p.PropGain);
        Assert.Equal(0, p.WhineGain);
    }

    [Fact]
    public void Blade_pass_matches_the_static_rpm_at_full_power_hold()
    {
        var p = SoundPreview.At(4.5, Engine, Spec);
        Assert.Equal(Engine.StaticRpm * Spec.Blades / 60, p.BladePassHz, 6);
    }

    [Fact]
    public void Wind_pass_is_engine_silent_with_gain()
    {
        var p = SoundPreview.At(8.25, Engine, Spec);
        Assert.True(p.WindGain > 0);
        Assert.Equal(0, p.PropGain);
    }

    [Fact]
    public void Rolling_pass_has_roll_gain()
    {
        var p = SoundPreview.At(10.5, Engine, Spec);
        Assert.True(p.RollGain > 0);
    }

    [Fact]
    public void Output_is_finite_at_the_minimum_static_rpm()
    {
        // EngineSoundModel clamps StaticRpm >= 1, so SoundPreview's StaticRpm <= 0 guard is unreachable through the
        // public API; the closest degenerate model is the clamp's floor, which must still give finite parameters.
        var engine = new EngineSoundModel(Spec, staticRpm: 1, staticThrust: 1, maxCurrent: 1);
        foreach (var t in new[] { 0.0, 1.5, 4.5, 6.0, 8.25, 10.5, 11.9 })
        {
            var p = SoundPreview.At(t, engine, Spec);
            Assert.False(double.IsNaN(p.BladePassHz) || double.IsNaN(p.PropGain) || double.IsNaN(p.WindGain) || double.IsNaN(p.RollGain));
        }
    }

    [Fact]
    public void Periodic_across_loop_boundaries()
    {
        foreach (var t in new[] { 0.0, 0.5, 3.2, 4.5, 6.9, 8.25, 10.5, 11.5, 11.9 })
        {
            var pa = SoundPreview.At(t, Engine, Spec);
            var pb = SoundPreview.At(t + SoundPreview.LoopSeconds, Engine, Spec);
            Assert.Equal(pa.BladePassHz, pb.BladePassHz, 6);
            Assert.Equal(pa.ShaftHz, pb.ShaftHz, 6);
            Assert.Equal(pa.ElectricalHz, pb.ElectricalHz, 6);
            Assert.Equal(pa.PropGain, pb.PropGain, 6);
            Assert.Equal(pa.WhineGain, pb.WhineGain, 6);
            Assert.Equal(pa.WindGain, pb.WindGain, 6);
            Assert.Equal(pa.WindCutoffHz, pb.WindCutoffHz, 6);
            Assert.Equal(pa.RollGain, pb.RollGain, 6);
        }
    }

    [Theory]
    [InlineData(1.0 / 144)]
    [InlineData(1.0 / 60)]
    [InlineData(1.0 / 30)]
    [InlineData(1.0 / 24)]
    [InlineData(0.1)]
    public void Impact_between_fires_exactly_once_per_loop_at_fixed_step_sizes(double dt)
    {
        const int loops = 6;
        Assert.Equal(loops, CountImpacts(FixedSteps(dt, loops * SoundPreview.LoopSeconds)));
    }

    [Fact]
    public void Impact_between_fires_exactly_once_per_loop_with_a_jittered_step()
    {
        const int loops = 6;
        var rng = new Random(20260925);
        Assert.Equal(loops, CountImpacts(JitteredSteps(rng, 0.005, 0.12, loops * SoundPreview.LoopSeconds)));
    }

    [Fact]
    public void Impact_between_crosses_a_wrap_from_one_loop_into_the_next()
    {
        // 11.9 s is just after the first loop's impact (11.5 s); 23.6 s is well into the second loop, past its
        // impact at 11.5 + 12 = 23.5 s. The elapsed span (11.7 s) is under one loop, so this must cross exactly
        // the one impact instant at 23.5 s, even though t0 and t1 sit in different loop iterations.
        Assert.True(SoundPreview.ImpactBetween(11.9, 23.6));
        Assert.False(SoundPreview.ImpactBetween(11.9, 23.4));
    }

    [Fact]
    public void Impact_between_reports_a_span_covering_a_full_loop_as_one_crossing()
    {
        Assert.True(SoundPreview.ImpactBetween(0, SoundPreview.LoopSeconds));
        Assert.True(SoundPreview.ImpactBetween(0, 3 * SoundPreview.LoopSeconds));
    }

    [Fact]
    public void Impact_between_is_false_for_a_zero_or_negative_span()
    {
        Assert.False(SoundPreview.ImpactBetween(5, 5));
        Assert.False(SoundPreview.ImpactBetween(5, 3));
    }

    [Fact]
    public void Impact_between_endpoints_are_left_open_right_closed()
    {
        // The impact instant itself (11.5 s) must count as the closing edge of a step that lands on it...
        Assert.True(SoundPreview.ImpactBetween(11.4, 11.5));
        // ...but not as the opening edge of the next step, or a step landing on it would double-count.
        Assert.False(SoundPreview.ImpactBetween(11.5, 11.6));
    }

    static IEnumerable<(double T0, double T1)> FixedSteps(double dt, double end)
    {
        double t = 0;
        while (t < end - 1e-12)
        {
            double next = Math.Min(t + dt, end);
            yield return (t, next);
            t = next;
        }
    }

    static IEnumerable<(double T0, double T1)> JitteredSteps(Random rng, double minDt, double maxDt, double end)
    {
        double t = 0;
        while (t < end - 1e-12)
        {
            double next = Math.Min(t + minDt + rng.NextDouble() * (maxDt - minDt), end);
            yield return (t, next);
            t = next;
        }
    }

    static int CountImpacts(IEnumerable<(double T0, double T1)> steps) =>
        steps.Count(s => SoundPreview.ImpactBetween(s.T0, s.T1));
}
