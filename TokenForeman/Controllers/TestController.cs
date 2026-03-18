using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TokenForeman.Controllers;

/// <summary>
/// DEMO-ONLY: Test endpoints for judges/demo. Not for production use.
/// Permission boundary: these endpoints return static metadata only; they do not call external APIs or use tokens.
/// </summary>
[ApiController]
[Route("api/test")]
[AllowAnonymous]
public sealed class TestController : ControllerBase
{
    /// <summary>DEMO-ONLY: Simulates a Google integration check. Returns static JSON; does not call Google.</summary>
    [HttpGet("google")]
    [ProducesResponseType(typeof(TestEndpointResponse), StatusCodes.Status200OK)]
    public IActionResult GetGoogle()
    {
        return Ok(new TestEndpointResponse("google", true, "Demo-only: Google integration is configured via Token Vault."));
    }

    /// <summary>DEMO-ONLY: Simulates a Procore integration check. Returns static JSON; does not call Procore.</summary>
    [HttpGet("procore")]
    [ProducesResponseType(typeof(TestEndpointResponse), StatusCodes.Status200OK)]
    public IActionResult GetProcore()
    {
        return Ok(new TestEndpointResponse("procore", true, "Demo-only: Procore integration is configured via Token Vault."));
    }
}

public sealed record TestEndpointResponse(string Integration, bool DemoOnly, string Message);
