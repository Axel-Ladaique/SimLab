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

    /// <summary>File stem of each texture layer, with the stem used instead while that file does not exist.
    /// The shader's LAYER table maps each SurfaceKind to one of these indices.</summary>
    static readonly (string Stem, string Fallback)[] Layers =
    {
        ("grass", "grass"),   // 0
        ("dirt", "dirt"),     // 1
        ("gravel", "gravel"), // 2
        ("soil", "soil"),     // 3
        ("rock", "gravel"),   // 4
        ("snow", "gravel"),   // 5
        ("needles", "soil"),  // 6
    };

    // Built once: the menu rebuilds the map scene on every visit.
    static Texture2DArray? _albedo, _normal;

    public static ShaderMaterial Create(MapAmbience ambience)
    {
        _albedo ??= BuildArray("albedo");
        _normal ??= BuildArray("normal");
        var tints = ambience.TerrainTints ?? MapAmbience.DefaultTerrainTints;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/terrain.gdshader") };
        material.SetShaderParameter("albedo_layers", _albedo);
        material.SetShaderParameter("normal_layers", _normal);
        material.SetShaderParameter("tints", tints.Select(t => new Vector3(t.R, t.G, t.B)).ToArray());
        return material;
    }

    static Texture2DArray BuildArray(string map)
    {
        // Fallback layers share the same file; decode each file once.
        var decoded = new Dictionary<string, Image>();
        var images = new Godot.Collections.Array<Image>();
        foreach (var (stem, fallback) in Layers)
        {
            var path = $"{Folder}{stem}_{map}.jpg";
            if (!ResourceLoader.Exists(path)) path = $"{Folder}{fallback}_{map}.jpg";
            if (!decoded.TryGetValue(path, out var image))
                decoded[path] = image = Uniform(GD.Load<Texture2D>(path).GetImage(), normal: map == "normal");
            images.Add(image);
        }
        var array = new Texture2DArray();
        var error = array.CreateFromImages(images);
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
