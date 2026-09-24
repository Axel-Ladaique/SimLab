using System.Text;

namespace SimLab.App.Localization;

/// <summary>Translation CSV: header <c>keys,&lt;lang&gt;,...</c>, one key per line; RFC 4180 quoting; <c>\n</c> means newline.</summary>
public sealed class TranslationTable
{
    readonly Dictionary<string, Dictionary<string, string>> _byLanguage;
    readonly List<string> _keys;

    TranslationTable(List<string> languages, List<string> keys, Dictionary<string, Dictionary<string, string>> byLanguage)
    {
        Languages = languages;
        _keys = keys;
        _byLanguage = byLanguage;
    }

    public IReadOnlyList<string> Languages { get; }
    public IReadOnlyList<string> Keys => _keys;

    public string Get(string language, string key) =>
        _byLanguage.TryGetValue(language, out var map) && map.TryGetValue(key, out var text) && text.Length > 0 ? text : key;

    public IReadOnlyList<string> MissingEntries() =>
        Languages.SelectMany(lang => _keys.Where(k => _byLanguage[lang].GetValueOrDefault(k, "").Length == 0).Select(k => $"{lang}:{k}")).ToList();

    public static TranslationTable Parse(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) throw new InvalidDataException("Translation file is empty.");
        var header = SplitLine(lines[0]);
        if (header.Count < 2 || header[0] != "keys") throw new InvalidDataException("Translation header must start with 'keys'.");
        var languages = header.Skip(1).ToList();
        var byLanguage = languages.ToDictionary(l => l, _ => new Dictionary<string, string>());
        var keys = new List<string>();
        foreach (var line in lines.Skip(1))
        {
            var fields = SplitLine(line);
            var key = fields[0];
            keys.Add(key);
            for (int i = 0; i < languages.Count; i++)
                byLanguage[languages[i]][key] = i + 1 < fields.Count ? fields[i + 1].Replace("\\n", "\n") : "";
        }
        return new TranslationTable(languages, keys, byLanguage);
    }

    static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
