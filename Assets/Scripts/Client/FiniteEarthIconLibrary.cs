using System.Collections.Generic;
using UnityEngine;

public static class FiniteEarthIconLibrary
{
    private const int GridColumns = 8;
    private const int GridRows = 8;
    private const float PixelsPerUnit = 32f;

    private static readonly Dictionary<string, Texture2D> TextureCache = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

    public static Sprite GetPlanetHealthIcon()
    {
        return GetSheet1Icon(0, 3, "planet-health");
    }

    public static Sprite GetWoodIcon()
    {
        return GetSheet1Icon(0, 0, "wood");
    }

    public static Sprite GetFoodIcon()
    {
        return GetSheet1Icon(1, 2, "food");
    }

    public static Sprite GetOreIcon()
    {
        return GetSheet1Icon(2, 2, "ore");
    }

    public static Sprite GetClimateIcon(ClimateEventType type)
    {
        switch (type)
        {
            case ClimateEventType.Heatwave:
                return GetSheet1Icon(4, 0, "climate-heatwave");
            case ClimateEventType.Wildfire:
                return GetSheet1Icon(5, 0, "climate-wildfire");
            case ClimateEventType.Flood:
                return GetSheet1Icon(6, 1, "climate-flood");
            case ClimateEventType.IceMelt:
                return GetSheet1Icon(7, 0, "climate-ice-melt");
            case ClimateEventType.DesertSpread:
                return GetSheet1Icon(6, 2, "climate-desert-spread");
            default:
                return null;
        }
    }

    private static Sprite GetSheet1Icon(int column, int rowFromTop, string cacheKey)
    {
        return GetGridIcon("Sprites/finite-earth-sheet-1", column, rowFromTop, cacheKey);
    }

    private static Sprite GetGridIcon(string resourcePath, int column, int rowFromTop, string cacheKey)
    {
        string fullCacheKey = resourcePath + ":" + cacheKey + ":" + column + ":" + rowFromTop;
        if (SpriteCache.TryGetValue(fullCacheKey, out Sprite cachedSprite) && cachedSprite != null)
        {
            return cachedSprite;
        }

        Texture2D texture = LoadTexture(resourcePath);
        if (texture == null)
        {
            return null;
        }

        int cellWidth = texture.width / GridColumns;
        int cellHeight = texture.height / GridRows;
        int clampedColumn = Mathf.Clamp(column, 0, GridColumns - 1);
        int clampedRow = Mathf.Clamp(rowFromTop, 0, GridRows - 1);
        Rect rect = new Rect(
            clampedColumn * cellWidth,
            texture.height - ((clampedRow + 1) * cellHeight),
            cellWidth,
            cellHeight);

        Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprite.name = "fe_" + cacheKey;
        SpriteCache[fullCacheKey] = sprite;
        return sprite;
    }

    private static Texture2D LoadTexture(string resourcePath)
    {
        if (TextureCache.TryGetValue(resourcePath, out Texture2D cachedTexture) && cachedTexture != null)
        {
            return cachedTexture;
        }

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null)
        {
            Debug.LogWarning("FiniteEarthIconLibrary: missing texture at Resources/" + resourcePath + ".");
            return null;
        }

        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        TextureCache[resourcePath] = texture;
        return texture;
    }
}
