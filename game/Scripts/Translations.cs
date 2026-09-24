using Godot;
using SimLab.App.Localization;

namespace SimLab.Game;

/// <summary>Loads the translation CSV (ignored by Godot's importer via .gdignore) into Godot's TranslationServer.</summary>
public static class Translations
{
    public static void Register(string csvPath, string language)
    {
        var table = TranslationTable.Parse(System.IO.File.ReadAllText(csvPath));
        foreach (var lang in table.Languages)
        {
            var translation = new Translation { Locale = lang };
            foreach (var key in table.Keys) translation.AddMessage(key, table.Get(lang, key));
            TranslationServer.AddTranslation(translation);
        }
        TranslationServer.SetLocale(language);
    }
}
