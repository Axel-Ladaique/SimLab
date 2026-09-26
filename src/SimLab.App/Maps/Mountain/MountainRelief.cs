using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Mountain;

/// <summary>
/// The mountain site's ground (world ENU, z = 0 at the strip). A crest curving west toward both ends of the map
/// (<see cref="CrestX"/>) separates a flat shoulder, where the strip lies, from a 350 m west face: rounded at the top,
/// steepest 30–300 m below the crest, crossed by up to three cliff bands, flattening into a valley floor that falls
/// gently south with the stream. East of the shoulder the ground climbs through meadows and forest to +600 m, and a
/// summit in the north-east corner reaches about +950 m.
/// </summary>
public static class MountainRelief
{
    public const double FaceDrop = 350;
    const double FaceLength = 930;
    const double ProfileStep = 0.5;
    const double EastStart = 260, EastRun = 1700, EastRise = 600, EastRoughness = 60;
    const double SummitX = 1600, SummitY = 1600, SummitHeight = 950, SummitSigma = 350;
    const double LakeX = -1150, LakeY = 500, LakeSemiX = 225, LakeSemiY = 125;

    /// <summary>Covers the shore band (r ≤ 1.15) even along the long axis, 0.15 × 225 = 33.75 m out.</summary>
    const double LakeBlend = 35;
    const double StreamDepth = 2;

    /// <summary>The road bridge's deck over the stream (m); under it the channel is dug again below the road. The road
    /// crosses the stream at about 30°, so the deck is long enough to span most of the oblique channel.</summary>
    public const double BridgeLength = 24, BridgeWidth = 5;

    /// <summary>Beyond this distance from the crossing the road's banks no longer fill the stream channel.</summary>
    const double BridgeReach = 60;

    /// <summary>Cut and fill banks rise at most this steeply (45°) from the road bed's edge…</summary>
    const double BankSlope = 1.0;

    /// <summary>…for this far, then steepen so they always meet the natural ground within <see cref="BankReach"/>.</summary>
    const double BankRun = 40, BankReach = 70;

    const double StripBlend = 20, PilotRadius = 25, PilotBlend = 15;

    /// <summary>A grid cell's diagonal: flattening that much beyond an area keeps every triangle touching it flat.</summary>
    const double CellMargin = 6;

    static readonly Noise2 Noise = new(MountainMap.Seed);

    /// <summary>Fraction of the face's smooth drop reached at each <see cref="ProfileStep"/> below the crest.</summary>
    static readonly double[] FaceProfile = BuildFaceProfile();

    /// <summary>Cliff bands: distance below the crest, height and width (m) at full strength.</summary>
    static readonly (double Below, double Height, double Width)[] Bands = [(110, 22, 14), (215, 18, 12), (470, 20, 14)];

    /// <summary>The lake, a 450 × 250 m ellipse drawn as a 48-gon.</summary>
    public static readonly WaterBody Lake = new(
        Enumerable.Range(0, 48).Select(k =>
        {
            double t = 2 * Math.PI * k / 48;
            return (LakeX + LakeSemiX * Math.Cos(t), LakeY + LakeSemiY * Math.Sin(t));
        }).ToArray(),
        MountainMap.LakeLevel);

    /// <summary>The stream from the lake's south shore along the valley floor, leaving the map in the south-west.</summary>
    public static readonly IReadOnlyList<(double X, double Y)> Stream =
    [
        (-1150, 375), (-1165, 150), (-1175, -200), (-1200, -600), (-1215, -1000), (-1290, -1250),
        (-1420, -1480), (-1540, -1660), (-1700, -1850), (-1860, -2000),
    ];

    /// <summary>The crest line: x of the crest at this y, concave to the west.</summary>
    public static double CrestX(double y) => -30 - y * y / 4000;

