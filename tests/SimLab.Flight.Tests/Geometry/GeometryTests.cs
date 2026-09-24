using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Geometry;

internal static class Approx
{
    public static void Equal(Vec3 expected, Vec3 actual, int precision = 9)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }
}

public class Vec3Tests
{
    [Fact]
    public void Cross_of_x_and_y_is_z() => Assert.Equal(Vec3.UnitZ, Vec3.Cross(Vec3.UnitX, Vec3.UnitY));

    [Fact]
    public void Length_of_3_4_0_is_5() => Assert.Equal(5.0, new Vec3(3, 4, 0).Length, 12);

    [Fact]
    public void Normalizing_zero_returns_zero() => Assert.Equal(Vec3.Zero, Vec3.Zero.Normalized());
}

public class QuatTests
{
    [Fact]
    public void Positive_rotation_about_z_pitches_nose_up()
        => Approx.Equal(Vec3.UnitY, Quat.FromAxisAngle(Vec3.UnitZ, Math.PI / 2).Rotate(Vec3.UnitX));

    [Fact]
    public void Positive_rotation_about_x_lowers_the_right_wing()
        => Approx.Equal(new Vec3(0, -1, 0), Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2).Rotate(Vec3.UnitZ));

    [Fact]
    public void Positive_rotation_about_y_yaws_nose_left()
        => Approx.Equal(new Vec3(0, 0, -1), Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2).Rotate(Vec3.UnitX));

    [Fact]
    public void InverseRotate_undoes_Rotate()
    {
        var q = Quat.FromAxisAngle(new Vec3(1, 2, 3), 0.7);
        var v = new Vec3(0.3, -1.2, 2.5);
        Approx.Equal(v, q.InverseRotate(q.Rotate(v)));
    }

    [Fact]
    public void Product_composes_rotations_right_to_left()
    {
        var a = Quat.FromAxisAngle(Vec3.UnitY, 0.4);
        var b = Quat.FromAxisAngle(Vec3.UnitX, -1.1);
        var v = new Vec3(1, 2, 3);
        Approx.Equal(a.Rotate(b.Rotate(v)), (a * b).Rotate(v));
    }
}

public class Mat3Tests
{
    [Fact]
    public void Inverse_undoes_multiplication()
    {
        var m = new Mat3(0.2, 0, -0.01, 0, 0.35, 0, -0.01, 0, 0.25);
        var v = new Vec3(1, -2, 0.5);
        Approx.Equal(v, m.Inverse() * (m * v));
    }

    [Fact]
    public void Singular_matrix_throws() => Assert.Throws<InvalidOperationException>(() => Mat3.Diagonal(1, 0, 1).Inverse());
}

public class AttitudeTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(30, 10, 45)]
    [InlineData(-60, -20, 270)]
    [InlineData(10, 5, 359)]
    public void Euler_round_trip(double rollDeg, double pitchDeg, double headingDeg)
    {
        var q = Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg));
        var a = Attitude.FromOrientation(q);
        Assert.Equal(rollDeg, Angle.Deg(a.Roll), 6);
        Assert.Equal(pitchDeg, Angle.Deg(a.Pitch), 6);
        Assert.Equal(headingDeg, Angle.Deg(a.Heading), 6);
    }

    [Fact]
    public void Heading_zero_points_north()
        => Approx.Equal(new Vec3(0, 0, -1), Attitude.ToOrientation(0, 0, 0).Rotate(Vec3.UnitX));

    [Fact]
    public void Heading_ninety_points_east()
        => Approx.Equal(Vec3.UnitX, Attitude.ToOrientation(0, 0, Math.PI / 2).Rotate(Vec3.UnitX));
}
