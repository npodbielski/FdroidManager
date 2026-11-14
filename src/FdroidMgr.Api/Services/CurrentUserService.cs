using System.Security.Claims;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    public string GetSubject(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? user.FindFirstValue("sub")
           ?? "";

    public string GetUsername(ClaimsPrincipal user) => user.Identity?.Name ?? "";
}
