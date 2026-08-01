# MYTC v1.0.19 release notes

- Adds the file context-menu action **WinRAR: extract to current folder (X)** for a single supported archive.
- On the first regular launch, MYTC detects a conventional WinRAR location and asks for confirmation. Choosing No opens a file picker for the actual `WinRAR.exe`.
- The WinRAR executable location can later be changed or cleared in **Options → Global settings**.
- The action stays disabled until a valid WinRAR path is configured. Extraction runs in the active pane directory and does not overwrite existing files.
- Verified by 76 automated tests, including configuration migration and safe WinRAR command argument coverage.
