using System;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Infrastructure
{
    public struct TileId : IEquatable<TileId>
    {
        public TileId(int zoom, int x, int y)
        {
            Zoom = zoom;
            X = x;
            Y = y;
        }

        public int Zoom { get; }
        public int X { get; }
        public int Y { get; }

        public bool Equals(TileId other) => Zoom == other.Zoom && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is TileId t && Equals(t);
        public override int GetHashCode() => (Zoom * 397) ^ (X * 7919) ^ Y;
        public override string ToString() => Zoom + "/" + X + "/" + Y;
    }

    /// <summary>
    /// A map tile. Either an image from a real tile server (ImagePng) or a procedural description the renderer turns
    /// into the ink-scroll style (TerrainSeed). Game mechanics never depend on tiles: they use server coordinates.
    /// </summary>
    public sealed class MapTile
    {
        public MapTile(TileId id, byte[] imagePng, int terrainSeed)
        {
            Id = id;
            ImagePng = imagePng;
            TerrainSeed = terrainSeed;
        }

        public TileId Id { get; }
        public byte[] ImagePng { get; }
        public int TerrainSeed { get; }
    }

    /// <summary>
    /// Map provider abstraction (spec 4/7.5). The prototype uses <see cref="ProceduralMapProvider"/>; a licensed tile
    /// service or our own renderer can replace it without touching game logic.
    /// </summary>
    public interface IMapProvider
    {
        string Attribution { get; }
        Task<MapTile> GetTileAsync(TileId id, CancellationToken ct);
    }

    /// <summary>No network, no third-party data: deterministic terrain seeds per tile for the scroll-style renderer.</summary>
    public sealed class ProceduralMapProvider : IMapProvider
    {
        public string Attribution => "Jianghu Go (procedural)";

        public Task<MapTile> GetTileAsync(TileId id, CancellationToken ct)
        {
            unchecked
            {
                var h = (id.Zoom * 73856093) ^ (id.X * 19349663) ^ (id.Y * 83492791);
                return Task.FromResult(new MapTile(id, null, h));
            }
        }
    }

    /// <summary>Web Mercator (EPSG:3857) helpers shared by every provider: slippy-map tile numbering, pixel positions.</summary>
    public static class WebMercator
    {
        public const int TileSize = 256;
        public const double MaxLatitude = 85.05112878;

        public static TileId TileFor(double lat, double lon, int zoom)
        {
            var (px, py) = ToPixel(lat, lon, zoom);
            var max = (1 << zoom) - 1;
            return new TileId(zoom, Math.Min(max, (int)(px / TileSize)), Math.Min(max, (int)(py / TileSize)));
        }

        /// <summary>Global pixel coordinates at <paramref name="zoom"/> (origin top-left at 180°W, 85.05°N).</summary>
        public static (double X, double Y) ToPixel(double lat, double lon, int zoom)
        {
            lat = Math.Max(-MaxLatitude, Math.Min(MaxLatitude, lat));
            var size = TileSize * Math.Pow(2, zoom);
            var x = (lon + 180) / 360 * size;
            var sin = Math.Sin(lat * Math.PI / 180);
            var y = (0.5 - (Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI))) * size;
            return (x, y);
        }

        public static (double Lat, double Lon) FromPixel(double x, double y, int zoom)
        {
            var size = TileSize * Math.Pow(2, zoom);
            var lon = (x / size * 360) - 180;
            var n = Math.PI - (2 * Math.PI * y / size);
            var lat = 180 / Math.PI * Math.Atan(Math.Sinh(n));
            return (lat, lon);
        }
    }
}
