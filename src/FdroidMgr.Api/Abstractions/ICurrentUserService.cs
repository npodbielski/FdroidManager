using System.Security.Claims;

namespace FdroidMgr.Api.Abstractions;

public interface ICurrentUserService
{
    string GetSubject(ClaimsPrincipal user);

    string GetUsername(ClaimsPrincipal user);
}
