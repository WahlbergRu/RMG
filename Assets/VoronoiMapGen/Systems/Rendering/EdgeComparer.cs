using System.Collections.Generic;

namespace VoronoiMapGen.Rendering
{
    public struct EdgeComparer : IEqualityComparer<(int, int)>
    {
        public bool Equals((int, int) x, (int, int) y) => x.Item1 == y.Item1 && x.Item2 == y.Item2;
        public int GetHashCode((int, int) obj) => (obj.Item1 * 397) ^ obj.Item2;
    }
}