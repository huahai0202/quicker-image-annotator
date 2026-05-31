# Agent Instructions

## Build Requirements

After every project modification, compile and self-test both executable variants:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -SelfTest
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x64 -SelfTest
```

Do not treat only one successful build as complete when source, assets, or release files change.

## GitHub Upload Requirements

When the user asks to upload, publish, release, or push an updated build to GitHub, update both executable artifacts:

- `AnnotatorApp.exe`
- `AnnotatorApp-x64.exe`

If creating or updating a GitHub Release, upload both files as release assets.
