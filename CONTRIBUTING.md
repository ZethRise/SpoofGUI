# Contributing to SpoofGUI

Thank you for your interest in contributing to SpoofGUI! We welcome bug reports, feature requests, documentation updates, and pull requests.

## How to Contribute

### 1. Reporting Bugs & Feature Requests
* Search the existing Issues before opening a new one to see if it has already been reported.
* If it is a new issue, use the templates provided to file a report.
* Include as much detail as possible: your Windows version, active profile settings, configuration mode (Proxy, Tunnel, System Proxy), and relevant logs from the **Logs** tab in the app.

### 2. Development Setup
Please read the [Build Guide](docs/BUILD.md) to set up your local development environment. You will need:
* **.NET 10 SDK** (for the WinUI 3 frontend)
* **Python 3.10+** (with `pip` and `pyinstaller` for compiling the SNI-Spoofing engine)
* **Inno Setup 6** (for building installers)

To build the project locally, close any running instances of `SpoofGUI.exe` and execute:
```bat
build-release.bat
```

### 3. Pull Request Guidelines
* Keep pull requests focused on a single issue or feature.
* Write clean, commented code and follow the existing coding conventions (C# standard coding style for WinUI 3, PEP 8 for the Python engine source).
* Ensure that the local release build (`build-release.bat`) passes without errors or warnings before submitting a pull request.
* Update documentation (`docs/`) if your changes introduce new configurations, behaviors, or prerequisites.

### 4. Commit Message Guidelines
Use clear and descriptive commit messages. We prefer messages structured as:
`[component] Brief description of changes`

Examples:
* `[frontend] Add profile export button`
* `[engine] Fix WinDivert buffer overflow warning`
* `[ci] Update setup-dotnet version`

---

## Code of Conduct
Please note that this project is released with a [Code of Conduct](CODE_OF_CONDUCT.md). By participating in this project, you agree to abide by its terms.
