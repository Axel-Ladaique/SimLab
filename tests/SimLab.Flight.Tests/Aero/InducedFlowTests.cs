using SimLab.Flight.Aero;

namespace SimLab.Flight.Tests.Aero;

public class InducedFlowTests
{
    [Fact]
    public void Ground_effect_vanishes_far_from_the_ground_and_is_strong_near_it()
    {
        Assert.True(InducedFlow.GroundEffectFactor(20, 1.5) > 0.99);
        Assert.True(InducedFlow.GroundEffectFactor(0.05, 1.5) < 0.5);
        Assert.Equal(1.0, InducedFlow.GroundEffectFactor(1, 0));
    }
}
