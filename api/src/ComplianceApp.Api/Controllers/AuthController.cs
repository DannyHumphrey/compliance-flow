using ComplianceApp.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplianceApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IDevTokenIssuer? _devTokenIssuer;

    public AuthController(IServiceProvider serviceProvider)
    {
        // Resolved lazily so the controller still loads when DevAuth is disabled
        // — it just returns 404 from /dev-token in that case.
        _devTokenIssuer = serviceProvider.GetService(typeof(IDevTokenIssuer)) as IDevTokenIssuer;
    }

    /// <summary>
    /// Issues a short-lived dev JWT for the given user + organisation.
    /// Only available when DevAuth.Enabled is true (i.e. local Development).
    /// Production swaps this out for a real Cognito-issued token.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("dev-token")]
    public ActionResult<DevTokenResponse> CreateDevToken([FromBody] DevTokenRequest request)
    {
        if (_devTokenIssuer is null)
        {
            return NotFound();
        }

        var token = _devTokenIssuer.Issue(request.UserId, request.OrganisationId);
        return Ok(new DevTokenResponse(token.AccessToken, token.ExpiresAt));
    }
}

public record DevTokenRequest(Guid UserId, Guid OrganisationId);

public record DevTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);
