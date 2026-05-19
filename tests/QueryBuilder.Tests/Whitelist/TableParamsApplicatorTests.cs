using System.Text.Json;
using ReportingPlatform.Contracts.TableParams;
using ReportingPlatform.QueryBuilder.Builder;
using ReportingPlatform.QueryBuilder.Whitelist;
using SqlKata;
using Xunit;

namespace ReportingPlatform.QueryBuilder.Tests.Whitelist;

public sealed class TableParamsApplicatorTests
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static QueryableSource MakeSource(
        IReadOnlyList<string>? allowed = null,
        IReadOnlyList<string>? sortable = null,
        int maxRows = 1000) => new()
    {
        TenantId        = "t1",
        SourceName      = "orders",
        SchemaName      = "public",
        TableName       = "orders",
        AllowedColumns  = allowed  ?? [],
        SortableColumns = sortable ?? [],
        MaxRows         = maxRows,
    };

    private static TablePaginationParams Pagination(
        int page = 1, int pageSize = 25,
        IReadOnlyList<SortSpec>? sort = null,
        IReadOnlyList<FilterSpec>? filters = null) => new()
    {
        Page     = page,
        PageSize = pageSize,
        Sort     = sort,
        Filters  = filters,
    };

    private static FilterSpec Filter(string key, string op, JsonElement value) => new()
    {
        Key   = key,
        Op    = op,
        Value = value,
    };

    // ------------------------------------------------------------------
    // T1: Unknown source (SOURCE_NOT_FOUND is raised by SqlKataQueryBuilder,
    //     but we test the column whitelist directly here)
    // ------------------------------------------------------------------

    [Fact]
    public void T1_ApplyFiltersOnly_RejectsDisallowedColumn()
    {
        var src = MakeSource(allowed: ["id", "name"]);
        var t   = Pagination(filters:
        [
            Filter("secret_col", "=", JsonSerializer.SerializeToElement("x")),
        ]);
        var q   = new Query("orders");

        var ex = Assert.Throws<AdapterException>(() =>
            TableParamsApplicator.ApplyFiltersOnly(q, t, src));

        Assert.Equal("COLUMN_NOT_ALLOWED", ex.ErrorCode);
        Assert.Contains("secret_col", ex.Detail ?? ex.Message);
    }

    [Fact]
    public void T2_Apply_RejectsDisallowedSortColumn()
    {
        var src = MakeSource(
            allowed:  ["id", "name"],
            sortable: ["id"]);           // only "id" is sortable
        var t   = Pagination(sort: [new SortSpec { Key = "name", Direction = "asc" }]);
        var q   = new Query("orders");

        var ex = Assert.Throws<AdapterException>(() =>
            TableParamsApplicator.Apply(q, t, src));

        Assert.Equal("SORT_NOT_ALLOWED", ex.ErrorCode);
    }

    [Fact]
    public void T3_Apply_AllowsAllColumnsWhenAllowedColumnsEmpty()
    {
        // Empty AllowedColumns = all columns allowed
        var src = MakeSource(allowed: []);
        var t   = Pagination(
            filters:
            [
                Filter("any_col", "=", JsonSerializer.SerializeToElement("v")),
            ]);
        var q = new Query("orders");

        // Should not throw
        var ex = Record.Exception(() => TableParamsApplicator.Apply(q, t, src));
        Assert.Null(ex);
    }

    [Fact]
    public void T4_Apply_RejectsUnknownFilterOp()
    {
        var src = MakeSource();
        var t   = Pagination(filters:
        [
            Filter("id", "BETWEEN", JsonSerializer.SerializeToElement(42)),
        ]);
        var q = new Query("orders");

        var ex = Assert.Throws<AdapterException>(() =>
            TableParamsApplicator.Apply(q, t, src));

        Assert.Equal("UNKNOWN_FILTER_OP", ex.ErrorCode);
    }

    [Fact]
    public void T5_Apply_CapsPageSizeAtMaxRows()
    {
        // MaxRows = 10; PageSize = 1000 → effective limit = 10
        var src = MakeSource(maxRows: 10);
        var t   = Pagination(page: 1, pageSize: 1000);
        var q   = new Query("orders");

        TableParamsApplicator.Apply(q, t, src);

        // SqlKata parameterizes LIMIT — check the binding value, not the literal SQL
        var compiler = new SqlKata.Compilers.PostgresCompiler();
        var compiled = compiler.Compile(q);

        Assert.Contains("LIMIT", compiled.Sql, StringComparison.OrdinalIgnoreCase);
        // The effective limit must be 10 (min of PageSize=1000, MaxRows=10)
        Assert.Contains(compiled.NamedBindings, kv => kv.Value is 10);
    }
}
