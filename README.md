# G13Reborn

A modern, open-source Windows control application for the Logitech G13 Advanced Gameboard.

## Goal

Restore the G13's **true analog thumbstick**, while eventually replacing legacy Logitech Gaming Software functionality: programmable keys, macros, application profiles, LCD and lighting.

**Current milestone: working G13 HID diagnostics, analog DirectInput axes and opt-in configurable virtual button forwarding through vJoy.** G13Reborn does not install drivers or write reports to the G13.

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
8. Optionally install and enable a supported x64 **vJoy** driver; configure device #1 with X and Y axes in vJoyConf.
9. Click **Enable vJoy** while connected to the G13. Move the thumbstick and open `joy.cpl` to inspect the virtual controller's X/Y axes.
10. Adjust the **Deadzone %** control (0-30%) and **Invert Y** setting if needed.
11. To test gamepad buttons, configure vJoy device 1 with **32 buttons** in vJoyConf, restart/enable vJoy in G13Reborn, and check **Forward buttons to vJoy**.
12. Use **Edit mappings...** to change physical-G13-button to virtual-button assignments (None, 1-32). The mappings are saved under `%LOCALAPPDATA%\G13Reborn\button-mappings.json`.

The application searches for USB VID 046D / PID C21C. It enumerates HID interfaces even when the Logitech application holds them open.

## Limitations and safety

- **No G13 driver replacement is attempted.** The input layer only uses the Windows HID API to open and read input reports.
- Logitech Gaming Software or the Windows driver may prevent opening the HID interface. In that case the app reports the Windows error; do not uninstall drivers or use Zadig yet.
- Some firmware/driver combinations may require a device initialization feature report before streaming input. This milestone deliberately does not send one.
- The report decoder expects report ID 01 and the 8-byte input layout documented by community drivers; the raw hex log is always retained for investigating variants.
- A G13 owner has confirmed raw analog X/Y changes, simultaneous G-key reports and **working analog vJoy movement in joy.cpl on Windows 11**. Virtual button mapping still requires hardware testing.
- vJoy is a separately installed optional Windows virtual-joystick driver. G13Reborn locates its native x64 interface DLL at runtime (normally under Program Files\\vJoy\\x64); alternatively set `VJOY_INTERFACE_DLL` to the absolute DLL path.
- Analog X/Y are always forwarded while vJoy is enabled. G-keys and thumb buttons are forwarded **only** when the opt-in checkbox is enabled. Default assignments use G1-G22 => vJoy buttons 1-22 and three thumb controls => 23-25; the other controls are unassigned. The physical button labels have not all been individually verified on hardware. Duplicate mappings are supported. If vJoy is configured with fewer than 32 buttons, assignments beyond that limit are ignored.
- The virtual device is reset and released on disconnect or program exit so a held joystick position is not left behind.
- vJoy presents a **DirectInput** controller; it does not pretend to be an Xbox/XInput controller. Some games accept only XInput.

## Projects

- G13Reborn.Device: Windows HID discovery, read-only input streaming, and G13 input packet decoder.
- G13Reborn.Diagnostics: WinForms diagnostic UI with input log, stick visualisation, and optional vJoy controls.
- G13Reborn.VirtualController: analog deadzone/sensitivity mapping and dynamically loaded vJoy feeder (no NuGet packages).
- G13Reborn.SmokeTests: parser, analog mapping and button mapping tests without hardware.

## Protocol references

- https://github.com/golgote/G13 (USB report layout)
- https://github.com/cavefish-dev/g13-driver (Windows replacement-driver approach)

This project is independently implemented. No source code was copied from these projects.

## Roadmap

1. ~~Confirm Windows can enumerate the G13 and read analog thumbstick values.~~
2. ~~Add basic deadzone and auxiliary-button identification.~~ (refine physical calibration later)
3. **Test vJoy virtual DirectInput analog X/Y output on Windows 11.**
4. **Test configurable vJoy button forwarding, then build keyboard/mouse mapping and application profiles.**
5. Add macros, LCD, RGB, calibration presets and an optional XInput backend.

This repository's licence has not yet been selected.

## vJoy installation (optional)

Use the signed Windows 11 release from the upstream project: https://github.com/BrunnerInnovation/vJoy/releases/tag/v2.2.2.0 . Install the 64-bit driver and configure vJoy device 1 with X and Y axes in vJoyConf. Installing a third-party kernel driver requires administrator approval; it is not necessary for the read-only G13 diagnostic functions.

This app does not copy, bundle or redistribute vJoy files.
