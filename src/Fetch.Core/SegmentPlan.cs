namespace Fetch.Core;

public readonly record struct ByteRange(long Start, long End)
{
    public long Length => checked(End - Start + 1);
}

public static class SegmentPlan
{
    public static IReadOnlyList<ByteRange> Split(long totalBytes, int segments)
    {
        if (totalBytes <= 0) throw new ArgumentOutOfRangeException(nameof(totalBytes));
        if (segments < 1 || segments > 32 || segments > totalBytes)
            throw new ArgumentOutOfRangeException(nameof(segments));
        var result = new List<ByteRange>(segments);
        long start = 0;
        long common = totalBytes / segments;
        long remainder = totalBytes % segments;
        for (int i = 0; i < segments; i++)
        {
            long size = common + (i < remainder ? 1 : 0);
            result.Add(new ByteRange(start, checked(start + size - 1)));
            start += size;
        }
        return result;
    }

    public static int Recommend(long? totalBytes, bool supportsRanges, int desired = 8, long minSize = 1_048_576)
    {
        if (!supportsRanges || !totalBytes.HasValue || totalBytes.Value < minSize * 2) return 1;
        return (int)Math.Clamp(totalBytes.Value / minSize, 1, Math.Clamp(desired, 1, 32));
    }
}
