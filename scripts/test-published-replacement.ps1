param([string]$PublishedExe = "publish\PortableSecureNotepad.exe")

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appExe = (Resolve-Path -LiteralPath $PublishedExe).Path
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('psn-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

try {
    $coreProject = Join-Path $repoRoot 'src\PortableSecureNotepad.Core\PortableSecureNotepad.Core.csproj'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$([System.Security.SecurityElement]::Escape($coreProject))" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $fixtureRoot 'Fixture.csproj')
    @'
using PortableSecureNotepad.Core;

const string password = "smoke-test-password";
const string newPassword = "new-smoke-password";
if (args.Length > 0 && args[0] == "verify-backup")
{
    var saved = VaultCrypto.Decrypt(EmbeddedPayload.ReadPayload(args[1])!, newPassword);
    if (saved.Notes.Count != 2 || !saved.Notes.Any(note => note.Text == "committed state"))
        throw new Exception("Publish backup did not retain the rekeyed, edited vault and draft");
    Console.WriteLine("Publish backup decrypts to the committed state.");
    return;
}
if (args.Length > 0 && args[0] == "stage")
{
    string appExe = args[1], cleanFixture = args[2], directory = args[3];
    Directory.CreateDirectory(directory);
    string target = Path.Combine(directory, "PortableSecureNotepad.exe");
    byte[] original = VaultCrypto.Encrypt(new VaultDocument { Notes = [new NoteEntry { Text = "previous state" }] }, password);
    EmbeddedPayload.WriteExecutableWithPayload(cleanFixture, target, original);
    using var owner = VaultInstanceLock.TryAcquire(target, TimeSpan.Zero)
        ?? throw new Exception("Could not lock smoke fixture");
    var store = new ExecutableVaultStore(target);
    using var session = new VaultSession(VaultCrypto.Decrypt(store.ReadPayload()!, password), password);
    session.BeginEdit(session.Notes.Single().Id);
    session.UpdateDraft("committed state");
    session.SaveDraft();
    session.UpdateDraft("preserved draft");
    session.Lock();
    session.Unlock(password);
    foreach (var format in new[] { VaultExportFormat.Encrypted, VaultExportFormat.Json, VaultExportFormat.Text })
    {
        string extension = format switch { VaultExportFormat.Encrypted => "psnvault", VaultExportFormat.Json => "json", _ => "txt" };
        VaultTransfer.WriteExport(Path.Combine(directory, "export." + extension), session.CreateExport(format), format);
    }
    byte[] updated = session.CreatePasswordChangePayload(password, newPassword);
    var request = store.PrepareCommit(updated);
    // Run the actual published WinForms replacement helper; the relaunched target is
    // a console fixture so this regression check does not need interactive dialogs.
    File.Copy(appExe, request.HelperPath, overwrite: true);
    store.StartReplacementHelper(request);
    File.WriteAllText(Path.Combine(directory, "staged.txt"), request.ReplacementPath);
    return;
}

string executable = Environment.ProcessPath!;
string root = Path.GetDirectoryName(executable)!;
try
{
    using var owner = VaultInstanceLock.TryAcquire(executable, TimeSpan.Zero)
        ?? throw new Exception("Helper failed to release the vault lock before relaunch");
    string completionHash = ExecutableReplacement.GetCommitCompletionHash(args)
        ?? throw new Exception("Relaunch lacked completion hash");
    if (ExecutableVaultStore.GetFileHash(executable) != completionHash)
        throw new Exception("Saved executable hash differs from helper confirmation");
    byte[] savedPayload = EmbeddedPayload.ReadPayload(executable)!;
    var saved = VaultCrypto.Decrypt(savedPayload, newPassword);
    if (saved.Notes.Count != 2 || !saved.Notes.Any(note => note.Text == "committed state") ||
        !saved.Notes.Any(note => note.Text == "preserved draft")) throw new Exception("Edited note and draft were not saved");
    bool oldPasswordRejected = false;
    try { VaultCrypto.Decrypt(savedPayload, password); }
    catch (VaultUnlockException) { oldPasswordRejected = true; }
    if (!oldPasswordRejected) throw new Exception("Old password still unlocks rekeyed target");
    string backup = Directory.GetFiles(root, "*.backup.exe").Single();
    string oldNote = VaultCrypto.Decrypt(EmbeddedPayload.ReadPayload(backup)!, password).Notes.Single().Text;
    if (oldNote != "previous state") throw new Exception("Previous state was not retained");
    var previous = VaultCrypto.Decrypt(EmbeddedPayload.ReadPayload(backup)!, password).Notes.Single();
    var edited = saved.Notes.Single(note => note.Text == "committed state");
    if (edited.Id != previous.Id || edited.CreatedAt != previous.CreatedAt || edited.UpdatedAt is null)
        throw new Exception("Editing did not preserve note identity, creation date and edit date");
    var restored = VaultCrypto.Decrypt(VaultTransfer.ReadEncryptedBackup(Path.Combine(root, "export.psnvault")), password);
    if (restored.Notes.Count != 2) throw new Exception("Encrypted export did not retain draft");
    var json = VaultTransfer.ReadPlainJson(Path.Combine(root, "export.json"));
    if (json.Notes.Count != 2 || !File.ReadAllText(Path.Combine(root, "export.txt")).Contains("preserved draft"))
        throw new Exception("Plain exports did not retain working notes and draft");
    using var imported = new VaultSession(new VaultDocument(), newPassword);
    if (imported.Import(restored) != 2 || imported.Import(json) != 0)
        throw new Exception("Backup restore failed to import or deduplicate notes");
    string helper = ExecutableReplacement.GetCleanupPaths(args, executable).Single();
    ExecutableReplacement.DeleteWithRetriesInBackground([helper]);
    for (int i = 0; i < 200 && File.Exists(helper); i++) Thread.Sleep(100);
    if (File.Exists(helper)) throw new Exception("Helper was not cleaned up");
    if (Directory.GetFiles(root, "*.pending.exe").Length != 0)
        throw new Exception("Successful replacement left an unconsumed pending EXE");
    File.WriteAllText(Path.Combine(root, "passed.txt"), "Published helper saved edited notes and a locked draft with the new password, retained the old-password backup, verified exports/import, relaunched, released the lock, and cleaned up.");
}
catch (Exception ex)
{
    File.WriteAllText(Path.Combine(root, "failed.txt"), ex.ToString());
    Environment.ExitCode = 1;
}
'@ | Set-Content -LiteralPath (Join-Path $fixtureRoot 'Program.cs')

    $fixtureProject = Join-Path $fixtureRoot 'Fixture.csproj'
    $fixtureOutput = Join-Path $fixtureRoot 'published'
    dotnet publish $fixtureProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $fixtureOutput
    if ($LASTEXITCODE -ne 0) { throw 'Smoke fixture publish failed.' }

    $runDirectory = Join-Path $fixtureRoot 'portable fixture with spaces'
    $fixtureDll = Join-Path $fixtureRoot 'bin\Release\net10.0\win-x64\Fixture.dll'
    dotnet $fixtureDll stage $appExe (Join-Path $fixtureOutput 'Fixture.exe') $runDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Smoke fixture staging failed.' }

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $passed = Join-Path $runDirectory 'passed.txt'
    $failed = Join-Path $runDirectory 'failed.txt'
    while (-not (Test-Path -LiteralPath $passed) -and -not (Test-Path -LiteralPath $failed) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (Test-Path -LiteralPath $failed) { throw (Get-Content -LiteralPath $failed -Raw) }
    if (-not (Test-Path -LiteralPath $passed)) { throw "Published helper smoke test timed out. Fixtures retained at $fixtureRoot" }
    Get-Content -LiteralPath $passed

    $target = Join-Path $runDirectory 'PortableSecureNotepad.exe'
    $savedHash = (Get-FileHash -LiteralPath $target).Hash
    $refused = $false
    try { & (Join-Path $PSScriptRoot 'publish.ps1') -Output $runDirectory }
    catch { $refused = $_.Exception.Message -like '*may contain saved notes*' }
    if (-not $refused -or (Get-FileHash -LiteralPath $target).Hash -ne $savedHash) {
        throw 'Publishing did not safely refuse the existing saved vault.'
    }

    # Simulate a native build failure through the real publish script.
    function dotnet { & $env:ComSpec /c exit 7 }
    $buildRejected = $false
    try { & (Join-Path $PSScriptRoot 'publish.ps1') -Output $runDirectory -ReplaceExisting }
    catch { $buildRejected = $_.Exception.Message -like '*exit code 7*' }
    finally { Remove-Item Function:\dotnet }
    if (-not $buildRejected -or (Get-FileHash -LiteralPath $target).Hash -ne $savedHash) {
        throw 'Failed native publish did not retain the existing saved vault.'
    }

    & (Join-Path $PSScriptRoot 'publish.ps1') -Output $runDirectory -ReplaceExisting
    $savedBackups = @(Get-ChildItem -LiteralPath $runDirectory -Filter '*.backup.exe' |
        Where-Object { (Get-FileHash -LiteralPath $_.FullName).Hash -eq $savedHash })
    if ($savedBackups.Count -ne 1) { throw 'Publishing did not retain exactly one verified copy of the previous saved EXE.' }
    dotnet $fixtureDll verify-backup $savedBackups[0].FullName
    if ($LASTEXITCODE -ne 0) { throw 'Publish backup verification failed.' }
    Write-Output 'Publishing refused implicit overwrite, retained the vault on build failure, and backed up explicit replacement.'
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'all-passed.txt') -Value 'passed'
}
finally {
    # Keep failed fixtures for diagnosis. Remove only this run's generated fixture directory.
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $tempBoundary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ((Test-Path -LiteralPath (Join-Path $fixtureRoot 'all-passed.txt')) -and
        $resolvedFixture.StartsWith($tempBoundary, [StringComparison]::OrdinalIgnoreCase) -and
        ([IO.Path]::GetFileName($resolvedFixture)).StartsWith('psn-smoke-')) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
