# KjTabBar Release Procedure

This document records the release steps for `KjTabBar` so the same flow can be repeated without relying on memory.

## 1. Preconditions

- Work on a clean branch state and confirm the release target commit.
- Confirm the version number to publish, for example `v1.1.3.0`.
- Make sure GitHub CLI is authenticated.

```powershell
& 'C:\Program Files\GitHub CLI\gh.exe' auth status
```

If authentication is missing, log in again:

```powershell
& 'C:\Program Files\GitHub CLI\gh.exe' auth login -h github.com -p https -w
```

## 2. Update Version Numbers

Important MSI upgrade rule:

- Every installer release must get new `ProductCode` and `PackageCode` GUID values in both setup projects.
- Keep `UpgradeCode` unchanged. It is the stable family identifier used by `RemovePreviousVersions`.
- If `ProductVersion` changes but `ProductCode` stays the same, Windows Installer can show: "Another version of this product is already installed."
- Generate fresh GUIDs with `[guid]::NewGuid().ToString().ToUpper()`.

Update these files before building:

- `KjTabBar\Properties\AssemblyInfo.cs`
  - `AssemblyVersion("x.y.z.w")`
  - `AssemblyFileVersion("x.y.z.w")`
- `Setup\Setup.vdproj`
  - `ProductVersion = "8:x.y.z"`
  - `ProductCode = "8:{new-guid}"`
  - `PackageCode = "8:{new-guid}"`
- `Setup\Setup_en.vdproj`
  - `ProductVersion = "8:x.y.z"`
  - `ProductCode = "8:{new-guid}"`
  - `PackageCode = "8:{new-guid}"`

Example for `v1.1.3.0`:

- Assembly version: `1.1.3.0`
- Setup product version: `1.1.3`
- Setup `ProductCode` and `PackageCode`: new GUID values for each setup project and each release build
- Keep `UpgradeCode` unchanged so `RemovePreviousVersions` can upgrade older installs
- Before publishing, inspect the built MSI `Property` table and confirm `ProductVersion`, `ProductCode`, and `UpgradeCode`.

## 3. Build the Application

Build the main executable in Release configuration:

```powershell
dotnet build KjTabBar.build.sln -c Release
```

Expected output:

- `KjTabBar\bin\Release\net481\KjTabBar.exe`

## 4. Build the Installer

Build the setup solution in Release configuration with Visual Studio.

If `devenv.com` is on `PATH`:

```powershell
devenv.com KjTabBar.setup.sln /Build Release
```

If it is not on `PATH`, use the full path, for example:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.com' KjTabBar.setup.sln /Build Release
```

Expected outputs:

- `Setup\Release\setup.exe`
- `Setup\Release\Setup.msi`

If the setup build fails with an unspecified Visual Studio error, check:

- `C:\Users\<UserName>\AppData\Roaming\Microsoft\VisualStudio\<InstanceId>\ActivityLog.xml`

## 5. Prepare Release Asset Names

Upload assets with the following names:

- App executable: `KjTabBar-vX.Y.Z.W.exe`
- Setup launcher: `KjTabBar-vX.Y.Z.W-setup.exe`
- MSI package: `KjTabBar-vX.Y.Z.W-setup.msi`

For example, `v1.1.3.0` becomes:

- `KjTabBar-v1.1.3.0.exe`
- `KjTabBar-v1.1.3.0-setup.exe`
- `KjTabBar-v1.1.3.0-setup.msi`

## 6. Commit, Tag, and Push

Stage changes, create the release commit, create the tag, and push both branch and tag.

```powershell
git add .
git commit -m "v1.1.3.0"
git tag v1.1.3.0
git push origin master
git push origin v1.1.3.0
```

Replace `v1.1.3.0` with the target version when releasing a different build.

## 7. Create the GitHub Release

Run GitHub CLI release commands outside the sandbox in Codex sessions so gh can read the Windows keyring token reliably.

Create the release notes from GitHub automatically:

```powershell
& 'C:\Program Files\GitHub CLI\gh.exe' release create v1.1.3.0 --title 'v1.1.3.0' --generate-notes
```

## 8. Upload the Release Assets

Upload the built files to the existing release.

Do not rely on `source#name` to change the downloaded filename. In GitHub CLI, that syntax sets the asset label, while the actual downloadable filename stays the original file name.

Create copies with the final release filenames first, then upload those files directly:

```powershell
New-Item -ItemType Directory -Force -Path '.\_release' | Out-Null
Copy-Item 'KjTabBar\bin\Release\net481\KjTabBar.exe' '.\_release\KjTabBar-v1.1.3.0.exe' -Force
Copy-Item 'Setup\Release\setup.exe' '.\_release\KjTabBar-v1.1.3.0-setup.exe' -Force
Copy-Item 'Setup\Release\Setup.msi' '.\_release\KjTabBar-v1.1.3.0-setup.msi' -Force

& 'C:\Program Files\GitHub CLI\gh.exe' release upload v1.1.3.0 `
  '.\_release\KjTabBar-v1.1.3.0.exe' `
  '.\_release\KjTabBar-v1.1.3.0-setup.exe' `
  '.\_release\KjTabBar-v1.1.3.0-setup.msi'
```
## 9. Verify the Built MSI Upgrade Metadata

Before uploading assets, confirm the MSI contains the expected metadata:

```powershell
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path 'Setup\Release\Setup.msi').Path, 0))
foreach ($prop in @('ProductCode','ProductVersion','UpgradeCode','ProductName')) {
  $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$prop'"))
  $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
  $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
  $value = if ($record -ne $null) { $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1)) } else { '<missing>' }
  "$prop=$value"
}
```

Expected checks:

- `ProductVersion` matches the setup version, for example `1.1.8`.
- `ProductCode` differs from the previous release.
- `UpgradeCode` matches the previous release.

## 10. Verify the Published Release

Confirm the release contents:

```powershell
& 'C:\Program Files\GitHub CLI\gh.exe' release view v1.1.3.0
```

The asset list should contain:

- `KjTabBar-v1.1.3.0.exe`
- `KjTabBar-v1.1.3.0-setup.exe`
- `KjTabBar-v1.1.3.0-setup.msi`

## 11. Notes for Codex Sessions

- `gh` may fail to read keyring credentials inside a sandboxed session. If that happens, run the GitHub CLI commands outside the sandbox or with approval.
- Keep text files in CRLF format. The repository pre-commit hook checks line endings.
