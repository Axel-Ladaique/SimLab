using System.Collections.Generic;
using System.Linq;

namespace SimLab.App.Field;

/// <param name="NameKey">Translation key of the name shown in the menu.</param>
public readonly record struct FieldEntry(string Id, string NameKey);

/// <summary>The flying fields the game offers. Only the club field exists; the flight always uses it.</summary>
public static class FieldCatalog
{
    public static IReadOnlyList<FieldEntry> All { get; } = [new("club", "FIELD_CLUB")];

    /// <summary>The field with this id, or the first one when it is unknown.</summary>
    public static FieldEntry Find(string id) => All.FirstOrDefault(f => f.Id == id, All[0]);
}
