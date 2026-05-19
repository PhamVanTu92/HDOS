using ReportingPlatform.Contracts.TableParams;

namespace ReportingPlatform.QueryBuilder.Builder;

internal static class TableParamsApplicator
{
    // Applies only filter constraints — used by the count query which needs no sort/limit.
    internal static void ApplyFiltersOnly(Query q, TablePaginationParams t, QueryableSource src)
    {
        foreach (var f in t.Filters ?? [])
        {
            ValidateFilterColumn(f.Key, src);
            ApplyFilter(q, f);
        }
    }

    internal static void Apply(Query q, TablePaginationParams t, QueryableSource src)
    {
        // Max rows cap — whitelist constraint
        var limit = Math.Min(t.PageSize, src.MaxRows);
        var offset = (t.Page - 1) * limit;

        q.Limit(limit).Offset(offset);

        if (t.Sort is not null)
        {
            foreach (var s in t.Sort)
            {
                ValidateSortColumn(s.Key, src);
                if (s.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase))
                    q.OrderByDesc(s.Key);
                else
                    q.OrderBy(s.Key);
            }
        }

        foreach (var f in t.Filters ?? [])
        {
            ValidateFilterColumn(f.Key, src);
            ApplyFilter(q, f);
        }
    }

    private static void ValidateSortColumn(string column, QueryableSource src)
    {
        var sortable = src.SortableColumns.Count > 0 ? src.SortableColumns : src.AllowedColumns;
        if (sortable.Count > 0 && !sortable.Contains(column, StringComparer.OrdinalIgnoreCase))
            throw new AdapterException("SORT_NOT_ALLOWED", column);
    }

    private static void ValidateFilterColumn(string column, QueryableSource src)
    {
        if (src.AllowedColumns.Count > 0 &&
            !src.AllowedColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            throw new AdapterException("COLUMN_NOT_ALLOWED", column);
    }

    private static void ApplyFilter(Query q, FilterSpec f)
    {
        switch (f.Op)
        {
            case "=":        q.Where(f.Key, GetScalar(f.Value)); break;
            case "!=":       q.WhereNot(f.Key, GetScalar(f.Value)); break;
            case ">":        q.Where(f.Key, ">",  GetScalar(f.Value)); break;
            case ">=":       q.Where(f.Key, ">=", GetScalar(f.Value)); break;
            case "<":        q.Where(f.Key, "<",  GetScalar(f.Value)); break;
            case "<=":       q.Where(f.Key, "<=", GetScalar(f.Value)); break;
            case "contains": q.WhereLike(f.Key, $"%{GetString(f.Value)}%"); break;
            case "in":       q.WhereIn(f.Key, GetArray(f.Value)); break;
            default:
                throw new AdapterException("UNKNOWN_FILTER_OP", f.Op);
        }
    }

    // --- JsonElement value extraction helpers ---

    internal static object? GetScalar(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var i) ? (object)i : el.GetDouble(),
        JsonValueKind.True   => true,
        JsonValueKind.False  => false,
        JsonValueKind.Null   => null,
        _ => throw new AdapterException("UNSUPPORTED_VALUE_KIND", el.ValueKind.ToString())
    };

    private static string GetString(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.String)
            throw new AdapterException("EXPECTED_STRING_VALUE", el.ValueKind.ToString());
        return el.GetString()!;
    }

    private static object[] GetArray(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            throw new AdapterException("EXPECTED_ARRAY_VALUE", el.ValueKind.ToString());

        return [.. el.EnumerateArray().Select(e => GetScalar(e) ?? DBNull.Value)];
    }
}
