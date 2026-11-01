# G13Reborn

A replacement configuration and input-routing application for the Logitech G13 Advanced Gameboard on Windows.

## Verified hardware milestone

The physical G13's analog X/Y thumbstick is readable through the existing Windows HID interface, and vJoy reproduces its true analog movement as a DirectInput controller. The G1-G22 keys, thumb buttons, and the thumbstick's push-to-click also work as vJoy buttons. No Logitech driver replacement or HID writes were needed.

## Programs

**G13Reborn.Config** — the new primary configuration application:
- Edit profiles with names, a per-key action table, 0-30% analog deadzone and Y-axis inversion.
- Assign any G-key or thumb button to: Disabled (`None`), a **keyboard chord** such as `Ctrl+Shift+F1`, held left/right/middle **mouse button**, single-step wheel up/down, a **vJoy button 1-32**, or a **named macro**.
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
- Native keyboard/mouse output uses Windows `SendInput`. Macros can type Unicode text, send key/mouse actions and delays; there is no macro recording UI, repeated-while-held behaviour or virtual joystick steps yet. LGS output suppression, window targeting, LCD and backlighting are also not implemented.
- vJoy is a third-party DirectInput virtual joystick; some games accept only XInput. Install the signed upstream driver from https://github.com/BrunnerInnovation/vJoy/releases (do not compile or replace its kernel driver just for this app).
- The Config and Diagnostics processes should not simultaneously acquire vJoy Device 1. Only one feeder can own it.
- The native vJoy DLL stays loaded within each G13Reborn process until exit. Clicking **Stop / release** still resets and relinquishes device #1; retaining the DLL avoids a vJoy hidden-window registration failure when **Start forwarding** is pressed again in the same process.
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

## vJoy restart regression check

With Device 1 configured in vJoyConf, run G13Reborn.Config with vJoy output
enabled. Perform **Start forwarding → Stop / release → Start forwarding**
several times without closing the application. Verify that the G13 stick and
buttons resume in `joy.cpl` on every cycle. An earlier implementation unloaded
and reloaded `vJoyInterface.dll` on each cycle, which could leave a native
registered window class behind and cause vJoy's `RegisterClassEx` and dummy
window error dialogs. The DLL is now cached per process; the vJoy device is
still released upon Stop / release and window close.

This vJoy library lifecycle change is a workaround for the existing signed
driver's userspace interface, not a kernel driver modification.


## Text macros and stepped macros

Macros belong to a named profile and are persisted in that profile's
`%LOCALAPPDATA%G13Rebornprofiles.json`.

1. Stop forwarding. Choose your profile, then click **Edit macros...**.
2. Click **New**, enter a unique macro name, and select either **Text** or
   **Steps**.
3. For Text, enter the text to type; for Steps, add actions with **Add step**.
   Available sequence actions: `Delay`, `KeyDown`, `KeyUp`,
   `KeyPress`, `MouseDown`, `MouseUp`, `MouseClick`,
   `MouseDoubleClick`, `WheelUp`, `WheelDown`. Keyboard actions
   take one key name (e.g. `Ctrl`, `A`, `F1`), mouse actions use
   `Left`, `Right`, or `Middle`. Each step can have
   **Delay after (ms)**, from 0 to 60000.
4. Click **Save macros**. In the main mapping grid, select **Macro** for
   the desired G-key and enter the macro's exact name in **Parameter**.
5. Click **Save profile**. Tick **Enable keyboard/mouse injection**, then
   **Start forwarding**. For text macros, test in Notepad first.

Only one macro is active at a time. A macro triggers on a fresh button press,
not on every report while the key is held. Other macro presses while one is
running are intentionally ignored rather than queued. Stop forwarding or
switching active application profiles cancels playback and releases any
macro-held keyboard/mouse buttons. Ordinary mapped keys that remain held are
protected against being released by macros.

Macro text is inserted through Unicode Windows input events. A newline is sent
as Enter and a tab as Tab. Text injection goes to the currently focused
application and, as with all Windows synthetic input, some games or elevated
applications may ignore it. A macro's own key-down events are released at the
end, even if a sequence deliberately omits a matching KeyUp. This first
implementation does not support repeated macros while a key is held.


