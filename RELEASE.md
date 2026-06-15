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

Update these files before building:

- `KjTabBar\Properties\AssemblyInfo.cs`
  - `AssemblyVersion("x.y.z.w")`
  - `AssemblyFileVersion("x.y.z.w")`
- `Setup\Setup.vdproj`
  - `ProductVersion = "8:x.y.z"`
- `Setup\Setup_en.vdproj`
  - `ProductVersion = "8:x.y.z"`

Example for `v1.1.3.0`:

- Assembly version: `1.1.3.0`
- Setup product version: `1.1.3`

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

If you want to upload directly from the build output locations:

```powershell
& 'C:\Program Files\GitHub CLI\gh.exe' release upload v1.1.3.0 `
  'KjTabBar\bin\Release\net481\KjTabBar.exe#KjTabBar-v1.1.3.0.exe' `
  'Setup\Release\setup.exe#KjTabBar-v1.1.3.0-setup.exe' `
  'Setup\Release\Setup.msi#KjTabBar-v1.1.3.0-setup.msi'
```

## 9. Verify the Published Release

Confirm the release contents:

```powershell
& 'C:\Program Files\GitHub CLI\gh.exe' release view v1.1.3.0
```

The asset list should contain:

- `KjTabBar-v1.1.3.0.exe`
- `KjTabBar-v1.1.3.0-setup.exe`
- `KjTabBar-v1.1.3.0-setup.msi`

## 10. Notes for Codex Sessions

- `gh` may fail to read keyring credentials inside a sandboxed session. If that happens, run the GitHub CLI commands outside the sandbox or with approval.
- Keep text files in CRLF format. The repository pre-commit hook checks line endings.
