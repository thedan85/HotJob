using Microsoft.AspNetCore.Mvc;
using hotjob.Controllers.Admin;

namespace hotjob.Controllers.Admin;

[ApiController]
public class AdminStatusController : AdminControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok("Admin access granted.");
}
