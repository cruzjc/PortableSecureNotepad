using PortableSecureNotepad.Core;
using PortableSecureNotepad.Ui;
using System.Diagnostics;

namespace PortableSecureNotepad;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            return Run(args);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            string recoveryDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
            MessageBox.Show(
                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Any pending recovery EXEs are preserved in:{Environment.NewLine}{recoveryDirectory}{Environment.NewLine}Copy a pending EXE to a new filename and open it with your password. Keep the original files until recovery is verified.",
                "Vault operation failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (ExecutableReplacement.IsReplacementMode(args))
        {
            return ExecutableReplacement.RunReplacementHelper(args);
        }

        string executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not determine executable path.");

        // Framework-dependent launch would identify dotnet.exe as the vault target.
#pragma warning disable IL3000 // Empty Location is the intentional single-file launch check.
        if (!string.IsNullOrEmpty(typeof(Program).Assembly.Location))
#pragma warning restore IL3000
        {
            MessageBox.Show("Run the single-file EXE produced by scripts\\publish.ps1 to use the portable vault.",
                "Published EXE required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        using var instanceLock = VaultInstanceLock.TryAcquire(executablePath, TimeSpan.Zero);
        if (instanceLock is null)
        {
            MessageBox.Show("This portable notepad is already open. Use the existing window to avoid conflicting commits.",
                "Vault already open", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        string? completionHash = ExecutableReplacement.GetCommitCompletionHash(args);
        if (completionHash is not null && ExecutableVaultStore.GetFileHash(executablePath) != completionHash)
        {
            throw new InvalidDataException("The restarted EXE does not match the committed vault. Preserve it and restore a manually exported backup if available.");
        }
        ExecutableReplacement.DeleteWithRetriesInBackground(ExecutableReplacement.GetCleanupPaths(args, executablePath));

        var store = new ExecutableVaultStore(executablePath);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                store.CleanupStaleTemporaryFiles(TimeSpan.FromHours(1));
            }
            catch
            {
            }
        });

        byte[]? payload = store.ReadPayload();
        IReadOnlyList<string> recoveryPaths = store.GetPendingRecoveryPaths();
        if (recoveryPaths.Count > 0)
        {
            MessageBox.Show(
                $"An unfinished commit was found. Its pending EXE has been preserved:{Environment.NewLine}{recoveryPaths[0]}{Environment.NewLine}{Environment.NewLine}Copy it to a new filename and open it with your password to recover entries. Keep all pending and backup EXEs until recovery is verified.",
                "Recovery copy available", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        VaultDocument document;
        string password;

        if (payload is null)
        {
            using var setupForm = new PasswordSetupForm();
            if (setupForm.ShowDialog() != DialogResult.OK)
            {
                return 0;
            }

            password = setupForm.Password;
            document = new VaultDocument();
        }
        else
        {
            if (!TryUnlock(payload, out document, out password))
            {
                return 0;
            }
        }

        if (completionHash is not null)
        {
            MessageBox.Show("The encrypted vault was saved and verified in this EXE.",
                "Commit saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        using var session = new VaultSession(document, password);
        // The session owns the unlocked state and can release it during manual locking.
        document = null!;
        password = string.Empty;
        Application.Run(new MainForm(store, session));
        return 0;
    }

    private static bool TryUnlock(byte[] payload, out VaultDocument document, out string password)
    {
        while (true)
        {
            using var unlockForm = new UnlockForm();
            if (unlockForm.ShowDialog() != DialogResult.OK)
            {
                document = new VaultDocument();
                password = string.Empty;
                return false;
            }

            try
            {
                password = unlockForm.Password;
                document = VaultCrypto.Decrypt(payload, password);
                return true;
            }
            catch (VaultUnlockException)
            {
                MessageBox.Show(
                    "Password/PIN did not unlock this vault.",
                    "Unlock failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Vault error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            // Structural corruption propagates to the recovery dialog in Main.
        }
    }
}
