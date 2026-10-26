# MSVC

A native C# command-line tool that downloads Microsoft C++ compiler and Windows SDK packages, extracts a portable toolchain, manages multiple installed compiler versions, and applies or restores the current user's compiler environment.

The Microsoft catalog and payload downloads come directly from Microsoft. The selected Scoop manifest pins the catalog URL and the SHA256 of its exact content. Compiler/SDK payload files are not bundled in this release. Review and accept Microsoft's license terms before installing: https://visualstudio.microsoft.com/license-terms/.

## Build

Requires the .NET 10 SDK on Windows x64.

```powershell
dotnet publish src/Msvc/Msvc.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish
```

The project includes the MSVC CLI, installer and toolchain management source plus the small DevTools contracts and Windows process/environment support needed to build it. It does not include the WPF Studio application.

## Commands

```powershell
msvc --help
msvc versions
msvc install --accept-license
msvc list
msvc use 14.44
msvc info
msvc-shell
```

The Scoop manifests are maintained separately in [scoop-devtools](https://github.com/0xFais/scoop-devtools). A Scoop install requires `main/dotnet-sdk` and downloads several GB from Microsoft.