### Text macro reliability and pacing

Text is sent as paced Unicode key down/up events rather than flooding one
large SendInput batch. The default delay is 25 ms between characters;
configurable from 5 to 100 ms in **Edit macros...**. Older saved profiles
containing `TextDelayMs: 0` use the safe 25 ms default automatically.

While a text macro runs, ordinary mapped G-key actions are deferred and
replayed on successful macro completion. Stop, cancellation or profile
changes discard queued actions to avoid sending stale keystrokes into a
different program. This cannot suppress keystrokes generated separately
by Logitech Gaming Software.



## Recording a keyboard/mouse sequence

**Macro Editor → New → Record / replace steps...** opens a visible
time-limited recorder. The recorder provides a **three-second countdown**
to switch focus to the application you want to record. It captures native
keyboard down/up events, left/right/middle mouse button transitions, mouse
wheel movement, and elapsed delay between actions.

Use **Stop and use recording** to accept the captured sequence, or
**Discard** to throw it away. Only accepted recordings are transferred to
the step editor, where timing and actions can be modified. Then click
**Save macros** and **Save profile** to persist changes to JSON.

**Recording is explicitly started**, shows a visible floating window,
stops after at most 60 seconds or when nearly 200 actions are captured,
and uninstalls both low-level hooks immediately when stopped. It never
swallows or modifies the physical input being recorded and ignores other
software's synthetic input. G13Reborn forwarding must be stopped to edit
macros, so its own mappings cannot be recursively recorded.

**Scope / limitations:** Motion of the mouse cursor, cursor positions and
the target application window are **not** recorded; a replayed mouse click
occurs at the cursor's current position. Recording is system-wide while
active, so do not type passwords or other sensitive data during recording.
This first version represents common left/right modifier keys as generic
Shift/Ctrl/Alt, filters key auto-repeat, and does not capture extra mouse
buttons or wheel horizontal scrolling.

The recording isn't automatically linked to a G-key. After accepting
recording, assign the macro by its name in the G-key mapping grid.


### Inserting recordings and standardizing sequence timing

You can now start recording a **new Steps macro with zero existing steps**.
An empty Steps macro is an acceptable editing draft; it becomes valid for
saving once a recording (or manually added steps) supplies its first action.

In the macro step grid, select a step before clicking
**Record / insert steps...** to insert the recording immediately after it.
If nothing is selected, recording **appends** to the end. A new empty
sequence accepts the recording as its first steps. Existing actions are
preserved, subject to the existing 200-step macro limit. After loading
a macro in the editor, no row is selected by default, so recording
appends unless you intentionally select a row.

The sequence editor also provides two independent timing controls:
- **After KeyDown (ms)** and **Apply key-down**, default 25 ms. Sets
  \`DelayAfterMs\` on *KeyDown* steps only. For a matching KeyUp, this
  delay controls how long the key stays pressed.
- **After KeyUp (ms)** and **Apply key-up**, default 25 ms. Sets
  \`DelayAfterMs\` on *KeyUp* steps only, spacing key releases from the
  next command.

Applying one does not modify the other, and neither changes mouse,
wheel, or Delay-only actions. Both can be applied for approximately
25 ms of key-down time plus 25 ms between presses (roughly a 50 ms
key cycle; operating-system scheduling can affect actual timings).
For \`KeyPress\` steps, playback still uses its own short fixed tap
time; use explicit KeyDown and KeyUp steps if you need these two timing
controls.

Use **Save macros** and then **Save profile** to persist both the
recorded steps and standardized delays.


### Recorder Stop button handling

The recorder ignores mouse events whose screen position is over the
recorder window, even while another application technically still has
foreground focus. This prevents the click on **Stop and use recording** (or
**Discard**) from becoming the final left-mouse down/up action.

Time spent moving the mouse back to the recorder is not added to the end
of the sequence. When an actual key/mouse button is still held at Stop,
the recorder inserts a matching release with at most a 25 ms delay instead
of waiting for the user-interface click time.
