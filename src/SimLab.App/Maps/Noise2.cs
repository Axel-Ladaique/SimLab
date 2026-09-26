namespace SimLab.App.Maps;

/// <summary>
/// Seeded 2-D value noise: a random value in [−1, 1] at each integer lattice point (an integer hash of the point and
/// the seed, so it never repeats), blended with a quintic fade. Deterministic and stateless after construction, hence
/// safe to share between threads.
/// </summary>
public sealed class Noise2
{
    readonly uint _seed;

    public Noise2(int seed) => _seed = Mix((uint)seed * 0x9E3779B9u + 0x7F4A7C15u);

    /// <summary>Smooth noise in [−1, 1] with features about one unit wide.</summary>
    public double Value(double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        int i = (int)fx, j = (int)fy;
        double u = Fade(x - fx), v = Fade(y - fy);
        double a = Lattice(i, j), b = Lattice(i + 1, j), c = Lattice(i, j + 1), d = Lattice(i + 1, j + 1);
        return a + u * (b - a) + v * (c - a) + u * v * (a - b - c + d);
    }

    /// <summary>Σ 0.5ᵏ · Value(2ᵏ x, 2ᵏ y) over the octaves, divided by Σ 0.5ᵏ so it stays in [−1, 1].</summary>
    public double Fbm(double x, double y, int octaves)
    {
        double sum = 0, norm = 0, amp = 1, f = 1;
        for (int k = 0; k < octaves; k++, amp *= 0.5, f *= 2)
        {
            sum += amp * Value(f * x, f * y);
            norm += amp;
        }
        return sum / norm;
    }

    /// <summary>Σ 0.5ᵏ · (1 − |Value(2ᵏ x, 2ᵏ y)|)², normalised to [0, 1]: sharp crests where the noise crosses zero.</summary>
    public double Ridged(double x, double y, int octaves)
    {
        double sum = 0, norm = 0, amp = 1, f = 1;
        for (int k = 0; k < octaves; k++, amp *= 0.5, f *= 2)
        {
            double r = 1 - Math.Abs(Value(f * x, f * y));
            sum += amp * r * r;
            norm += amp;
        }
        return sum / norm;
    }

    double Lattice(int i, int j)
    {
        uint h = Mix(_seed ^ Mix((uint)i * 0x85EBCA6Bu ^ Mix((uint)j * 0xC2B2AE35u)));
        return h * (2.0 / uint.MaxValue) - 1;
    }

    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    /// <summary>The murmur3 finaliser: a cheap integer hash with good avalanche.</summary>
    static uint Mix(uint h)
    {
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }
}
