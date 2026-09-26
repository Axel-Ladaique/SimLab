namespace SimLab.App.Maps;

/// <summary>A still-water surface (lake, pond): a horizontal outline and the level (world z) it sits at.</summary>
public sealed record WaterBody(IReadOnlyList<(double X, double Y)> Outline, double Level)
{
    /// <summary>Even–odd point-in-polygon test; the outline need not be convex.</summary>
    public bool Contains(double x, double y)
    {
        bool inside = false;
        int n = Outline.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var (xi, yi) = Outline[i];
            var (xj, yj) = Outline[j];
            if (((yi > y) != (yj > y)) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }
}