    /// <summary>The analytic relief before the stamps (strip, pilot area, road, lake, stream).</summary>
    public static double Height(double x, double y)
    {
        double h = 3 * Noise.Fbm(x / 180, y / 180, 3);
        double below = CrestX(y) - x;
        if (below > 0) h += WestFace(below, x, y);
        if (x > EastStart) h += East(x, y);
        double kx = x + 150, ky = y - 950;
        h += 18 * Math.Exp(-(kx * kx + ky * ky) / (2 * 70 * 70)); // the knob the chairlift climbs to
        return h;
    }

    /// <summary>The map's height grid: the relief sampled every <see cref="MountainMap.GridStep"/> m, then the
    /// stream carved, the lake dug, the road cut and filled (the stream channel dug again under the bridge), and the
    /// strip and pilot area levelled.</summary>
    public static HeightGrid Build(MountainRoad road)
    {
        const double half = MountainMap.HalfSize, step = MountainMap.GridStep;
        int count = (int)Math.Round(2 * half / step) + 1;
        var heights = new float[count * count];
        // The bed is held a grid step beyond the road's 1 m shoulders so every triangle under the wheels lies on it.
        double roadCore = road.Width / 2 + 1 + step;
        Parallel.For(0, count, j =>
        {
            double y = -half + j * step;
            for (int i = 0; i < count; i++)
            {
                double x = -half + i * step;
                double h = Height(x, y);
                h = CarveStream(x, y, h);
                h = DigLake(x, y, h);
                if (road.Nearest(x, y, roadCore + BankReach) is { } near)
                {
                    h = near.Distance <= roadCore ? near.Profile : CutAndFill(h, road.Within(x, y, roadCore + BankReach), roadCore);
                    h = UnderBridge(x, y, h, road.StreamCrossing, near.Distance, roadCore);
                }
                h = Level(x, y, h);
                heights[j * count + i] = (float)h;
            }
        });
        return new HeightGrid(-half, -half, step, count, heights);
    }

    /// <summary>
    /// The drop below the shoulder at <paramref name="below"/> m west of the crest: the smooth profile scaled so the
    /// cliff bands make up the rest of the 350 m, the bands (strength varying along the crest, softened where the road
    /// climbs), ridged noise growing down the slope, and the floor's gentle fall to the south.
    /// </summary>
    static double WestFace(double below, double x, double y)
    {
        var (strength, roadSection) = BandStrength(y);

        double bandsTotal = 0, bandsDrop = 0;
        for (int k = 0; k < Bands.Length; k++)
        {
            var (_, height, width) = Bands[k];
            double at = BandCentre(k, y);
            bandsTotal += strength * height;
            bandsDrop += strength * height * SmoothStep((below - at + width / 2) / width);
        }
        double h = -(FaceDrop - bandsTotal) * Profile(below) - bandsDrop;

        double envelope = SmoothStep(below / 300) * (1 - 0.5 * SmoothStep((below - 700) / 300)) * (1 - 0.5 * roadSection);
        h += 12 * envelope * (2 * Noise.Ridged(x / 110, y / 110, 4) - 1);
        h += SmoothStep(below / FaceLength) * (5 - 0.0065 * Math.Max(0, 500 - y));
        return h;
    }

    /// <summary>
    /// How much the cliff bands show at (x, y), each 0…1 and scaled by the bands' strength there: <c>Rock</c> on the
    /// bands themselves (fading over 6 m beyond their width), <c>Scree</c> in a 20–60 m apron below each band's foot
    /// (its length varying along the crest).
    /// </summary>
    public static (double Rock, double Scree) CliffBands(double x, double y)
    {
        double below = CrestX(y) - x;
        if (below <= 0) return (0, 0);
        double strength = BandStrength(y).Strength;
        if (strength <= 0) return (0, 0);
        double rock = 0, scree = 0;
        for (int k = 0; k < Bands.Length; k++)
        {
            double at = BandCentre(k, y), width = Bands[k].Width;
            rock = Math.Max(rock, 1 - SmoothStep((Math.Abs(below - at) - width / 2) / 6));
            double foot = below - (at + width / 2), apron = 40 + 20 * Noise.Value(29.1 * (k + 1), y / 180);
            scree = Math.Max(scree, SmoothStep((foot + 5) / 10) * (1 - SmoothStep((foot - apron) / 15)));
        }
        return (strength * rock, strength * scree);
    }

