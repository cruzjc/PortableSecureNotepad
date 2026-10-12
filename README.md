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

After publishing, run `.\scripts\test-published-replacement.ps1` to exercise the actual published helper against generated encrypted fixtures. It verifies two successive commits to the same EXE, saved notes and password, zero automatic backup EXEs, relaunch, lock release, temporary-file cleanup, manual export/import, overwrite refusal, native build failure, and explicit publish replacement. The relaunched fixture is a console app; this check does not cover interactive WinForms dialogs. Failed fixtures are retained under the printed temporary smoke directory for diagnosis.

Publishing refuses to overwrite any existing output EXE because it may contain notes, including a damaged vault. Prefer a different `-Output` folder. If replacement is intentional, use `.\scripts\publish.ps1 -ReplaceExisting`; the previous EXE is retained as a uniquely named `.backup.exe` and verified before the script reports success. Publishing creates a clean app; it does not migrate notes from the backup into the new EXE. Close the app before replacing its EXE.

## Use

On first launch, create a password or PIN. Type note text into the compose box and click `Save Entry` to queue it. Repeat for as many entries as needed, then click `Commit All`. The app writes a temporary replacement, exits briefly, atomically overwrites the same EXE, and relaunches. The saved confirmation appears after the restarted EXE is verified and the vault is unlocked. Commits do not create automatic backup EXEs; after successful cleanup, only the original app EXE remains.

On later launches, enter the same password/PIN to decrypt notes. Search filters the timestamped note list as each key is typed. Pending entries are visible in the list before commit, but they are only embedded into the EXE after `Commit All` completes.

## Editing and Locking

Select an entry and click `Edit selected`, double-click it, or press `Ctrl+E`. Edit its text, then click `Update Entry` (`Ctrl+S`) to stage the change. The original entry ID and creation date are preserved, and an edit date is recorded. `Cancel edit` discards only the editor changes. `Commit All` (`Ctrl+Shift+S`) saves staged changes and the current draft through the verified restart flow. Editing a pending entry updates it instead of creating a duplicate. Search keeps the selected entry when it still matches.

Use **Vault → Lock now** or `Ctrl+L` to clear the note list, preview, search and editor. Working notes, pending changes and the exact unfinished draft are held in an encrypted in-memory checkpoint. Use **Vault → Unlock** or `Ctrl+L` again and enter the current password to resume. A failed or canceled unlock leaves the screen locked. Locking does not commit to disk; closing the app still discards uncommitted work after confirmation.

Use **Vault → Change password/PIN** to enter the current password and confirm a new one. This commits all working notes and the current draft, then restarts. The same EXE now requires the new password. If preparation fails, the current window retains its original password and work. Earlier encrypted exports and any existing legacy backups still require their original password; changing the current password does not change those files.

## Backup, Export and Import

The **File** menu offers three formats. Exports always include every working entry and the active draft, regardless of the search filter, without changing the EXE or marking work as committed.

| Format | Use |
| --- | --- |
| Encrypted `.psnvault` | A compact, restorable backup encrypted with the current vault password (`Ctrl+Shift+E`). |
| Unencrypted `.json` | A restorable export retaining entry IDs, creation dates and edit dates. |
| Unencrypted `.txt` | Readable UTF-8 notes with timestamps; text exports are not imported as structured backups. |

Plain exports show a confirmation explaining that the notes are unencrypted. Export filenames must match their format, so export cannot overwrite an EXE. Files are staged, flushed and verified before atomic replacement of an existing export.

Choose **File → Import backup** (`Ctrl+Shift+I`) to import `.psnvault`, JSON, or an older portable EXE backup. Encrypted backups request the password used when that backup was created. Imported entries are staged until `Commit All`. Existing entries are preserved: repeated identical entries are skipped, and conflicting versions of an entry are imported under a new ID rather than overwriting your current text. Save a backup before upgrading to a newly published EXE, then import it into the new app.

## Storage and Recovery

The vault is encrypted with AES-GCM. The encryption key is derived from the password/PIN with PBKDF2-SHA256 and a per-vault salt. Losing the password/PIN means the embedded notes cannot be recovered.

Only one instance may edit a particular EXE at a time. Commits also refuse to replace an EXE that changed after opening. Use the published single-file EXE; framework-dependent launches such as `dotnet run` do not support embedded storage.

If replacement fails, the app reports an error and keeps the `.pending.exe` containing the new encrypted vault. Startup also reports pending recovery copies. Copy the pending EXE to a new filename (for example, `RecoveredNotepad.exe`), open it with your password, and verify the notes before removing any recovery files. Do not overwrite your original EXE during recovery.

Successful commits consume the pending replacement and remove the temporary helper. No previous-state EXE is retained automatically. Use **File → Encrypted backup** or a plain export when you want a separate saved copy. Failed pending EXEs and existing legacy backups are preserved for recovery; remove them manually when no longer needed. The developer publish script's explicitly requested `-ReplaceExisting` backup is separate from normal app commits.

Malformed vaults produce a recovery error instead of a startup crash or an endless password loop. Version 1 vaults support PBKDF2 iteration counts from 100,000 to 2,000,000 and embedded payloads up to 64 MiB; the app's existing 210,000-iteration encryption setting is unchanged.
