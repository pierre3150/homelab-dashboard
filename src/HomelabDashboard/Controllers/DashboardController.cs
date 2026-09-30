using HomelabDashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace HomelabDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IMorningReportService _morningReportService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IDashboardService dashboardService,
        IMorningReportService morningReportService,
        ILogger<DashboardController> logger)
    {
        _dashboardService = dashboardService;
        _morningReportService = morningReportService;
        _logger = logger;
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot(CancellationToken ct)
    {
        try
        {
            var snapshot = await _dashboardService.GetSnapshotAsync(ct);
            return Ok(snapshot);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Proxmox API");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Impossible de contacter l'API Proxmox. Vérifie l'hôte, le token et que le node est joignable."
            });
        }
    }

    /// <summary>
    /// The morning report: disk usage (per node and per physical disk), any
    /// errors seen over the window, and average CPU/RAM per guest.
    /// windowHours lets the frontend ask for something other than the last
    /// 24h (e.g. "since Friday" over a weekend) without changing the default.
    /// </summary>
    [HttpGet("morning-report")]
    public async Task<IActionResult> GetMorningReport([FromQuery] double? windowHours, CancellationToken ct)
    {
        try
        {
            var window = windowHours is > 0 ? TimeSpan.FromHours(windowHours.Value) : (TimeSpan?)null;
            var report = await _morningReportService.GenerateAsync(window, ct);
            return Ok(report);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Proxmox API while building morning report");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Impossible de contacter l'API Proxmox. Vérifie l'hôte, le token et que le node est joignable."
            });
        }
    }
}
