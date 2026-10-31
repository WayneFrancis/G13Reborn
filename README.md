# G13Reborn

A modern, open-source Windows control application for the Logitech G13 Advanced Gameboard.

## Goal

Restore the G13's **true analog thumbstick**, while eventually replacing legacy Logitech Gaming Software functionality: programmable keys, macros, application profiles, LCD and lighting.

**Current milestone: read-only USB HID diagnostics.** The software does not install drivers, send USB commands, remap keys, or create a virtual controller yet.

## Requirements

- Windows 11 (Windows 10 may also work, but is not yet tested)
- Visual Studio 2026 with the .NET desktop development workload, or the .NET 10 SDK
- Logitech G13 (USB VID 046D / PID C21C)

## Run

1. Clone the repository.
2. Open G13Reborn.slnx in Visual Studio 2026.
3. Select G13Reborn.Diagnostics as the startup project.
4. Build and run, or use: dotnet run --project src/G13Reborn.Diagnostics
5. Click **Refresh** to enumerate HID interfaces, select an interface with a nonzero input report length, and click **Connect (read only)**.
6. Move the thumbstick and press G-keys. The position and raw input reports should change.
7. Use **Start capture...** to write every received report to a timestamped TSV file for troubleshooting.

The application searches for USB VID 046D / PID C21C. It enumerates HID interfaces even when the Logitech application holds them open.

## Limitations and safety

- **No driver replacement is attempted.** The first milestone only uses the Windows HID API to open and read input reports.
- Logitech Gaming Software or the Windows driver may prevent opening the HID interface. In that case the app reports the Windows error; do not uninstall drivers or use Zadig yet.
- Some firmware/driver combinations may require a device initialization feature report before streaming input. This milestone deliberately does not send one.
- The report decoder expects report ID 01 and the 8-byte input layout documented by community drivers; the raw hex log is always retained for investigating variants.
- Real-device access and the application have **not yet been verified on an actual G13**.
- There is no vJoy/XInput support at this milestone. That comes after confirming raw analog readings.

## Projects

- G13Reborn.Device: Windows HID discovery, read-only input streaming, and G13 input packet decoder.
- G13Reborn.Diagnostics: WinForms diagnostic UI with input log and thumbstick visualisation.

## Protocol references

- https://github.com/golgote/G13 (USB report layout)
- https://github.com/cavefish-dev/g13-driver (Windows replacement-driver approach)

This project is independently implemented. No source code was copied from these projects.

## Roadmap

1. Confirm Windows can enumerate the G13 and read analog thumbstick values.
2. Add stick calibration/deadzone and detailed button identification.
3. Add virtual DirectInput output via a supported virtual controller driver.
4. Add profiles, macros, LCD output, RGB control and an optional XInput backend.

This repository's licence has not yet been selected.
