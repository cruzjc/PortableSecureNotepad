# Portable Secure Notepad

Portable Secure Notepad is a Windows-only notepad/sticky-note app published as a single self-contained EXE. Notes are encrypted and appended to the EXE itself, so copying the EXE copies the saved notes with it.

## Build

Install the .NET 10 SDK (the runtime alone cannot build this repository).

```powershell
dotnet restore
dotnet test
.\scripts\publish.ps1
```

The portable artifact is:

```text
publish\PortableSecureNotepad.exe
```

The publish script builds from a temporary copy of `src` before copying the final EXE back to `publish`. This avoids Windows apphost file-lock issues that can happen when publishing directly inside synced folders.

After publishing, run `.\scripts\test-published-replacement.ps1` to exercise the actual published helper against generated encrypted fixtures, including backup, relaunch, lock release, cleanup, overwrite refusal, native build failure, and explicit publish replacement. The relaunched fixture is a console app; this check does not cover interactive WinForms dialogs. Failed fixtures are retained under the printed temporary smoke directory for diagnosis.

Publishing refuses to overwrite any existing output EXE because it may contain notes, including a damaged vault. Prefer a different `-Output` folder. If replacement is intentional, use `.\scripts\publish.ps1 -ReplaceExisting`; the previous EXE is retained as a uniquely named `.backup.exe` and verified before the script reports success. Publishing creates a clean app; it does not migrate notes from the backup into the new EXE. Close the app before replacing its EXE.

## Use

On first launch, create a password or PIN. Type note text into the compose box and click `Save Entry` to queue it. Repeat for as many entries as needed, then click `Commit All`. The app writes an updated replacement EXE, exits briefly, swaps the updated EXE into place while retaining the previous EXE as a backup, and relaunches. The saved confirmation appears after the restarted EXE is verified and the vault is unlocked.

On later launches, enter the same password/PIN to decrypt notes. Search filters the timestamped note list as each key is typed. Pending entries are visible in the list before commit, but they are only embedded into the EXE after `Commit All` completes.

## Storage and Recovery

The vault is encrypted with AES-GCM. The encryption key is derived from the password/PIN with PBKDF2-SHA256 and a per-vault salt. Losing the password/PIN means the embedded notes cannot be recovered.

Only one instance may edit a particular EXE at a time. Commits also refuse to replace an EXE that changed after opening. Use the published single-file EXE; framework-dependent launches such as `dotnet run` do not support embedded storage.

If replacement fails, the app reports an error and keeps the `.pending.exe` containing the new encrypted vault. Startup also reports pending recovery copies. Copy the pending EXE to a new filename (for example, `RecoveredNotepad.exe`), open it with your password, and verify the notes before removing any recovery files. Do not overwrite your original EXE during recovery.

Every successful replacement retains the previous EXE as a uniquely named `.backup.exe`. Copy and open a backup under a new filename to recover the previous saved state. Pending EXEs and backups are never automatically deleted; only stale helper EXEs are cleaned up. Backups consume roughly one app EXE's worth of disk space per commit and contain previous encrypted notes, so remove them manually only when no longer needed. The first backup of a previously unused EXE has no saved vault.

Malformed vaults produce a recovery error instead of a startup crash or an endless password loop. Version 1 vaults support PBKDF2 iteration counts from 100,000 to 2,000,000 and embedded payloads up to 64 MiB; the app's existing 210,000-iteration encryption setting is unchanged.
