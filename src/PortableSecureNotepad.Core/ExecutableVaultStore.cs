using System.Diagnostics;

namespace PortableSecureNotepad.Core;

public sealed class ExecutableVaultStore
{
    private const string TempMarker = ".psn-";

    public ExecutableVaultStore(string executablePath)
    {
        ExecutablePath = Path.GetFullPath(executablePath);
    }

    public string ExecutablePath { get; }

    public byte[]? ReadPayload()
    {
        return EmbeddedPayload.ReadPayload(ExecutablePath);
    }

    public ReplacementRequest PrepareCommit(byte[] encryptedPayload)
    {
        ArgumentNullException.ThrowIfNull(encryptedPayload);

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
            HelperPath: helperPath);
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

        Process? process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Could not start replacement helper.");
        }
    }

    public void CleanupStaleTemporaryFiles(TimeSpan minimumAge)
    {
        string directory = Path.GetDirectoryName(ExecutablePath)
            ?? throw new InvalidOperationException("Executable path has no directory.");
        string stem = Path.GetFileNameWithoutExtension(ExecutablePath);
        DateTimeOffset oldestAllowed = DateTimeOffset.UtcNow.Subtract(minimumAge);

        foreach (string path in Directory.EnumerateFiles(directory, $"{stem}{TempMarker}*.exe"))
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
    string HelperPath);
