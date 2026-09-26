namespace SimLab.Flight.Numerics;

/// <summary>
/// LU factorization with partial pivoting of a square matrix: factored once, solved for many right-hand sides.
/// <see cref="Factor"/> refactors another matrix of the same size in place, without allocating.
/// </summary>
public sealed class LuDecomposition
{
    // Row-major n×n, flat: rows as spans keep the elimination loops free of 2D index arithmetic and bounds checks.
    readonly double[] _lu;
    readonly int[] _pivot;

    public LuDecomposition(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n) throw new ArgumentException("The matrix must be square.", nameof(matrix));
        _lu = new double[n * n];
        _pivot = new int[n];
        Size = n;
        Factor(matrix);
    }

    public int Size { get; }

    /// <summary>Factors <paramref name="matrix"/> (same size) in place of the current factorization.</summary>
    public void Factor(double[,] matrix)
    {
        int n = Size;
        if (matrix.GetLength(0) != n || matrix.GetLength(1) != n) throw new ArgumentException("The matrix size must not change.", nameof(matrix));
        Buffer.BlockCopy(matrix, 0, _lu, 0, n * n * sizeof(double));
        var lu = _lu.AsSpan();
        for (int i = 0; i < n; i++) _pivot[i] = i;
        for (int k = 0; k < n; k++)
        {
            int row = k;
            double largest = Math.Abs(lu[k * n + k]);
            for (int i = k + 1; i < n; i++)
            {
                double v = Math.Abs(lu[i * n + k]);
                if (v > largest)
                {
                    largest = v;
                    row = i;
                }
            }
            if (largest < 1e-14) throw new InvalidOperationException("The matrix is singular.");
            var pivotRow = lu.Slice(k * n, n);
            if (row != k)
            {
                var other = lu.Slice(row * n, n);
                for (int c = 0; c < n; c++) (pivotRow[c], other[c]) = (other[c], pivotRow[c]);
                (_pivot[k], _pivot[row]) = (_pivot[row], _pivot[k]);
            }
            double diagonal = pivotRow[k];
            var pivotTail = pivotRow.Slice(k + 1);
            for (int i = k + 1; i < n; i++)
            {
                var target = lu.Slice(i * n, n);
                double factor = target[k] /= diagonal;
                if (factor == 0) continue;
                var tail = target.Slice(k + 1);
                for (int c = 0; c < tail.Length; c++) tail[c] -= factor * pivotTail[c];
            }
        }
    }

    /// <summary>Solves A x = <paramref name="rhs"/>. <paramref name="x"/> must not alias <paramref name="rhs"/>.</summary>
    public void Solve(ReadOnlySpan<double> rhs, Span<double> x)
    {
        int n = Size;
        if (rhs.Length != n || x.Length != n) throw new ArgumentException("Vector lengths must match the matrix size.");
        ReadOnlySpan<double> lu = _lu;
        for (int i = 0; i < n; i++)
        {
            var row = lu.Slice(i * n, i);
            double sum = rhs[_pivot[i]];
            for (int c = 0; c < row.Length; c++) sum -= row[c] * x[c];
            x[i] = sum;
        }
        for (int i = n - 1; i >= 0; i--)
        {
            var row = lu.Slice(i * n, n);
            double sum = x[i];
            for (int c = i + 1; c < n; c++) sum -= row[c] * x[c];
            x[i] = sum / row[i];
        }
    }
}
