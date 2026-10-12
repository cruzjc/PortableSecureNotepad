using System.Diagnostics;

namespace PortableSecureNotepad.Core;

public static class ExecutableReplacement
{
    public const string ReplaceArg = "--replace";
    public const string CleanupArg = "--cleanup";
    public const string CommitCompleteArg = "--commit-complete";

    private const int ReplaceAttempts = 100;
    private const int ReplaceDelayMs = 100;

    public static bool IsReplacementMode(IReadOnlyList<string> args)
    {
        return args.Count > 0 && string.Equals(args[0], ReplaceArg, StringComparison.Ordinal);
    }

    public static int RunReplacementHelper(IReadOnlyList<string> args)
    {
        ReplacementRequest parsed = ParseReplacementArguments(args);
        WaitForParentExit(parsed.ParentProcessId, TimeSpan.FromSeconds(30));
        string savedHash;
        using (var instanceLock = VaultInstanceLock.TryAcquire(parsed.TargetPath, TimeSpan.FromSeconds(10))
            ?? throw new IOException("Another instance is using this vault. The pending EXE has been preserved."))
        {
            savedHash = CompleteReplacement(parsed);
        }
        // The relaunched UI acquires the same lock before reading the vault.
        StartRelaunchedTarget(parsed.TargetPath, parsed.HelperPath, savedHash);
        return 0;
    }

    public static IReadOnlyList<string> GetCleanupPaths(IReadOnlyList<string> args, string executablePath)
    {
        var cleanupPaths = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], CleanupArg, StringComparison.Ordinal) && i + 1 < args.Count)
            {
                string path = Path.GetFullPath(args[i + 1]);
                string directory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!;
                string prefix = Path.GetFileNameWithoutExtension(executablePath) + ".psn-";
                if (string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".helper.exe", StringComparison.OrdinalIgnoreCase))
                {
                    cleanupPaths.Add(path);
                }
                i++;
            }
        }

        return cleanupPaths;
    }

    public static void DeleteWithRetriesInBackground(IEnumerable<string> paths)
    {
        string[] pathArray = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (pathArray.Length == 0)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            foreach (string path in pathArray)
            {
                DeleteWithRetries(path);
            }
        });
    }

    public static string? GetCommitCompletionHash(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == CommitCompleteArg) return args[i + 1];
        }
        return null;
    }

    private static ReplacementRequest ParseReplacementArguments(IReadOnlyList<string> args)
    {
        if (!IsReplacementMode(args))
        {
            throw new ArgumentException("Replacement mode was not requested.", nameof(args));
        }

        int parentProcessId = int.Parse(GetRequiredValue(args, "--pid"));
        string targetPath = Path.GetFullPath(GetRequiredValue(args, "--target"));
        string replacementPath = Path.GetFullPath(GetRequiredValue(args, "--replacement"));
        string helperPath = Path.GetFullPath(GetRequiredValue(args, "--helper"));
        string expectedHash = GetRequiredValue(args, "--expected-hash");

        return new ReplacementRequest(parentProcessId, targetPath, replacementPath, helperPath, expectedHash);
    }

    private static string GetRequiredValue(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        throw new ArgumentException($"Missing required argument {name}.");
    }

    private static void WaitForParentExit(int parentProcessId, TimeSpan timeout)
    {
        try
        {
            using Process parent = Process.GetProcessById(parentProcessId);
            if (!parent.HasExited)
            {
                if (!parent.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    throw new IOException("The app did not exit in time. The pending EXE has been preserved.");
                }
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static string CompleteReplacement(ReplacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (EmbeddedPayload.ReadPayload(request.ReplacementPath) is not { Length: > 0 })
        {
            throw new InvalidDataException("The replacement EXE has no embedded vault.");
        }
        string replacementHash = ExecutableVaultStore.GetFileHash(request.ReplacementPath);
        Exception? lastException = null;

        for (int attempt = 0; attempt < ReplaceAttempts; attempt++)
        {
            try
            {
                if (ExecutableVaultStore.GetFileHash(request.TargetPath) != request.ExpectedTargetHash)
                {
                    throw new InvalidDataException("The target EXE changed before replacement. The pending EXE has been preserved.");
                }
                File.Replace(request.ReplacementPath, request.TargetPath, null);
                if (ExecutableVaultStore.GetFileHash(request.TargetPath) != replacementHash)
                {
                    throw new InvalidDataException("The saved EXE failed verification. Preserve this EXE and restore a manually exported backup if available.");
                }
                return replacementHash;
            }
            catch (IOException ex) when (File.Exists(request.ReplacementPath))
            {
                lastException = ex;
            }
            catch (UnauthorizedAccessException ex) when (File.Exists(request.ReplacementPath))
            {
                lastException = ex;
            }

            Thread.Sleep(ReplaceDelayMs);
        }

        throw new IOException("Could not replace the executable.", lastException);
    }

    private static void StartRelaunchedTarget(string targetPath, string helperPath, string savedHash)
    {
        var startInfo = new ProcessStartInfo(targetPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory
        };

        startInfo.ArgumentList.Add(CleanupArg);
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add(CommitCompleteArg);
        startInfo.ArgumentList.Add(savedHash);

        using Process process = Process.Start(startInfo)
            ?? throw new IOException("The vault was saved, but relaunch failed. Open the target EXE manually.");
    }

    private static void DeleteWithRetries(string path)
    {
        for (int attempt = 0; attempt < ReplaceAttempts; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            Thread.Sleep(ReplaceDelayMs);
        }
    }

}
