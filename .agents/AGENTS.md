# JournalApp Customization Rules

## Version Management Rule
- **Every time** codebase changes are compiled and prepared for a release (push/deploy/release), you **must** update the version number of the application.
- The version format is `Major.Minor.Build.Revision` (e.g., `1.0.4.0`).
- Increment the third or fourth digit of the version number (e.g., from `1.0.3.0` to `1.0.4.0`).
- Update the version number in the following files:
  1. `JournalApp.csproj` (the `<Version>` tag)
  2. `Package.appxmanifest` (the `Version` attribute of `<Identity>`)

## Release Packaging Rule
- After bumping the version, compile the project in release mode:
  ```powershell
  dotnet publish -c Release -r win-x64 -p:GenerateAppxPackageOnBuild=true -p:PackageCertificatePassword=Password
  ```
- Copy the newly-built `.msix` and `.cer` files from `AppPackages\JournalApp_<Version>_x64_Test\` to the `Installation\` folder:
  ```powershell
  Copy-Item "AppPackages\JournalApp_<Version>_x64_Test\JournalApp_<Version>_x64.msix" "Installation\JournalApp_<Version>_x64.msix" -Force
  Copy-Item "AppPackages\JournalApp_<Version>_x64_Test\JournalApp_<Version>_x64.cer" "Installation\JournalApp_<Version>_x64.cer" -Force
  ```
- Make sure to update the certificate filename references in `Installation\Install.ps1` and `Installation\Install.bat` to match the new version if needed.
- Compress the `Installation\` folder into `Installation.zip` in the root:
  ```powershell
  Compress-Archive -Path "Installation\*" -DestinationPath "Installation.zip" -Force
  ```
- Commit all code changes, push to GitHub, and create a new GitHub release using `gh release create v<Version>`:
  ```powershell
  gh release create v<Version> "AppPackages\JournalApp_<Version>_x64_Test\JournalApp_<Version>_x64.msix" "Installation.zip" "JournalAppDevKey.cer" --title "JournalApp v<Version>" --notes "Release description"
  ```
