
namespace FdroidMgr.Api.Models;

public class RepoAlreadyInitializedException(string message, string backupPath) : Exception(message)
{
    public string BackupPath { get; } = backupPath;
}
