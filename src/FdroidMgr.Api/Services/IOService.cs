using System.Text;
using FdroidMgr.Api.Abstractions;

namespace FdroidMgr.Api.Services;

public sealed class RealIoService : IOService
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void MoveFile(string source, string destination, bool overwrite = false)
        => File.Move(source, destination, overwrite);

    public void CopyFile(string source, string destination, bool overwrite = false)
        => File.Copy(source, destination, overwrite);

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default)
        => File.ReadAllTextAsync(path, ct);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public string[] ReadAllLines(string path) => File.ReadAllLines(path);

    public IEnumerable<string> ReadLines(string path) => File.ReadLines(path);

    public Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
        => File.WriteAllTextAsync(path, content, ct);

    public Task WriteAllTextAsync(string path, string content, Encoding encoding, CancellationToken ct = default)
        => File.WriteAllTextAsync(path, content, encoding, ct);

    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

    public void WriteAllLines(string path, IEnumerable<string> lines) => File.WriteAllLines(path, lines);

    public Stream OpenRead(string path) => File.OpenRead(path);

    public Stream CreateFile(string path) => File.Create(path);

    public void SetUnixFileMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        => Directory.EnumerateFiles(path, searchPattern);

    public IEnumerable<string> EnumerateFilesRecursive(string path)
        => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);

    public long GetLength(string path) => new FileInfo(path).Length;

    public IEnumerable<string> EnumerateFileSystemEntries(string path)
        => Directory.EnumerateFileSystemEntries(path);
}
