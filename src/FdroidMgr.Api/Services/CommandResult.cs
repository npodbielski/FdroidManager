namespace FdroidMgr.Api.Services;

public record CommandResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}
