using System.Diagnostics;

namespace PortableSecureNotepad.Core;

public static class ExecutableReplacement
{
    public const string ReplaceArg = "--replace";
    public const string CleanupArg = "--cleanup";

    private const int ReplaceAttempts = 100;
    private const int ReplaceDelayMs = 100;

    public static bool IsReplacementMode(IReadOnlyList<string> args)
    {
        return args.Count > 0 && string.Equals(args[0], ReplaceArg, StringComparison.Ordinal);
    }

    public static int RunReplacementHelper(IReadOnlyList<string> args)
    {
        try
        {
            ReplacementArguments parsed = ParseReplacementArguments(args);
            WaitForParentExit(parsed.ParentProcessId, TimeSpan.FromSeconds(30));
            MoveWithRetries(parsed.ReplacementPath, parsed.TargetPath);
            StartRelaunchedTarget(parsed.TargetPath, parsed.HelperPath);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    public static IReadOnlyList<string> GetCleanupPaths(IReadOnlyList<string> args)
    {
        var cleanupPaths = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], CleanupArg, StringComparison.Ordinal) && i + 1 < args.Count)
            {
                cleanupPaths.Add(args[i + 1]);
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

    private static ReplacementArguments ParseReplacementArguments(IReadOnlyList<string> args)
    {
        if (!IsReplacementMode(args))
        {
            throw new ArgumentException("Replacement mode was not requested.", nameof(args));
        }

        int parentProcessId = int.Parse(GetRequiredValue(args, "--pid"));
        string targetPath = Path.GetFullPath(GetRequiredValue(args, "--target"));
        string replacementPath = Path.GetFullPath(GetRequiredValue(args, "--replacement"));
        string helperPath = Path.GetFullPath(GetRequiredValue(args, "--helper"));

        return new ReplacementArguments(parentProcessId, targetPath, replacementPath, helperPath);
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
                parent.WaitForExit((int)timeout.TotalMilliseconds);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void MoveWithRetries(string replacementPath, string targetPath)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt < ReplaceAttempts; attempt++)
        {
            try
            {
                File.Move(replacementPath, targetPath, overwrite: true);
                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
            }

            Thread.Sleep(ReplaceDelayMs);
        }

        throw new IOException("Could not replace the executable.", lastException);
    }

    private static void StartRelaunchedTarget(string targetPath, string helperPath)
    {
        var startInfo = new ProcessStartInfo(targetPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory
        };

        startInfo.ArgumentList.Add(CleanupArg);
        startInfo.ArgumentList.Add(helperPath);

        Process.Start(startInfo);
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

    private sealed record ReplacementArguments(
        int ParentProcessId,
        string TargetPath,
        string ReplacementPath,
        string HelperPath);
}
