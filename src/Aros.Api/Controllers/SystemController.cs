using Microsoft.AspNetCore.Mvc;

namespace Aros.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new
        {
            message = "pong",
            platform = Environment.OSVersion.Platform.ToString(),
            time = DateTime.UtcNow
        });
    }
}
