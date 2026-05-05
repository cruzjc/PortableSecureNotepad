using PortableSecureNotepad.Core;
using PortableSecureNotepad.Ui;
using System.Diagnostics;

namespace PortableSecureNotepad;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (ExecutableReplacement.IsReplacementMode(args))
        {
            return ExecutableReplacement.RunReplacementHelper(args);
        }

        ExecutableReplacement.DeleteWithRetriesInBackground(ExecutableReplacement.GetCleanupPaths(args));

        ApplicationConfiguration.Initialize();

        string executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not determine executable path.");

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

        Application.Run(new MainForm(store, document, password));
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
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            {
                MessageBox.Show(
                    ex.Message,
                    "Vault error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
