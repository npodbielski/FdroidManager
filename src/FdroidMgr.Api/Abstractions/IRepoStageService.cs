using FdroidMgr.Api.Models;

namespace FdroidMgr.Api.Abstractions;

public interface IRepoStageService
{
    string? ResolvePublicDir();

    void Stage(RepoConfig cfg);
}
