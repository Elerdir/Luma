namespace Luma.Domain.Media;

/// <summary>
/// Orders names the way a person reads them: "Episode 2" comes before "Episode 10",
/// where a plain string sort puts 10 first because '1' sorts under '2'. Runs of digits
/// are compared as numbers, everything else case-insensitively — which is what makes a
/// folder of episodes line up in broadcast order.
/// </summary>
public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer Instance { get; } = new();

    private NaturalNameComparer() { }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var byNumber = CompareNumbers(x, ref i, y, ref j);
                if (byNumber != 0)
                    return byNumber;
                continue;
            }

            var left = char.ToUpperInvariant(x[i]);
            var right = char.ToUpperInvariant(y[j]);
            if (left != right)
                return left.CompareTo(right);

            i++;
            j++;
        }

        // Ran out of one of them: the shorter remainder sorts first. Falling back to an
        // ordinal comparison keeps names that differ only in case or in leading zeros
        // ("S01E01" and "S1E1") in a stable, repeatable order rather than tied.
        var byRemainder = (x.Length - i).CompareTo(y.Length - j);
        return byRemainder != 0 ? byRemainder : string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// Compare the digit runs starting at the two cursors and leave both just past them.
    /// Leading zeros are skipped, so "07" and "7" are the same number; a longer run of
    /// significant digits is the larger number.
    /// </summary>
    private static int CompareNumbers(string x, ref int i, string y, ref int j)
    {
        while (i < x.Length && x[i] == '0') i++;
        while (j < y.Length && y[j] == '0') j++;

        var startX = i;
        var startY = j;
        while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
        while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

        var lengthX = i - startX;
        var lengthY = j - startY;
        return lengthX != lengthY
            ? lengthX.CompareTo(lengthY)
            : string.CompareOrdinal(x, startX, y, startY, lengthX);
    }
}
