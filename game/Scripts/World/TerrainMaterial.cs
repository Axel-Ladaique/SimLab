using System.Collections.Generic;
using System.Linq;
using Godot;
using SimLab.App.Maps;

namespace SimLab.Game.World;

/// <summary>Builds the terrain's shader material: the surface textures packed into two texture arrays (albedo,
/// normal) plus the map's per-surface tints.</summary>
public static class TerrainMaterial
{
    const string Folder = "res://Textures/terrain/";
    const int LayerSize = 1024;

    /// <summary>File stem of each named layer, with the stem used instead while that file does not exist. In
    /// <see cref="SurfaceKind"/> order (grass, mowed grass and wheat all read the grass layer).</summary>
    static readonly (string Stem, string Fallback)[] Layers =
    {
        ("grass", "grass"),   // 0: grass, mowed grass, wheat
        ("dirt", "dirt"),     // 1: dirt
        ("gravel", "gravel"), // 2: gravel
        ("soil", "soil"),     // 3: ploughed
        ("rock", "gravel"),   // 4: rock
        ("snow", "gravel"),   // 5: snow
        ("needles", "soil"),  // 6: needles
    };

    /// <summary>Named layer used by each <see cref="SurfaceKind"/>, indexing into <see cref="Layers"/>.</summary>
    static readonly int[] KindLayer = { 0, 0, 1, 2, 0, 3, 4, 5, 6 };

    // Built once: the menu rebuilds the map scene on every visit.
    static Texture2DArray? _albedo, _normal;
    static int[]? _slots;

    public static ShaderMaterial Create(MapAmbience ambience)
    {
        // A missing layer's slot is decided once, from whether its own albedo file exists, and reused for both
        // arrays and for the per-kind layer_index below, so a fallback (rock and snow onto gravel, needles onto
        // soil) reads the same array slot as the layer it stands in for, rather than a redundant copy of it.
        var slots = _slots ??= LayerSlots();
        _albedo ??= BuildArray("albedo", slots);
        _normal ??= BuildArray("normal", slots);
        var tints = ambience.TerrainTints ?? MapAmbience.DefaultTerrainTints;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/terrain.gdshader") };
        material.SetShaderParameter("albedo_layers", _albedo);
        material.SetShaderParameter("normal_layers", _normal);
        material.SetShaderParameter("tints", tints.Select(t => new Vector3(t.R, t.G, t.B)).ToArray());
        material.SetShaderParameter("layer_index", KindLayer.Select(named => slots[named]).ToArray());
        return material;
    }

    /// <summary>Which array slot each named <see cref="Layers"/> entry occupies: its own slot when its albedo file
    /// exists, otherwise the fallback layer's slot, so no layer that is only ever a stand-in gets one of its own.</summary>
    static int[] LayerSlots()
    {
        var slotOfStem = new Dictionary<string, int>();
        var slots = new int[Layers.Length];
        for (int i = 0; i < Layers.Length; i++)
        {
            var (stem, fallback) = Layers[i];
            var resolved = ResourceLoader.Exists($"{Folder}{stem}_albedo.jpg") ? stem : fallback;
            if (!slotOfStem.TryGetValue(resolved, out int slot)) slotOfStem[resolved] = slot = slotOfStem.Count;
            slots[i] = slot;
        }
        return slots;
    }

    static Texture2DArray BuildArray(string map, int[] slots)
    {
        var images = new Image?[slots.Max() + 1];
        for (int i = 0; i < Layers.Length; i++)
        {
            int slot = slots[i];
            if (images[slot] is not null) continue;
            var (stem, fallback) = Layers[i];
            var path = $"{Folder}{stem}_{map}.jpg";
            if (!ResourceLoader.Exists(path)) path = $"{Folder}{fallback}_{map}.jpg";
            images[slot] = Uniform(GD.Load<Texture2D>(path).GetImage(), normal: map == "normal");
        }
        var array = new Texture2DArray();
        var error = array.CreateFromImages(new Godot.Collections.Array<Image>(images!));
        if (error != Error.Ok) GD.PushError($"Terrain {map} texture array: {error}");
        return array;
    }

    /// <summary>Brings an imported (VRAM-compressed) texture to the one size and format the array requires.
    /// The importer's own mipmaps are kept when the size already fits: regenerated ones measurably
    /// flatten the normal-mapped relief at a grazing view. Normal maps come out of their two-channel
    /// compression with z = 0; z is rebuilt before regenerating so the mipmaps can be renormalised.</summary>
    static Image Uniform(Image image, bool normal)
    {
        if (image.IsCompressed()) image.Decompress();
        image.Convert(Image.Format.Rgba8);
        if (image.HasMipmaps() && image.GetWidth() == LayerSize && image.GetHeight() == LayerSize) return image;
        image.ClearMipmaps();
        if (image.GetWidth() != LayerSize || image.GetHeight() != LayerSize)
            image.Resize(LayerSize, LayerSize, Image.Interpolation.Lanczos);
        if (normal) RebuildZ(image);
        image.GenerateMipmaps(renormalize: normal);
        return image;
    }

    static void RebuildZ(Image image)
    {
        var data = image.GetData();
        for (int p = 0; p < data.Length; p += 4)
        {
            float x = data[p] / 127.5f - 1f, y = data[p + 1] / 127.5f - 1f;
            float z = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y));
            data[p + 2] = (byte)Mathf.RoundToInt((z + 1f) * 127.5f);
        }
        image.SetData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, data);
    }
}
