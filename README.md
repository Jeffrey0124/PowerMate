# PowerMateVolume

Small Windows helper for the USB Griffin PowerMate on Windows 11.

It listens for PowerMate knob events and maps them to the default audio output:

- clockwise: volume up
- counterclockwise: volume down
- press: mute toggle

## Commands

```powershell
.\scripts\install-powermate-volume.ps1
.\PowerMateVolume\publish\PowerMateVolume.exe --probe
.\PowerMateVolume\publish\PowerMateVolume.exe --volume-step 0.02
.\PowerMateVolume\publish\PowerMateVolume.exe --backend auto
.\PowerMateVolume\publish\PowerMateVolume.exe --backend winusb
.\PowerMateVolume\publish\PowerMateVolume.exe --backend hid
```

The default `auto` backend tries the current WinUSB/libwdi driver first, then falls
back to HID if the device has been restored to Windows' HID input driver.

Logs are written to `%LOCALAPPDATA%\PowerMateVolume\PowerMateVolume.log`.

## Installer

Portable installer:

```text
dist\installer\PowerMateVolumeSetup.exe
```

Double-click it on another Windows 11 PC to install for the current user. It
installs to `%LOCALAPPDATA%\Programs\PowerMateVolume`, registers current-user
startup, and starts the helper immediately. It is self-contained, so the target
machine does not need a separate .NET install.

Silent install/uninstall:

```powershell
.\dist\installer\PowerMateVolumeSetup.exe /quiet
"$env:LOCALAPPDATA\Programs\PowerMateVolume\PowerMateVolumeSetup.exe" /uninstall
```

SHA256:

```text
35A09B3B31CD95F435110B1159510E7DB8A4068411D407A7083D250C4C12809A
```
