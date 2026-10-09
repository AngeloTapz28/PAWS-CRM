using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.infrastructure.Data;

namespace PAWS.api.Controllers;

// A tiny "is everything alive?" endpoint.
// It checks the API itself and the MASTER database only.
[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly MasterErpDbContext _master;
    private readonly ILogger<HealthController> _logger;

    public HealthController(MasterErpDbContext master, ILogger<HealthController> logger)
    {
        _master = master;
        _logger = logger;
    }

    // GET /api/health
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        bool masterReachable;

        try
        {
            // Give up after 10 seconds so a slow database cannot hang this request.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            // Asks: "can I open a connection to the master database?"
            masterReachable = await _master.Database.CanConnectAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            // Full details go to the private log only, never to the caller.
            _logger.LogWarning(ex, "Health check: the master database check failed.");
            masterReachable = false;
        }

        if (masterReachable)
        {
            return Ok(new
            {
                status = "Healthy",
                api = "Running",
                masterDatabase = "Reachable",
                checkedAtUtc = DateTime.UtcNow
            });
        }

        // 503 = "Service Unavailable". No server or database names are included.
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            status = "Unhealthy",
            api = "Running",
            masterDatabase = "Unreachable",
            checkedAtUtc = DateTime.UtcNow
        });
    }
}
