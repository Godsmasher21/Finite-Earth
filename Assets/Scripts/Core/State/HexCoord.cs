using System;
using UnityEngine;

[Serializable]
public readonly struct HexCoord : IEquatable<HexCoord>
{
    public readonly int q;
    public readonly int r;

    public HexCoord(int q, int r)
    {
        this.q = q;
        this.r = r;
    }

    public Vector3Int ToVector3Int()
    {
        return new Vector3Int(q, r, 0);
    }

    public static HexCoord FromVector3Int(Vector3Int cell)
    {
        return new HexCoord(cell.x, cell.y);
    }

    public bool Equals(HexCoord other)
    {
        return q == other.q && r == other.r;
    }

    public override bool Equals(object obj)
    {
        return obj is HexCoord other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (q * 397) ^ r;
        }
    }

    public override string ToString()
    {
        return $"({q}, {r})";
    }
}
