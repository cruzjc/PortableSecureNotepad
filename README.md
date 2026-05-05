# Portable Secure Notepad

Portable Secure Notepad is a Windows-only notepad/sticky-note app published as a single self-contained EXE. Notes are encrypted and appended to the EXE itself, so copying the EXE copies the saved notes with it.

## Build

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

## Use

On first launch, create a password or PIN. Type note text into the compose box and click `Commit`. The app writes an updated replacement EXE, exits briefly, swaps the updated EXE into place, and relaunches.

On later launches, enter the same password/PIN to decrypt notes. Search filters the timestamped note list as each key is typed.

## Storage and Recovery

The vault is encrypted with AES-GCM. The encryption key is derived from the password/PIN with PBKDF2-SHA256 and a per-vault salt. Losing the password/PIN means the embedded notes cannot be recovered.
