using FdroidMgr.Api.Abstractions;

using FdroidMgr.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FdroidMgr.Api;

public partial class BootInitializer(
    IServiceScopeFactory scopeFactory,
    BootState state,
    IHostApplicationLifetime lifetime,
    ILogger<BootInitializer> log) : IHostedService
{

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() => _ = RebuildIndexAsync());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RebuildIndexAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            ReprotectLegacySecrets(scope.ServiceProvider);

            var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();
            var repoCfg = await db.RepoConfig.FirstOrDefaultAsync();
            if (repoCfg is null)
            {
                return;
            }

            var initService = scope.ServiceProvider.GetRequiredService<IRepoInitService>();
            if (!initService.IsInitialized())
            {
                return;
            }

            Log.BootIndexRebuild(log);
            var indexService = scope.ServiceProvider.GetRequiredService<IRepoIndexService>();
            await indexService.GenerateAsync(repoCfg);
        }
        catch (Exception ex)
        {
            Log.BootIndexRebuildFailed(log, ex);
        }
        finally
        {
            state.BootCompleted = true;
        }
    }

    private void ReprotectLegacySecrets(IServiceProvider services)
    {
        var db = services.GetRequiredService<FdroidDb>();
        var protector = services.GetRequiredService<IPasswordProtector>();

        var changed = false;
        foreach (var key in db.Keys.ToList())
        {
            if (protector.IsLegacy(key.PasswordEncrypted))
            {
                key.PasswordEncrypted = protector.Protect(protector.Unprotect(key.PasswordEncrypted));
                changed = true;
            }
        }

        var cfg = db.RepoConfig.FirstOrDefault();
        if (cfg is not null && cfg.RepoKeystorePassEnc is not null && protector.IsLegacy(cfg.RepoKeystorePassEnc))
        {
            cfg.RepoKeystorePassEnc = protector.Protect(protector.Unprotect(cfg.RepoKeystorePassEnc));
            changed = true;
        }

        var clientKey = db.FdroidClientKey.FirstOrDefault();
        if (clientKey is not null && protector.IsLegacy(clientKey.SecretCipher))
        {
            clientKey.SecretCipher = protector.Protect(protector.Unprotect(clientKey.SecretCipher));
            changed = true;
        }

        if (changed)
        {
            db.SaveChanges();
            Log.SecretsReprotected(log);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 5100, Level = LogLevel.Information,
            Message = "Boot index rebuild starting")]
        public static partial void BootIndexRebuild(ILogger logger);

        [LoggerMessage(EventId = 5101, Level = LogLevel.Error,
            Message = "Boot index rebuild failed")]
        public static partial void BootIndexRebuildFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 5102, Level = LogLevel.Information,
            Message = "Re-encrypted legacy secrets to v2")]
        public static partial void SecretsReprotected(ILogger logger);
    }
}
