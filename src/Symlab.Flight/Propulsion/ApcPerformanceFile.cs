using System.Globalization;
using System.Text.RegularExpressions;

namespace Symlab.Flight.Propulsion;

/// <summary>Reads APC "PER3" performance files (columns: V, J, Pe, Ct, Cp, ...), one block per RPM.</summary>
public static class ApcPerformanceFile
{
    static readonly Regex RpmPattern = new(@"PROP\s+RPM\s*=\s*([0-9.]+)", RegexOptions.IgnoreCase);

    public static PropellerSpec Parse(string text, double diameterM, double pitchM, double targetRpm)
    {
        var blocks = new List<(double Rpm, List<(double J, double Ct, double Cp)> Rows)>();
        List<(double J, double Ct, double Cp)>? current = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var match = RpmPattern.Match(line);
            if (match.Success)
            {
                current = [];
                blocks.Add((double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), current));
                continue;
            }
            if (current is null) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            if (!TryParse(parts[0], out _) || !TryParse(parts[1], out var j) ||
                !TryParse(parts[3], out var ct) || !TryParse(parts[4], out var cp)) continue;
            current.Add((j, ct, cp));
        }

        var usable = blocks.Where(b => b.Rows.Count >= 2).ToList();
        if (usable.Count == 0) throw new InvalidDataException("No usable 'PROP RPM =' block found in the APC file.");
        var best = usable.MinBy(b => Math.Abs(b.Rpm - targetRpm));

        var rows = best.Rows.GroupBy(r => r.J).Select(g => g.First()).OrderBy(r => r.J).ToArray();
        return new PropellerSpec(
            diameterM, pitchM,
            rows.Select(r => r.J).ToArray(),
            rows.Select(r => r.Ct).ToArray(),
            rows.Select(r => r.Cp).ToArray());
    }

    static bool TryParse(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
