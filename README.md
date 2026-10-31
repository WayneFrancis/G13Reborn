# G13Reborn

A replacement configuration and input-routing application for the Logitech G13 Advanced Gameboard on Windows.

## Verified hardware milestone

The physical G13's analog X/Y thumbstick is readable through the existing Windows HID interface, and vJoy reproduces its true analog movement as a DirectInput controller. The G1-G22 keys, thumb buttons, and the thumbstick's push-to-click also work as vJoy buttons. No Logitech driver replacement or HID writes were needed.

## Programs

**G13Reborn.Config** — the new primary configuration application:
- Edit profiles with names, a per-key action table, 0-30% analog deadzone and Y-axis inversion.
- Assign any G-key or thumb button to: Disabled (`None`), a **keyboard chord** such as `Ctrl+Shift+F1`, held left/right/middle **mouse button**, single-step wheel up/down, or a **vJoy button 1-32**.
- A held physical button holds its keyboard/mouse target down; releasing it releases the corresponding input. Shared modifier keys stay pressed until the last mapped control is released. Mouse-wheel steps trigger only on a new press, not on every HID report.
- Separate **opt-in** checkboxes for native keyboard/mouse input and vJoy input. When forwarding stops or the G13 disconnects, the application attempts to release all injected keys/buttons and reset the virtual joystick.
- Create, clone, delete and manually select profiles. Optionally associate executable file names (e.g. `notepad.exe`) with profiles and enable automatic switching based on the foreground application's process name. The selected profile is the fallback whenever no association matches.
- Profiles persist in `%LOCALAPPDATA%\G13Reborn\profiles.json`. This is separate from the earlier diagnostics-only `button-mappings.json`.

**G13Reborn.Diagnostics** — retains HID discovery, read-only report capture, button-bit inspection, and earlier vJoy tests. Use it to troubleshoot the device; don't leave it forwarding simultaneously with the Config app.

## Run the configuration app

Requirements: Windows 11, Logitech G13, .NET 10 SDK / Visual Studio 2026 with .NET desktop development workload. vJoy is optional for keyboard/mouse-only mappings.

```powershell
cd D:\Dev\G13Reborn
git pull
dotnet run --project src\G13Reborn.Config
```

1. The first run creates an in-memory **Default** profile. By default G1-G22 map to vJoy buttons 1-22, and the three thumb buttons map to 23-25. **No keyboard or mouse injection is enabled by default.**
2. Configure Device 1 in vJoyConf with X/Y axes and 32 buttons, if you want vJoy output.
3. Select a G13 HID interface in the Config app. Choose a profile and edit the mapping table. For a keyboard action, set **Output action = Keyboard** and **Parameter = Ctrl+F1** (or similar).
4. Click **Save profile**, select the **Enable keyboard/mouse injection** and/or **Enable vJoy X/Y and buttons** checkboxes, then click **Start forwarding**.
5. Test in Notepad or `joy.cpl`. To edit mappings again, click **Stop / release** first.

### Important limitations

- **Logitech Gaming Software (LGS) might still emit its original programmed keystrokes.** G13Reborn reads HID input separately but cannot suppress LGS output. Disable/unbind conflicting LGS mappings or close LGS when testing to avoid duplicates.
- **Keyboard/mouse injection uses Windows `SendInput`**, so it follows standard desktop input focus and Windows integrity/UIPI protections: it generally cannot control elevated applications from a non-elevated process. This is not a kernel-level remapper.
- The first version sends keyboard virtual-key events and basic mouse button/wheel events. It does not yet support text macros, key sequences/timings, suppression of Logitech Gaming Software output, application-window targeting, LCD or backlighting.
- vJoy is a third-party DirectInput virtual joystick; some games accept only XInput. Install the signed upstream driver from https://github.com/BrunnerInnovation/vJoy/releases (do not compile or replace its kernel driver just for this app).
- The Config and Diagnostics processes should not simultaneously acquire vJoy Device 1. Only one feeder can own it.
- Physical button names beyond the G keys/thumb controls have not all been confirmed; the app's mapping list derives from current known report bits. No driver changes are made.

## Repository projects

- `src/G13Reborn.Device`: Windows HID discovery and input report decoding.
- `src/G13Reborn.VirtualController`: vJoy DirectInput backend and analog stick mapping.
- `src/G13Reborn.Mapping`: profiles, chord parsing and deterministic key/mouse event transitions.
- `src/G13Reborn.Config`: WinForms profile editor and input forwarding.
- `src/G13Reborn.Diagnostics`: protocol diagnostics and input capture.
- `tests/G13Reborn.SmokeTests`: no-hardware test runner for report decoding, analog mapping, profile logic and keyboard/mouse transitions.

## Development

```powershell
dotnet build G13Reborn.slnx --configuration Release
dotnet run --configuration Release --project tests\G13Reborn.SmokeTests
```

Protocol background: https://github.com/golgote/G13 and https://github.com/cavefish-dev/g13-driver . Independently implemented without copying their source. Repository licence is not yet selected.

## Automatic profile switching

1. Stop input forwarding.
2. Choose an existing profile, or clone your current profile with **New / Clone...**. Clones intentionally start with no executable associations to avoid ambiguous matches.
3. In **Profile executables**, enter one or more executable file names separated by commas, such as `notepad.exe, kenshi_x64.exe`. Enter **file names only**, not paths or window titles.
4. Tick **Auto-switch by foreground app** and click **Save profile**.
5. Select the **manual fallback profile** in the top-left dropdown, then enable an output type and click **Start forwarding**.
6. Switch to the matching application. The **Active:** label displays the profile in use. When you focus G13Reborn or an unassigned program, the selected manual profile is used instead.

The app polls the foreground executable about every 300 ms. Associations
are case-insensitive and are validated to ensure the same executable cannot
be assigned to multiple profiles. Automatic switching is **off by default**
when importing an existing profiles.json file.

**Input safety:** When changing profiles, previously simulated keyboard/mouse
keys are released, vJoy buttons are cleared, and any physically held G13
buttons are ignored until they are released and pressed again. The analog
thumbstick continues using the newly active profile's deadzone/inversion.
