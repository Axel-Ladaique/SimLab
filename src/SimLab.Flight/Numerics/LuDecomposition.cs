namespace SimLab.Flight.Numerics;

/// <summary>LU factorization with partial pivoting of a square matrix: factored once, solved for many right-hand sides.</summary>
public sealed class LuDecomposition
{
    readonly double[,] _lu;
    readonly int[] _pivot;

    public LuDecomposition(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n) throw new ArgumentException("The matrix must be square.", nameof(matrix));
        _lu = (double[,])matrix.Clone();
        _pivot = new int[n];
        for (int i = 0; i < n; i++) _pivot[i] = i;
        for (int k = 0; k < n; k++)
        {
            int row = k;
            double largest = Math.Abs(_lu[k, k]);
            for (int i = k + 1; i < n; i++)
            {
                if (Math.Abs(_lu[i, k]) > largest)
                {
                    largest = Math.Abs(_lu[i, k]);
                    row = i;
                }
            }
            if (largest < 1e-14) throw new InvalidOperationException("The matrix is singular.");
            if (row != k)
            {
                for (int c = 0; c < n; c++) (_lu[k, c], _lu[row, c]) = (_lu[row, c], _lu[k, c]);
                (_pivot[k], _pivot[row]) = (_pivot[row], _pivot[k]);
            }
            for (int i = k + 1; i < n; i++)
            {
                double factor = _lu[i, k] /= _lu[k, k];
                for (int c = k + 1; c < n; c++) _lu[i, c] -= factor * _lu[k, c];
            }
        }
        Size = n;
    }

    public int Size { get; }

    /// <summary>Solves A x = <paramref name="rhs"/>. <paramref name="x"/> must not alias <paramref name="rhs"/>.</summary>
    public void Solve(ReadOnlySpan<double> rhs, Span<double> x)
    {
        int n = Size;
        if (rhs.Length != n || x.Length != n) throw new ArgumentException("Vector lengths must match the matrix size.");
        for (int i = 0; i < n; i++)
        {
            double sum = rhs[_pivot[i]];
            for (int c = 0; c < i; c++) sum -= _lu[i, c] * x[c];
            x[i] = sum;
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = x[i];
            for (int c = i + 1; c < n; c++) sum -= _lu[i, c] * x[c];
            x[i] = sum / _lu[i, i];
        }
    }
}
