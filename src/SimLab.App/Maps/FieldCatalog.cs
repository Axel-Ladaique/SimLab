using SimLab.App.Maps.Club;

namespace SimLab.App.Maps;

/// <param name="NameKey">Translation key of the name shown in the menu.</param>
public sealed record FieldEntry(string Id, string NameKey, Func<FieldMap> Create);

/// <summary>The flying fields the game offers.</summary>
public static class FieldCatalog
{
    public static IReadOnlyList<FieldEntry> All { get; } = [new(ClubMap.Id, "FIELD_CLUB", ClubMap.Create)];

    static readonly Dictionary<string, Lazy<FieldMap>> Maps = All.ToDictionary(f => f.Id, f => new Lazy<FieldMap>(f.Create));

    /// <summary>The field with this id, or the first one when it is unknown.</summary>
    public static FieldEntry Find(string id) => All.FirstOrDefault(f => f.Id == id) ?? All[0];

    /// <summary>The map of the field with this id (the first one when unknown), built once and then shared.</summary>
    public static FieldMap Load(string id) => Maps[Find(id).Id].Value;
}
