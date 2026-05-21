using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using ReportingPlatform.ExcelProvider.Services;

namespace ReportingPlatform.ExcelProvider.Management;

// ─── Request bodies ────────────────────────────────────────────────────────────

public sealed class UpdateStockBody
{
    [JsonPropertyName("stock")]
    public int Stock { get; set; }
}

// ─── Controller ───────────────────────────────────────────────────────────────

/// <summary>
/// HTTP management API for reading and mutating Excel data at runtime.
/// After each successful mutation, a WidgetStale notification is sent to the
/// Ingestion API via <see cref="NotificationService"/>.
/// </summary>
[ApiController]
[Route("api/data")]
public sealed class DataController : ControllerBase
{
    private readonly DataManagementService _mgmt;
    private readonly NotificationService   _notify;
    private readonly ILogger<DataController> _logger;

    public DataController(
        DataManagementService     mgmt,
        NotificationService       notify,
        ILogger<DataController>   logger)
    {
        _mgmt   = mgmt;
        _notify = notify;
        _logger = logger;
    }

    // ─── Sales ─────────────────────────────────────────────────────────────────

    /// <summary>GET /api/data/sales?date=2024-01-15&amp;region=North</summary>
    [HttpGet("sales")]
    public async Task<IActionResult> GetSales(
        [FromQuery] string? date,
        [FromQuery] string? region,
        CancellationToken   ct)
    {
        try
        {
            var rows = await _mgmt.GetSalesAsync(date, region, ct);
            return Ok(rows);
        }
        catch (OperationCanceledException) { return StatusCode(499); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSales failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>POST /api/data/sales</summary>
    [HttpPost("sales")]
    public async Task<IActionResult> AddSale(
        [FromBody] CreateSaleRequest req,
        CancellationToken            ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var record = await _mgmt.AddSaleAsync(req, ct);
            _ = _notify.NotifyDataChangedAsync(DataManagementService.SalesOperations, CancellationToken.None);
            return CreatedAtAction(nameof(GetSales), new { }, record);
        }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddSale failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>PUT /api/data/sales/{rowIndex}</summary>
    [HttpPut("sales/{rowIndex:int}")]
    public async Task<IActionResult> UpdateSale(
        int                        rowIndex,
        [FromBody] UpdateSaleRequest req,
        CancellationToken           ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var record = await _mgmt.UpdateSaleAsync(rowIndex, req, ct);
            _ = _notify.NotifyDataChangedAsync(DataManagementService.SalesOperations, CancellationToken.None);
            return Ok(record);
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "physRow")
        {
            return NotFound(new { error = $"Row {rowIndex} not found." });
        }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateSale rowIndex={RowIndex} failed", rowIndex);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>DELETE /api/data/sales/{rowIndex}</summary>
    [HttpDelete("sales/{rowIndex:int}")]
    public async Task<IActionResult> DeleteSale(int rowIndex, CancellationToken ct)
    {
        try
        {
            await _mgmt.DeleteSaleAsync(rowIndex, ct);
            _ = _notify.NotifyDataChangedAsync(DataManagementService.SalesOperations, CancellationToken.None);
            return NoContent();
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "physRow")
        {
            return NotFound(new { error = $"Row {rowIndex} not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteSale rowIndex={RowIndex} failed", rowIndex);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ─── Products ──────────────────────────────────────────────────────────────

    /// <summary>GET /api/data/products</summary>
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(CancellationToken ct)
    {
        try
        {
            var rows = await _mgmt.GetProductsAsync(ct);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetProducts failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>PUT /api/data/products/{productId}/stock — Body: { "stock": 150 }</summary>
    [HttpPut("products/{productId}/stock")]
    public async Task<IActionResult> UpdateProductStock(
        string              productId,
        [FromBody] UpdateStockBody body,
        CancellationToken   ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var record = await _mgmt.UpdateProductStockAsync(productId, body.Stock, ct);
            _ = _notify.NotifyDataChangedAsync(DataManagementService.InventoryOperations, CancellationToken.None);
            return Ok(record);
        }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex)        { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateProductStock productId={ProductId} failed", productId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
