# SCXI

**SCXI (Steam Controller XInput)** is a Windows utility that allows the 2026 Steam Controller to function as a standard Xbox 360/XInput controller for games outside of Steam.

SCXI reads the Steam Controller directly through Windows Raw Input and forwards its state to a virtual Xbox 360 controller. This allows games that support XInput to recognize the Steam Controller as a normal gamepad while Steam remains running in the background.

## Features

- Full Xbox 360 / XInput controller emulation
- Analog left and right sticks
- Analog triggers
- D-pad
- Face buttons
- Shoulder buttons
- Thumbstick clicks
- Start / View buttons
- Steam / Guide button
- Game rumble forwarded to the Steam Controller haptics
- Automatic controller disconnect and reconnect handling
- Automatic VIIPER startup and shutdown
- Minimal Windows system tray interface
- Remembers whether SCXI was enabled or disabled
- Optional Start with Windows support
- Bundled VIIPER server
- Installer can install the required USB/IP driver when necessary

## Current Status

SCXI `v0.1.0` is the first public release.

The application has been tested with the 2026 Steam Controller on Windows 11 x64.

SCXI currently emulates an Xbox 360 controller. Additional emulation modes may be considered in the future.

---

# How It Works

SCXI uses the following input path:

```text
2026 Steam Controller
        ↓
Windows Raw Input
        ↓
SCXI
        ↓
VIIPER
        ↓
Virtual Xbox 360 Controller
        ↓
XInput Game
```

Steam can remain open while SCXI is running.

SCXI does not translate the controller into keyboard or mouse inputs. Games see a normal Xbox/XInput controller.

---

# Installation

The recommended installation method is the SCXI installer available from the GitHub Releases page.

Download:

```text
SCXI-Setup-0.1.0-win-x64.exe
```

and run the installer.

SCXI installs for the current Windows user under:

```text
%LOCALAPPDATA%\Programs\SCXI
```

For example:

```text
C:\Users\<YOUR_USERNAME>\AppData\Local\Programs\SCXI
```

Administrator access is not required for SCXI itself.

## USB/IP Driver

SCXI uses VIIPER to provide the virtual Xbox controller. On Windows, VIIPER requires the `usbip-win2` driver.

SCXI `v0.1.0` uses:

```text
usbip-win2 0.9.7.7
```

If the required USB/IP installation is not detected, SCXI Setup can install the bundled driver package.

Windows will request administrator permission only for the driver installation.

### USB Device Warning

Installing the USB/IP driver can temporarily restart USB 3.0 hubs.

Connected devices such as:

- keyboards
- mice
- USB audio interfaces
- webcams
- USB storage devices

may briefly disconnect during installation.

Save any important work involving USB devices before continuing.

A Windows restart may be requested after the driver is installed.

---

# Steam Input Setup

SCXI is designed to run while Steam remains open.

However, Steam's global controller mappings should be configured so Steam does not generate additional keyboard, mouse, or shortcut inputs while SCXI is handling the controller.

## Desktop Layout

Use the SCXI Desktop Controller Layout:

```text
SCXI
Desktop Controller Layout for SCXI
```

Steam Workshop configuration:

```text
workshop://3784735851
```

This layout intentionally contains no normal Desktop bindings.

The goal is to prevent the Steam Controller from generating keyboard or mouse input through Steam while SCXI is active.

If Steam provides an option to browse or import community controller layouts, use the Workshop configuration above.

## Guide Button Chord Layout

The Steam **Guide Button Chord Layout** should also be cleared or configured so pressing the Steam/Guide button does not activate additional Steam keyboard, mouse, or shortcut bindings.

SCXI handles the Guide button itself as part of the virtual Xbox controller.

Because Steam may change the location or presentation of these settings between client versions, SCXI does not automatically modify Steam configuration files.

## Steam Configuration Files

Steam may store exported controller configurations underneath a path similar to:

```text
<STEAM_INSTALL_DIRECTORY>\userdata\<STEAM_ACCOUNT_ID>\config\...
```

Paths and filenames may vary depending on the Steam client version and configuration type.

SCXI intentionally does not modify these files automatically.

---

# Using SCXI

After installation, launch SCXI from the Start Menu.

SCXI runs in the Windows system tray.

The tray icon indicates the current state:

```text
Green  = Steam Controller connected
Red    = Disabled or waiting for controller
```

When a Steam Controller successfully connects, SCXI performs a short confirmation haptic buzz.

## Tray Menu

Right-click the SCXI tray icon to access:

```text
Status
Enabled
Refresh Devices
Start with Windows
Quit
```

### Enabled

