namespace ReportingPlatform.Transformers.Engine;

/// <summary>
/// Applies the seven computed column transforms to adapter rows.
/// Called by the resolver AFTER the adapter returns rows and BEFORE
/// the widget transformer executes. Rows are immutable — each transform
/// appends new key-value pairs to a copied row dictionary.
/// </summary>
public sealed class ComputedColumnEngine : IComputedColumnEngine
{
    public IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Apply(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        IReadOnlyList<TableColumn> computedColumns)
    {
        // Fast-path: nothing to compute
        var cols = computedColumns.Where(c => c.Computed is not null && c.ComputedOn is not null)
                                  .ToList();
        if (cols.Count == 0) return rows;

        // Work on mutable copies; originals never touched
        var mutable = rows
            .Select(r => new Dictionary<string, JsonElement>(r, StringComparer.Ordinal))
            .ToArray();

        foreach (var col in cols)
        {
            var srcKey  = col.ComputedOn!;
            var destKey = col.Key;
            var values  = ExtractDoubles(mutable, srcKey);

            switch (col.Computed)
            {
                case ComputedTransform.DeltaFromPrevious:
                    ApplyDelta(mutable, destKey, values);
                    break;

                case ComputedTransform.PercentChangeFromPrevious:
                    ApplyPercentChange(mutable, destKey, values);
                    break;

                case ComputedTransform.PercentOfTotal:
                    ApplyPercentOfTotal(mutable, destKey, values);
                    break;

                case ComputedTransform.RunningTotal:
                    ApplyRunningTotal(mutable, destKey, values);
                    break;

                case ComputedTransform.MovingAverage3:
                    ApplyMovingAverage(mutable, destKey, values, 3);
                    break;

                case ComputedTransform.MovingAverage7:
                    ApplyMovingAverage(mutable, destKey, values, 7);
                    break;

                case ComputedTransform.Rank:
                    ApplyRank(mutable, destKey, values);
                    break;
            }
        }

        return mutable;
    }

    // --- Transform implementations ---

    private static void ApplyDelta(
        Dictionary<string, JsonElement>[] rows,
        string destKey,
        double?[] values)
    {
        rows[0][destKey] = JsonElementHelpers.NullElement();
        for (var i = 1; i < rows.Length; i++)
        {
            var v = values[i];
            var prev = values[i - 1];
            rows[i][destKey] = v is not null && prev is not null
                ? (v.Value - prev.Value).ToJsonElement()
                : JsonElementHelpers.NullElement();
        }
    }

    private static void ApplyPercentChange(
        Dictionary<string, JsonElement>[] rows,
        string destKey,
        double?[] values)
    {
        rows[0][destKey] = JsonElementHelpers.NullElement();
        for (var i = 1; i < rows.Length; i++)
        {
            var v    = values[i];
            var prev = values[i - 1];
            rows[i][destKey] = v is not null && prev is not null && prev.Value != 0d
                ? (100d * (v.Value - prev.Value) / prev.Value).ToJsonElement()
                : JsonElementHelpers.NullElement();
        }
    }

    private static void ApplyPercentOfTotal(
        Dictionary<string, JsonElement>[] rows,
        string destKey,
        double?[] values)
    {
        var total = values.Sum(v => v ?? 0d);
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i][destKey] = values[i] is not null && total != 0d
                ? (100d * values[i]!.Value / total).ToJsonElement()
                : JsonElementHelpers.NullElement();
        }
    }

    private static void ApplyRunningTotal(
        Dictionary<string, JsonElement>[] rows,
        string destKey,
        double?[] values)
    {
        var running = 0d;
        for (var i = 0; i < rows.Length; i++)
        {
            running += values[i] ?? 0d;
            rows[i][destKey] = running.ToJsonElement();
        }
    }

    private static void ApplyMovingAverage(
        Dictionary<string, JsonElement>[] rows,
        string destKey,
        double?[] values,
        int window)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            if (i < window - 1)
            {
                rows[i][destKey] = JsonElementHelpers.NullElement();
                continue;
            }

            var sum   = 0d;
            var count = 0;
            for (var j = i - window + 1; j <= i; j++)
            {
                if (values[j] is not null)
                {
                    sum += values[j]!.Value;
                    count++;
                }
            }

            rows[i][destKey] = count > 0
                ? (sum / count).ToJsonElement()
                : JsonElementHelpers.NullElement();
        }
    }

    private static void ApplyRank(
        Dictionary<string, JsonElement>[] rows,
        string destKey,
        double?[] values)
    {
        // Rank 1 = highest value; nulls get null rank
        var indexed = values
            .Select((v, i) => (Value: v, Index: i))
            .Where(t => t.Value is not null)
            .OrderByDescending(t => t.Value!.Value)
            .ToList();

        var rankMap = new Dictionary<int, int>(indexed.Count);
        for (var rank = 0; rank < indexed.Count; rank++)
            rankMap[indexed[rank].Index] = rank + 1;

        for (var i = 0; i < rows.Length; i++)
        {
            rows[i][destKey] = rankMap.TryGetValue(i, out var r)
                ? JsonSerializer.SerializeToElement(r)
                : JsonElementHelpers.NullElement();
        }
    }

    private static double?[] ExtractDoubles(
        Dictionary<string, JsonElement>[] rows, string key)
    {
        var result = new double?[rows.Length];
        for (var i = 0; i < rows.Length; i++)
            result[i] = rows[i].TryGetValue(key, out var el) ? el.ToDouble() : null;
        return result;
    }
}
