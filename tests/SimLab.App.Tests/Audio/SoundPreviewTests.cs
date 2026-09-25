using SimLab.App.Audio;

namespace SimLab.App.Tests.Audio;

public class SoundPreviewTests
{
    static readonly EngineSoundModel Engine = new(new SoundSpec(Blades: 2, PolePairs: 7), staticRpm: 9000, staticThrust: 20, maxCurrent: 60);
    static readonly SoundSpec Spec = new(Blades: 2, PolePairs: 7);

    [Fact]
    public void Engine_is_silent_at_idle()
    {
        var (p, impact) = SoundPreview.At(0.5, Engine, Spec);
        Assert.Equal(0, p.PropGain);
        Assert.Equal(0, p.WhineGain);
        Assert.False(impact);
    }

    [Fact]
    public void Blade_pass_matches_the_static_rpm_at_full_power_hold()
    {
        var (p, _) = SoundPreview.At(4.5, Engine, Spec);
        Assert.Equal(Engine.StaticRpm * Spec.Blades / 60, p.BladePassHz, 6);
    }

    [Fact]
    public void Wind_pass_is_engine_silent_with_gain()
    {
        var (p, _) = SoundPreview.At(8.25, Engine, Spec);
        Assert.True(p.WindGain > 0);
        Assert.Equal(0, p.PropGain);
    }

    [Fact]
    public void Rolling_pass_has_roll_gain()
    {
        var (p, _) = SoundPreview.At(10.5, Engine, Spec);
        Assert.True(p.RollGain > 0);
    }

    [Fact]
    public void Exactly_one_impact_episode_per_loop()
    {
        const double dt = 1.0 / 60;
        int episodes = 0;
        bool wasImpact = false;
        for (double t = 0; t < SoundPreview.LoopSeconds - 1e-9; t += dt)
        {
            bool impact = SoundPreview.At(t, Engine, Spec).Impact;
            if (impact && !wasImpact) episodes++;
            wasImpact = impact;
        }
        Assert.Equal(1, episodes);
    }

    [Fact]
    public void Periodic_across_loop_boundaries()
    {
        foreach (var t in new[] { 0.0, 0.5, 3.2, 4.5, 6.9, 8.25, 10.5, 11.5, 11.9 })
        {
            var (pa, impactA) = SoundPreview.At(t, Engine, Spec);
            var (pb, impactB) = SoundPreview.At(t + SoundPreview.LoopSeconds, Engine, Spec);
            Assert.Equal(impactA, impactB);
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
}
