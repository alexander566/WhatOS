# WhatOS

Clean, debloated Windows 11 installer. Run `WhatOS.exe` on an existing Windows PC (or make a USB stick): it downloads the official Microsoft image, lets you pick apps, and installs a lean Windows with tested performance tweaks.

- `app/` installer app (C#, .NET Framework 4.8, WebView2 UI) and its PowerShell/cmd payload
- `setup/pe/` WinPE install screen `whatpe.exe` (C, mingw-w64)

Build on Linux: `./build.sh` (needs `mingw-w64` and .NET SDK 8).

License: GPL-3.0

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- Committers and reviewers: [alexander566](https://github.com/alexander566)
- Approvers: [alexander566](https://github.com/alexander566)

Only binaries built by this repository's GitHub Actions workflow from the public source are signed.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. It downloads the official Windows image from Microsoft and the apps the user picks from their publishers (winget / Microsoft Store).
