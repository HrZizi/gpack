# Build Artifact Rule

Whenever the program is in a usable or testable state, or after completing major implementations and passing tests:

1. Execute the full test suite:
   ```powershell
   dotnet test NagelRepacks.sln -c Release
   ```
2. Build and publish the standalone release binary artifacts (self-contained single-file):
   ```powershell
   .\build.ps1 -Standalone
   ```
3. Ensure the published standalone executables (`artifacts/gpack/gpack.exe` and `artifacts/installer/Setup.exe`) are generated and healthy.
