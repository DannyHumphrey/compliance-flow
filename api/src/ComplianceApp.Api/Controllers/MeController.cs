using ComplianceApp.Application.Common.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ComplianceApp.Api.Controllers;

/// <summary>
/// Round-trip diagnostic endpoint: confirms the bearer token was accepted and
/// that the tenant claim resolved through to <see cref="ICurrentUserService"/>.
/// Used by the integration test in T6 to prove auth + tenancy plumbing.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;

    public MeController(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    [HttpGet]
    public ActionResult<MeResponse> Get()
    {
        return Ok(new MeResponse(
            _currentUser.UserId,
            _currentUser.OrganisationId,
            _currentUser.IsAuthenticated));
    }
}

public record MeResponse(Guid? UserId, Guid? OrganisationId, bool IsAuthenticated);
