# TOST - Legacy WinForms Edition

This folder contains the original, legacy **Windows Forms (WinForms)** version of TOST. 

TOST has since been completely rewritten using **Avalonia UI** to support true cross-platform capabilities (Windows and Linux). The modern codebase lives in the `Desktop/` folder in the root directory.

However, this WinForms version is retained here for reference, historical purposes, or for users who specifically prefer the old Windows-only UI.

## How to Run

If you wish to compile and run this legacy version, you need the **.NET 8.0 SDK** installed on a Windows machine.

From the root of the repository, run:

```bash
dotnet run --project Legacy/WinForms/TOST.WinForms.Legacy.csproj
```

## Notice

- **No longer actively maintained:** New features, cross-platform support, and major bug fixes are built for the Avalonia version (`Desktop/`). 
- **Windows Only:** WinForms is tightly coupled to Windows and will not run on Linux.
