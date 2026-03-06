using System;
using UnityEngine;
using UnityEngine.Tilemaps;

[Serializable]
public struct TileColor
{
    public TileType type;
    public TileBase tile;
    public Color color;
}

[CreateAssetMenu(menuName = "FiniteEarth/TileTypeColors")]
public class TileTypeColors : ScriptableObject
{
    public TileColor[] colors;
    public TileBase defaultTile;
    public Color defaultColor = Color.white;

    public Color Get(TileType type, Color fallback)
    {
        if (colors == null) return fallback;

        for (int i = 0; i < colors.Length; i++)
        {
            if (colors[i].type == type) return colors[i].color;
        }
        return fallback;
    }

    public TileBase GetTile(TileType type, TileBase fallback = null)
    {
        if (colors == null)
        {
            if (defaultTile != null) return defaultTile;
            return fallback;
        }

        for (int i = 0; i < colors.Length; i++)
        {
            if (colors[i].type == type && colors[i].tile != null) return colors[i].tile;
        }

        if (defaultTile != null) return defaultTile;
        return fallback;
    }
}
