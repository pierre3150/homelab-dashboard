using HomelabDashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace HomelabDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(IDashboardService dashboardService, ILogger<DashboardController> logger)
    {
        _dashboardService = dashboardService;
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
}
