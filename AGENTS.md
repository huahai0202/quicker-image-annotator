# Agent Instructions

## Build Requirements

After every project modification, compile and self-test all executable variants:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -SelfTest
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x64 -SelfTest
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x86 -SelfTest
```

Do not treat only one successful build as complete when source, assets, or release files change.

## GitHub Upload Requirements

When the user asks to upload, publish, release, or push an updated build to GitHub, update all executable artifacts:

- `AnnotatorApp.exe`
- `AnnotatorApp-x64.exe`
- `AnnotatorApp-x86.exe`

If creating or updating a GitHub Release, upload all three files as release assets.
