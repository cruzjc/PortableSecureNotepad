using System.Diagnostics;
using System.Security.Cryptography;

namespace PortableSecureNotepad.Core;

public sealed class ExecutableVaultStore
{
    private const string TempMarker = ".psn-";
    private readonly string _expectedTargetHash;

    public ExecutableVaultStore(string executablePath)
    {
        ExecutablePath = Path.GetFullPath(executablePath);
        _expectedTargetHash = GetFileHash(ExecutablePath);
    }

    public string ExecutablePath { get; }

    public byte[]? ReadPayload()
    {
        return EmbeddedPayload.ReadPayload(ExecutablePath);
    }

    public ReplacementRequest PrepareCommit(byte[] encryptedPayload)
    {
        ArgumentNullException.ThrowIfNull(encryptedPayload);
        if (!string.Equals(GetFileHash(ExecutablePath), _expectedTargetHash, StringComparison.Ordinal))
        {
            throw new IOException("This EXE has changed since it was opened. Reopen it before committing; your pending entries are still in this window.");
        }

        string directory = Path.GetDirectoryName(ExecutablePath)
            ?? throw new InvalidOperationException("Executable path has no directory.");
        string stem = Path.GetFileNameWithoutExtension(ExecutablePath);
        string id = Guid.NewGuid().ToString("N");
        string pendingPath = Path.Combine(directory, $"{stem}{TempMarker}{id}.pending.exe");
        string helperPath = Path.Combine(directory, $"{stem}{TempMarker}{id}.helper.exe");

        EmbeddedPayload.WriteExecutableWithPayload(ExecutablePath, pendingPath, encryptedPayload);
        EmbeddedPayload.WriteExecutableWithoutPayload(ExecutablePath, helperPath);

        return new ReplacementRequest(
            ParentProcessId: Environment.ProcessId,
            TargetPath: ExecutablePath,
            ReplacementPath: pendingPath,
            HelperPath: helperPath,
            ExpectedTargetHash: _expectedTargetHash);
    }

    public void StartReplacementHelper(ReplacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo(request.HelperPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(request.TargetPath) ?? Environment.CurrentDirectory
        };

        startInfo.ArgumentList.Add(ExecutableReplacement.ReplaceArg);
        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(request.ParentProcessId.ToString());
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(request.TargetPath);
        startInfo.ArgumentList.Add("--replacement");
        startInfo.ArgumentList.Add(request.ReplacementPath);
        startInfo.ArgumentList.Add("--helper");
        startInfo.ArgumentList.Add(request.HelperPath);
        startInfo.ArgumentList.Add("--expected-hash");
        startInfo.ArgumentList.Add(request.ExpectedTargetHash);

        Process? process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Could not start replacement helper.");
        }
    }

    public static string GetFileHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public IReadOnlyList<string> GetPendingRecoveryPaths()
    {
        string directory = Path.GetDirectoryName(ExecutablePath)!;
        string stem = Path.GetFileNameWithoutExtension(ExecutablePath);
        return Directory.GetFiles(directory, $"{stem}{TempMarker}*.pending.exe")
            .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
    }

    public void CleanupStaleTemporaryFiles(TimeSpan minimumAge)
    {
        string directory = Path.GetDirectoryName(ExecutablePath)
            ?? throw new InvalidOperationException("Executable path has no directory.");
        string stem = Path.GetFileNameWithoutExtension(ExecutablePath);
        DateTimeOffset oldestAllowed = DateTimeOffset.UtcNow.Subtract(minimumAge);

        // Pending vaults and backups are recovery data, never disposable temporary files.
        foreach (string path in Directory.EnumerateFiles(directory, $"{stem}{TempMarker}*.helper.exe"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) <= oldestAllowed.UtcDateTime)
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

public sealed record ReplacementRequest(
    int ParentProcessId,
    string TargetPath,
    string ReplacementPath,
    string HelperPath,
    string ExpectedTargetHash);
