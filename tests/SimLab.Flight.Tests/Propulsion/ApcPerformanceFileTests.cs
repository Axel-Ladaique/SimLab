using SimLab.Flight.Propulsion;

namespace SimLab.Flight.Tests.Propulsion;

public class ApcPerformanceFileTests
{
    const string Sample = """
         10x7E.dat

         PROP RPM =        4000

          V          J           Pe          Ct          Cp         PWR        Torque       Thrust
        (mph)     (Adv_Ratio)     -           -           -          (Hp)      (In-Lbf)      (Lbf)
         0.00      0.0000      0.0000      0.1000      0.0500      0.0100      0.1000      0.3000
         5.00      0.3000      0.4000      0.0800      0.0480      0.0100      0.1000      0.2000
        10.00      0.6000      0.6000      0.0200      0.0300      0.0100      0.1000      0.1000

         PROP RPM =        8000

          V          J           Pe          Ct          Cp         PWR        Torque       Thrust
        (mph)     (Adv_Ratio)     -           -           -          (Hp)      (In-Lbf)      (Lbf)
         0.00      0.0000      0.0000      0.1100      0.0520      0.0100      0.1000      1.3000
         5.00      0.1500      0.3000      0.1000      0.0510      0.0100      0.1000      1.2000
        10.00      0.3000      0.5000      0.0850      0.0490      0.0100      0.1000      1.0000
        15.00      0.4500      0.6500      -NaN-       0.0450      0.0100      0.1000      0.8000
        20.00      0.6000      0.7000      0.0300      0.0350      0.0100      0.1000      0.5000
        """;

    [Fact]
    public void Picks_the_block_nearest_the_target_rpm_and_skips_invalid_rows()
    {
        var p = ApcPerformanceFile.Parse(Sample, 0.254, 0.178, 7500);
        Assert.Equal(new[] { 0.0, 0.15, 0.3, 0.6 }, p.J);
        Assert.Equal(0.11, p.Ct[0], 9);
        Assert.Equal(0.035, p.Cp[^1], 9);
        Assert.Equal(0.254, p.DiameterM);
    }

    [Fact]
    public void File_without_rpm_blocks_is_rejected()
        => Assert.Throws<InvalidDataException>(() => ApcPerformanceFile.Parse("nothing here", 0.25, 0.18, 8000));
}