    /// <summary>The cliff bands' strength along the crest (0…1), softened where the road climbs, and how far into the
    /// road's section of the face y lies (0…1).</summary>
    static (double Strength, double RoadSection) BandStrength(double y)
    {
        double strength = Math.Clamp(0.6 + 0.9 * Noise.Value(3.7, y / 350), 0, 1);
        double roadSection = SmoothStep((y + 1130) / 150) * (1 - SmoothStep((y + 480) / 150));
        return (strength * (1 - 0.85 * roadSection), roadSection);
    }

    /// <summary>Distance below the crest of band <paramref name="k"/>'s centre, wandering ±25 m along the crest.</summary>
    static double BandCentre(int k, double y) => Bands[k].Below + 25 * Noise.Value(17.3 * (k + 1), y / 250);

    /// <summary>The climb east of the shoulder with its ridged roughness, merged with the north-east summit.</summary>
    static double East(double x, double y)
    {
        double ramp = EastRise * SmoothStep((x - EastStart) / EastRun);
        ramp += EastRoughness * SmoothStep((x - EastStart) / 1200) * (2 * Noise.Ridged(x / 260, y / 260, 5) - 1);
        double sx = x - SummitX, sy = y - SummitY;
        double bell = Math.Exp(-(sx * sx + sy * sy) / (2 * SummitSigma * SummitSigma));
        double summit = bell * (SummitHeight + 20 * Noise.Fbm(x / 120, y / 120, 3));
        // Smooth maximum whose rounding fades with the summit, so it adds nothing far from it.
        double k = 80 * bell, m = Math.Max(ramp, summit);
        if (k < 1e-6) return m;
        double t = Math.Max(k - Math.Abs(ramp - summit), 0) / k;
        return m + t * t * k / 4;
    }

    /// <summary>
    /// The ground beside a road bed: every nearby stretch of road allows the ground to differ from its bed height by
    /// a bank of <see cref="BankSlope"/> from the bed's edge (steepening past <see cref="BankRun"/>, so the banks always
    /// meet the natural ground). The natural height is clamped into what all of them allow, so where two legs of the
    /// switchback are close the ground between them stays continuous.
    /// </summary>
    static double CutAndFill(double natural, IEnumerable<(double Distance, double Profile)> road, double core)
    {
        double low = double.NegativeInfinity, high = double.PositiveInfinity;
        foreach (var (distance, profile) in road)
        {
            double outside = Math.Max(distance - core, 0), steeper = Math.Max(outside - BankRun, 0);
            double rise = BankSlope * outside + 0.05 * steeper * steeper;
            low = Math.Max(low, profile - rise);
            high = Math.Min(high, profile + rise);
        }
        return low > high ? (low + high) / 2 : Math.Clamp(natural, low, high);
    }

    /// <summary>
    /// Near the road's stream crossing, the channel cut again through the road's banks and under the deck, below both
    /// the natural ground and the road surface. Near the road only nodes a cell diagonal inside the deck's ends are
    /// cut, so every grid triangle under the road off the deck keeps the road's height and meets the deck's ends.
    /// </summary>
    static double UnderBridge(double x, double y, double h, (double X, double Y, double YawDeg, double Profile) crossing,
        double roadDistance, double roadCore)
    {
        double dx = x - crossing.X, dy = y - crossing.Y;
        if (dx * dx + dy * dy > BridgeReach * BridgeReach) return h;
        double cut = StreamCut(StreamDistance(x, y));
        if (cut <= 0) return h;
        double diagonal = MountainMap.GridStep * Math.Sqrt(2);
        var (along, _) = PlanarYaw.ToLocal(dx, dy, crossing.YawDeg);
        if (roadDistance <= roadCore + diagonal && Math.Abs(along) > BridgeLength / 2 - diagonal) return h;
        return Math.Min(h, Math.Min(Height(x, y), crossing.Profile) - cut);
    }

    static double CarveStream(double x, double y, double h) => h - StreamCut(StreamDistance(x, y));

