using Godot;

namespace Eden.Game.Art;

/// <summary>
/// The art-reference palette (docs/art/references/README.md): saturated grass green, warm brown dirt and wood,
/// burnt-orange / terracotta roofs, cool grey-blue stone, cream plaster, turquoise-to-deep-blue water. Kenney's own
/// colours (teal roofs, lavender stone) are remapped here when meshes are baked, so every pack reads as one family.
/// All colours are sRGB.
/// </summary>
public static class Palette
{
    public static readonly Color Grass = Hex(0x7FBF4D);
    public static readonly Color GrassDark = Hex(0x5E9E3A);
    public static readonly Color LeafLight = Hex(0x8CC152);
    public static readonly Color Leaf = Hex(0x5FA03C);
    public static readonly Color LeafDark = Hex(0x3E7F38);
    public static readonly Color Pine = Hex(0x2F6E3F);
    public static readonly Color PineLight = Hex(0x3F8A49);
    public static readonly Color Autumn = Hex(0xD98B2B);
    public static readonly Color Bark = Hex(0x7A4E2D);
    public static readonly Color BarkDark = Hex(0x5A3A22);
    public static readonly Color Dirt = Hex(0x8A5A35);
    public static readonly Color DirtDark = Hex(0x6B4428);
    public static readonly Color Wood = Hex(0x8B5A33);
    public static readonly Color Roof = Hex(0xC8643A);
    public static readonly Color RoofDark = Hex(0xA14B2C);
    public static readonly Color Plaster = Hex(0xEDE2C8);
    public static readonly Color Stone = Hex(0xCFC7B6);
    public static readonly Color StoneDark = Hex(0x9E978A);
    public static readonly Color Brick = Hex(0xA65A3F);
    public static readonly Color Water = Hex(0x3FC1C9);
    public static readonly Color Wheat = Hex(0xE3C35A);
    public static readonly Color Berry = Hex(0xC8323C);
    public static readonly Color Ore = Hex(0xB8743A);

    /// <summary>Settlement accent colours (banners, clothing): strong, distinct hues that read at 140 m.</summary>
    public static readonly Color[] Settlements =
    [
        Hex(0xD9453B), Hex(0x3C78D8), Hex(0xF2C230), Hex(0x8E5BC9),
        Hex(0xF07F2A), Hex(0x2BB3A6), Hex(0xE068A8), Hex(0x8DBA3A),
    ];

    public static Color SettlementColor(int settlementId)
        => settlementId < 0 ? Hex(0xB9B2A3) : Settlements[(int)((uint)settlementId % Settlements.Length)];

    public static Color Hex(uint rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    /// <summary>Kenney Nature Kit materials are flat colours named by role; map each role to the palette.</summary>
    public static Color NatureMaterial(string material, Color original) => material switch
    {
        "leafsGreen" => Leaf,
        "leafsDark" => Pine,
        "leafsFall" or "leafsOrange" => Autumn,
        "woodBark" => Bark,
        "woodBarkDark" => BarkDark,
        "wood" => Wood,
        "woodDark" => BarkDark,
        "woodInner" => Hex(0xE8C99A),
        "grass" => Grass,
        "grassDark" => GrassDark,
        "dirt" => Dirt,
        "dirtDark" => DirtDark,
        "stone" or "stoneDark" => material == "stone" ? Stone : StoneDark,
        "rock" => StoneDark,
        _ => original,
    };

    /// <summary>How Kenney Fantasy Town Kit's lavender "stone" cells are painted for a building part.</summary>
    public enum StoneAs { Plaster, Stone, Brick }

    /// <summary>
    /// Remaps a Fantasy Town Kit colormap texel: teal and red roofs to terracotta, lavender stone to plaster/stone/brick,
    /// browns kept (slightly warmer), light blue water to turquoise. Value (the gradient inside a palette cell) is kept.
    /// </summary>
    public static Color TownTexel(Color c, StoneAs stoneAs)
    {
        c.ToHsv(out float h, out float s, out float v);
        float deg = h * 360f;
        if (s > 0.25f && deg is >= 140f and <= 200f) return Tone(Roof, v, 0.72f); // teal roof tiles
        if (s > 0.45f && (deg <= 12f || deg >= 340f)) return Tone(Roof, v, 0.80f); // red roof tiles, banners
        if (deg is >= 200f and <= 275f && s < 0.45f) // lavender / grey-blue stone and plaster
        {
            var target = stoneAs switch { StoneAs.Plaster => Plaster, StoneAs.Brick => Brick, _ => Stone };
            return Tone(target, v, 0.85f);
        }
        if (s > 0.3f && deg is >= 190f and <= 215f) return Water;
        if (deg is >= 12f and <= 45f && s > 0.25f) return c.Lerp(Wood, 0.35f); // wood: keep the kit's grain, warm it
        return c;
    }

    /// <summary>Keeps the brightness variation of <paramref name="value"/> around <paramref name="reference"/> on a target colour.</summary>
    private static Color Tone(Color target, float value, float reference)
    {
        float k = Math.Clamp(value / reference, 0.6f, 1.25f);
        return new Color(Math.Min(1f, target.R * k), Math.Min(1f, target.G * k), Math.Min(1f, target.B * k));
    }
}