Turns the SCXI controller bridge on or off.

SCXI remembers this setting between launches.

### Refresh Devices

Restarts controller detection and the virtual controller connection.

This can be useful if a controller or related device was reconnected unexpectedly.

### Start with Windows

Registers SCXI to launch when the current Windows user signs in.

The setting can be enabled or disabled directly from the tray menu.

### Quit

Stops SCXI, removes its virtual controller, shuts down the VIIPER instance started by SCXI, and exits the tray application.

If VIIPER was already running before SCXI started, SCXI will not intentionally terminate that pre-existing instance.

---

# Controller Support

SCXI `v0.1.0` currently targets the **2026 Steam Controller**.

Known wireless receiver identification used during development:

```text
VID: 28DE
PID: 1304
```

Other Steam Controller generations have not been tested with this release.

---

# Rumble

SCXI forwards Xbox/XInput rumble commands from games to the physical Steam Controller haptics.

Both Xbox rumble channels are supported.

The controller also performs a short haptic confirmation when SCXI establishes a successful controller connection.

---

# Tested Games

SCXI has been successfully tested with:

### Rocket League

Launch source:

```text
Epic Games
```

Observed behavior:

- detected immediately as an Xbox controller
- Xbox button glyphs displayed
- analog sticks worked correctly
- analog triggers worked correctly
- no duplicate controller input
- in-game rumble successfully forwarded to the Steam Controller

Additional games should work as long as they support standard XInput controllers.

---

# Uninstalling

SCXI can be removed through Windows Installed Apps.

Uninstalling SCXI removes:

- SCXI
- bundled VIIPER
- SCXI Start Menu entries
- SCXI's Start with Windows registry entry

The USB/IP driver is intentionally **not automatically removed** because it is a separate system component and may be used by other software.

SCXI also preserves its per-user settings file:

```text
%LOCALAPPDATA%\SCXI\settings.json
```

This allows preferences such as the Enabled state to survive a reinstall.

---

# Building From Source

SCXI currently targets:

```text
.NET 10
Windows x64
Windows Forms
```

The project uses:

```text
Viiper.Client 0.7.0
```

Clone the repository and restore/build with:

```powershell
dotnet restore
dotnet build .\SCXI.csproj
```

## Publishing

SCXI includes a Windows x64 publish profile:

```text
Properties\PublishProfiles\win-x64.pubxml
```

Publish with:

```powershell
dotnet publish .\SCXI.csproj -p:PublishProfile=win-x64
```

The output is written to:

```text
dist\SCXI-win-x64\
```

The published package contains:

```text
SCXI.exe

tools\
└── viiper\
    └── viiper.exe
```

The SCXI executable is a self-contained .NET single-file Windows application.

The .NET runtime does not need to be installed separately.

---

# Installer

The Windows installer is built using Inno Setup 7.

Installer source:

```text
installer\SCXI.iss
```

The finished installer is generated under:

```text
installer\output\
```

Generated binaries and downloaded third-party dependency packages are intentionally excluded from Git source history.

Official compiled installers are distributed through GitHub Releases.

---

# Third-Party Components

SCXI uses and/or distributes components from other projects.

## VIIPER

SCXI communicates with the standalone VIIPER server.

SCXI currently packages:

```text
VIIPER 0.7.0
```

VIIPER is distributed under the GNU General Public License.

The corresponding VIIPER license and source archive are included with the installed SCXI package.

SCXI communicates with VIIPER as a separate process through its API.

## Viiper.Client

SCXI uses:

```text
Viiper.Client 0.7.0
```

for communication with VIIPER.

See the VIIPER project for its applicable licensing information.

## usbip-win2

SCXI Setup includes:

```text
usbip-win2 0.9.7.7 x64
```

for systems where the required USB/IP driver is not already installed.

usbip-win2 is licensed under the BSD 2-Clause License.

Its license is included with SCXI.

Third-party projects remain copyright of their respective authors.

---

# License

SCXI itself is licensed under the MIT License.

See:

```text
LICENSE
```

for the full license text.

Third-party components bundled with or used by SCXI remain subject to their own licenses.

---

# Project Branches

The repository uses:

```text
master
```

for stable releases and:

```text
develop
```

for ongoing development.

Release versions are tagged using:

```text
vMAJOR.MINOR.PATCH
```

For example:

```text
v0.1.0
```

---

# Disclaimer

SCXI is an independent community project.

It is not affiliated with, endorsed by, or sponsored by Valve Corporation, Steam, Microsoft, Xbox, VIIPER, or the usbip-win2 project.

Steam, Steam Controller, Xbox, and other product names are trademarks of their respective owners.