    /// <summary>Distance to the stream's centre line.</summary>
    public static double StreamDistance(double x, double y)
    {
        double d = double.PositiveInfinity;
        for (int k = 1; k < Stream.Count; k++) d = Math.Min(d, SegmentDistance(x, y, Stream[k - 1], Stream[k]));
        return d;
    }

    /// <summary>The channel's depth at <paramref name="d"/> m from the stream's centre line: flat 1.5 m either side,
    /// easing out to 0 at 7.5 m.</summary>
    static double StreamCut(double d) => d > 7.5 ? 0 : StreamDepth * (1 - SmoothStep((d - 1.5) / 6));

    /// <summary>Horizontal distance outside the lake's shore, measured along the ray from its centre; 0 or less
    /// inside.</summary>
    public static double OutsideLake(double x, double y)
    {
        double dx = x - LakeX, dy = y - LakeY;
        double r = Math.Sqrt((dx / LakeSemiX) * (dx / LakeSemiX) + (dy / LakeSemiY) * (dy / LakeSemiY));
        return r < 1e-9 ? -LakeSemiY : Math.Sqrt(dx * dx + dy * dy) * (1 - 1 / r);
    }

    /// <summary>Inside the shore a bowl 2 m below the level at the edge and 15 m at the centre; outside it the ground
    /// blends back to the natural relief over 30 m and stays above the water.</summary>
    static double DigLake(double x, double y, double h)
    {
        double dx = x - LakeX, dy = y - LakeY;
        double r2 = (dx / LakeSemiX) * (dx / LakeSemiX) + (dy / LakeSemiY) * (dy / LakeSemiY);
        double level = MountainMap.LakeLevel;
        if (r2 <= 1) return level - 2 - 13 * (1 - r2);
        double outside = OutsideLake(x, y);
        if (outside >= LakeBlend) return h;
        double bank = level + 0.3;
        return Math.Max(bank + (h - bank) * SmoothStep(outside / LakeBlend), bank);
    }

    /// <summary>The strip rectangle and the pilot's disc (each widened by a grid cell) exactly at 0.</summary>
    static double Level(double x, double y, double h)
    {
        double sx = Math.Max(Math.Max(25 - x, x - 165), 0), sy = Math.Max(Math.Abs(y) - 16, 0);
        double strip = SmoothStep(Math.Sqrt(sx * sx + sy * sy) / StripBlend);
        var pilot = MountainMap.Layout.PilotPosition;
        double pd = Math.Sqrt((x - pilot.X) * (x - pilot.X) + (y - pilot.Y) * (y - pilot.Y));
        double pilotArea = SmoothStep((pd - PilotRadius - CellMargin) / PilotBlend);
        return h * strip * pilotArea;
    }

    static double Profile(double below)
    {
        double u = below / ProfileStep;
        if (u >= FaceProfile.Length - 1) return 1;
        int i = (int)u;
        return FaceProfile[i] + (u - i) * (FaceProfile[i + 1] - FaceProfile[i]);
    }

    /// <summary>Integrates the face's slope shape: rising from 0 over the first 45 m (the rounded brow), steady to
    /// 250 m, then easing to 0 at <see cref="FaceLength"/>; normalised to end at 1.</summary>
    static double[] BuildFaceProfile()
    {
        int n = (int)(FaceLength / ProfileStep) + 1;
        var p = new double[n];
        for (int i = 1; i < n; i++)
        {
            double d = i * ProfileStep;
            double slope = SmoothStep(d / 45) * (d < 250 ? 1 : Math.Pow(Math.Max(0, 1 - (d - 250) / 680), 1.1));
            p[i] = p[i - 1] + slope * ProfileStep;
        }
        for (int i = 0; i < n; i++) p[i] /= p[n - 1];
        return p;
    }

    static double SegmentDistance(double x, double y, (double X, double Y) a, (double X, double Y) b)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y;
        double t = Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / (ex * ex + ey * ey), 0, 1);
        double dx = a.X + t * ex - x, dy = a.Y + t * ey - y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
