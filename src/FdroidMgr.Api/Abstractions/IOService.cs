using System.Text;

namespace FdroidMgr.Api.Abstractions;

public interface IOService
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path, bool recursive);

    void MoveFile(string source, string destination, bool overwrite = false);

    void CopyFile(string source, string destination, bool overwrite = false);

    Task<string> ReadAllTextAsync(string path, CancellationToken ct = default);

    string ReadAllText(string path);

    string[] ReadAllLines(string path);

    IEnumerable<string> ReadLines(string path);

    Task WriteAllTextAsync(string path, string content, CancellationToken ct = default);

    Task WriteAllTextAsync(string path, string content, Encoding encoding, CancellationToken ct = default);

    void WriteAllText(string path, string content);

    void WriteAllLines(string path, IEnumerable<string> lines);

    Stream OpenRead(string path);

    Stream CreateFile(string path);

    void SetUnixFileMode(string path, UnixFileMode mode);

    DateTime GetLastWriteTimeUtc(string path);

    long GetLength(string path);

    IEnumerable<string> EnumerateFiles(string path, string searchPattern);

    IEnumerable<string> EnumerateFilesRecursive(string path);

    IEnumerable<string> EnumerateFileSystemEntries(string path);
}
