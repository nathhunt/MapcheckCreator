using System;

namespace SrsMcPsqa.Core.Optimisation
{
    /// <summary>2D summed-area table: O(1) sum over any axis-aligned rectangle of pixels.</summary>
    public sealed class SummedAreaTable
    {
        private readonly double[] _t;

        /// <param name="values">Row-major values, index i + width·l.</param>
        public SummedAreaTable(double[] values, int width, int height)
        {
            if (values.Length != width * height) throw new ArgumentException("Size mismatch.");
            Width = width; Height = height;
            int w1 = width + 1;
            _t = new double[w1 * (height + 1)];
            for (int l = 0; l < height; l++)
            {
                double row = 0;
                for (int i = 0; i < width; i++)
                {
                    row += values[i + width * l];
                    _t[(i + 1) + w1 * (l + 1)] = _t[(i + 1) + w1 * l] + row;
                }
            }
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Sum over pixels i ∈ [i0, i1), l ∈ [l0, l1). Indices are clamped to the table.</summary>
        public double Sum(int i0, int i1, int l0, int l1)
        {
            if (i0 < 0) i0 = 0; if (l0 < 0) l0 = 0;
            if (i1 > Width) i1 = Width; if (l1 > Height) l1 = Height;
            if (i1 <= i0 || l1 <= l0) return 0;
            int w1 = Width + 1;
            return _t[i1 + w1 * l1] - _t[i0 + w1 * l1] - _t[i1 + w1 * l0] + _t[i0 + w1 * l0];
        }

        public double Total => _t[_t.Length - 1];
    }
}
