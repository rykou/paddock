# Paddock

Desktop fences for Windows: translucent panels on your desktop that hold files and shortcuts, roll up out of the way,
and disappear with a double-click.

Paddock is a standalone app. It does not modify or load anything into Explorer; each fence is an ordinary window kept
directly above the desktop, backed by a real folder.

## Features

- **Fences are real folders.** Drop a file or shortcut on a fence and it moves into that fence's folder
  (`%USERPROFILE%\Paddock\<fence name>`). Hold Ctrl to copy instead. Drag items out to move them back.
- **Create a fence by drawing it.** Drag a rectangle on empty desktop, release, click **Create fence here**.
- **Roll up.** Double-click the title (or click the arrow) to collapse a fence to its title bar. Hovering opens it
  temporarily, with a slide animation, and hides any fences it would overlap.
- **Quick hide.** Double-click empty desktop to fade every fence out; hover where one was to bring it back, or
  double-click again to show them all.
- **Survives Show desktop.** Fences stay on screen when Win+D raises the desktop.
- **Full Windows menu on items.** Right-click a file for Open with, Send to, Properties and the rest.
- **Customize** per fence: colour presets or any colour, Windows accent colour, opacity, icon size, title alignment,
  rounded corners, lock position, and "apply this style to all fences".
- Tray icon with new fence, show/hide, start with Windows, and the quick-hide toggle.

## Install

Download `Paddock-<version>.msi` from Releases and run it from a local drive. It installs per machine to
`C:\Program Files\Paddock` and adds a Start menu shortcut. Requires Windows 10 or 11 (rounded corners need 11) and
.NET Framework 4.8, which both ship with.

Settings are stored in `%APPDATA%\Paddock\fences.xml`.

## Build

The build runs from WSL and produces a Windows exe and MSI:

```bash
DOTNET_ROOT=/path/to/dotnet-sdk WIX_DIR=/path/to/wix314-binaries ./build.sh
```

- .NET SDK 8 or newer (Linux) builds the `net48` exe through the `Microsoft.NETFramework.ReferenceAssemblies` package.
- WiX 3.14 binaries (`wix314-binaries.zip` from the WiX releases) run through Windows interop to build the MSI.
  Without `WIX_DIR` the script stops after building `bin/Paddock.exe`.

To run a second, independent instance for testing, set `PADDOCK_HOME` to an empty Windows folder; settings and fence
folders then live there instead of in your profile.

## Limitations

- Icons placed in a fence leave the desktop; Paddock cannot hold the desktop's own icons in place.
- Virtual items such as This PC and Recycle Bin are not files and cannot be dropped in.
- Opacity applies to the whole fence, icons included.
- Rename is not available from the item right-click menu.

Paddock is an independent project and is not affiliated with or endorsed by Stardock.
