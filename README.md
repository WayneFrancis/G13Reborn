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


## Testing a macro without G13 forwarding

In **Edit macros...**, choose a Text or Steps macro, make changes, and click
**Test macro...**. The test uses the current **unsaved** macro editor values;
it does **not** write JSON, change mappings or require assigning a G-key.
The main forwarding service must be stopped while the editor is open.

A small **Test macro** window provides a three-second countdown. Switch
to a disposable target document such as Notepad. The test waits until
some other application, not G13Reborn, has foreground focus before it
begins sending input. **The test generates real keyboard/mouse events**,
so do not test dangerous shortcuts or click macros in important applications.

Click **Cancel test** (or close the test window) at any time: pending
steps are cancelled, and every key/mouse button held by the test is
released. Playback also releases any held inputs on natural completion or
on an input injection error. A safety timeout ends tests after two
minutes. This is an independent test output channel; normal HID/vJoy
forwarding is not started and profile state is not modified.

Text tests use the existing reliable 25 ms default character pacing,
while sequence tests use their configured delays and step ordering.
Keyboard events in elevated programs and raw-input games may not
respond to Windows SendInput; Notepad is the initial recommended target.


## Manual sequence insertion and playback diagnostics

The Steps editor now has **Add step (after)** and **Insert before**:

- Select a numbered step row before clicking **Add step (after)** to
  insert the new action immediately after it; with no selection the new
  action goes at the end.
- Select a numbered row before clicking **Insert before** to insert
  the new action immediately before it; with no selection the new
  action is added at the very beginning, including in an empty macro.
- The newly inserted row is selected so repeated Add/Insert operations
  stay at the insertion point. Click **Clear selection** to return to
  the prepend/append defaults. Row headers display step numbers and
  a small status line shows the current selection.

The **Test macro...** window now includes a read-only log showing
the currently executing step, its action and parameter, and the
configured delay after it. For text macros the progress line shows
how many UTF-16 code units have been sent, without displaying the
macro's contents in the log. Logs show completion or cancellation.
The recorded sequence itself and the existing safe text playback are
unchanged.


## Multiple-row edits and clipboard shortcuts

In the Steps grid, use **Ctrl+click** or **Shift+click** to select
multiple rows (including disjoint selections). **Remove selected** and
the **Delete** key remove all selected steps, not just the current row.

The grid supports **Ctrl+C** (copy), **Ctrl+X** (cut), and **Ctrl+V**
(paste) for selected steps. Copied steps retain their action type,
parameters, and individual after-delays. Clipboard data uses a private
G13Reborn macro-steps format as well as readable text. This allows
copy/paste between different macros within the app without treating
arbitrary text as executable macro steps.

Paste inserts **after the selected step**, or after the **last selected
row** when several are selected. With no selection it appends. Cut
removes rows only after the clipboard has accepted the content. When
editing text inside an individual grid cell, standard text editing
shortcuts remain available instead.

Recorded sequences have a natural final after-delay of 0 ms. When
**Record / insert steps...** inserts a recording before existing later
steps, the last recorded step now receives the **After KeyUp** spinner's
default join delay (25 ms), rather than immediately playing the next
step. The join is added only if the last recorded step's delay is zero;
a nonzero recorded delay is preserved. Recordings appended at the end
keep their trailing zero delay.


## Macro management and unsaved changes

The Macro Editor now offers **Duplicate** alongside **New** and **Delete**.
Duplication makes a deep copy, including all timing and step actions,
with a unique name such as \`AdvMacro copy\` or \`AdvMacro copy 2\`.
The original macro is unchanged.

Renaming existing macros in the editor now automatically updates matching
G-key **Macro** assignments in the main profile mapping grid when you
accept the edits. Renames are tracked against their original identities
throughout the editor session, including after repeated edits and
duplication. Deleting a macro with assigned G-keys prompts before clearing
those now-invalid assignments. Changing an assigned macro's name no
longer causes a missing-macro validation failure when saving the profile.

**Save macros** accepts edits back to the profile window but does not
write the profile file. **Save profile** is the actual persistence boundary.
Macro changes are staged separately from the saved profile data until
then; choosing **No** at the unsaved-profile prompt safely discards those
staged changes rather than accidentally writing them to disk.

**Cancel** or closing the Macro Editor now warns before discarding
unsaved macro edits, including changes not yet committed from individual
fields or step cells. You can keep editing instead of losing changes.


## Main G-key mapping macro dropdown

In the main **Profiles & Input Mapping** grid, changing **Output action**
to **Macro** changes that row's **Key chord / vJoy # / Macro** cell
into a dropdown. The list includes macro names defined in the **current
profile**, sorted alphabetically. It does not display macros from other
profiles. Existing Macro assignments are displayed as their selected
name; switching from a keyboard mapping to Macro clears its incompatible
old key-chord value.

Create, rename, duplicate or delete macros through **Edit macros...**.
When you accept the editor, the dropdowns are updated immediately from
the staged macro definitions (even before **Save profile**). Renamed
assignments keep their new name; deleted assignments are cleared after
confirmation. If no macro exists, the dropdown has no macro names yet:
create one in **Edit macros...** first. Empty selections remain invalid
for saving a Macro assignment, rather than silently choosing a macro.
All non-Macro actions retain ordinary editable parameter text.


## Experimental G13 hardware LCD and lighting (explicit opt-in)

The main **Profiles & Input Mapping** window now has **LCD / backlight...**
next to the G13 device controls. Open it only while G13 forwarding is
stopped. The diagnostic window enumerates HID collections for device
\`046D:C21C\` separately:

- **RGB / M-key LEDs**: the selected feature-report collection (5-byte
  report or larger) accepts a manually chosen RGB colour and the four
  M1/M2/M3/MR light checkboxes. Press **Apply RGB** or **Apply M LEDs**
  to send an explicit one-shot HID feature report. RGB is report ID
  \`0x07\` with R/G/B bytes; macro LEDs use report ID \`0x05\`, with
  four bit flags.
- **LCD**: the selected output-report collection must advertise exactly
  992 bytes. **Send test pattern** displays a 160×43 monochrome test
  image; **Clear LCD** sends an empty frame. The bitmap starts at byte
  32, with six vertical 8-row bands, one byte per column.

This feature does **not** replace the system HID driver, change
the input-report reader, install new services, save device settings
or automatically overwrite the LCD/background lighting. It sends
nothing until a button is pressed. On Windows, a write-capable HID
interface may be held exclusively by Logitech Gaming Software or
another utility; this will be reported as an error. A working build
and correct byte-level tests do **not** establish that a particular
Windows driver will accept real hardware commands.

Protocol references:
- Linux \`hid-lg-g15\`: https://code.googlesource.com/linux/torvalds/linux/+/de03b17ec046d81c4713bc54306579b9024fc513/drivers/hid/hid-lg-g15.c
- PadForge's monochrome screen report documentation:
  https://padforge.org/docs/reference/keyboard-screens-internals/

Future work after physical confirmation: show selected profile,
time and macro state on the LCD, then allow per-profile backlight
colours with optional apply-on-profile-switch.


### M-key LED readback and coexistence with Logitech Gaming Software

The LCD test pattern, blank frame and RGB backlight have been verified on
real G13 hardware. The M-key indicator LEDs still need testing without
Logitech Gaming Software (LGS) running. When you press M1/M2/M3, LGS can
restore its own LCD picture and change the indicator state.

Quit LGS fully via its **system tray** menu (closing its main window
is not sufficient), then open **LCD / backlight...**. Select M1 only and
press **Apply M LEDs**: the window shows the requested mask and attempts
to read back the device's current 5-byte LED feature report (ID 0x05).
**Read LEDs** queries the same report without writing anything, updates
the four checkbox values and displays the mask in hexadecimal.

Readback success only proves what the device reports at that instant.
A mismatch after writing may indicate another program, such as LGS,
replaced the state, or that an HID collection was selected incorrectly.
If Windows refuses the read command, the dialog distinguishes
"write succeeded, readback unavailable" from an actual write failure.

The checkbox controls do **not** automatically write reports; press
**Apply M LEDs** to send changes. The first G13 LCD and RGB
implementations remain unchanged.


## Macro repeat modes and Delay actions

Macro Editor now includes **Repeat** next to Macro type:

- **Once** (default for existing macros): execute a single pass for each new G-key press.
- **WhileHeld**: repeat the macro until its assigned G-key is released.
- **Toggle**: one press starts repeating, release does not stop it; the next press of the *same physical G-key* stops it.

Only one macro can run at a time. A running loop stops if forwarding
stops, the active profile changes, or its required release/second press
is detected. Each pass releases macro-held keys/mouse buttons before
repeating and has a minimum 25ms repeat gap to avoid uncontrolled
zero-delay loops. A new macro start while another is already running is
ignored rather than queued. **Test macro...** runs one pass, even for
looped macros, so preview cannot start an unattended infinite loop.

**Delay** sequence steps have a blank, disabled **Key / mouse button**
cell; their duration is **Delay after (ms)**. Changing another step
into Delay clears its old parameter. Changing it back permits parameter
editing. Copy/paste, step reorder and recording insertion retain this rule.
These editor changes do not affect the standalone hardware macro
profile protocol; they are supported by software forwarding.


### Macro step parameter editor

In **Macro Editor**, the **Key / mouse button** column adapts to
the selected Action:

- **MouseDown / MouseUp / MouseClick / MouseDoubleClick:** a dropdown
  with **Left**, **Right**, and **Middle**. You cannot enter arbitrary
  mouse-button text. Switching between mouse actions preserves the
  selected button.
- **KeyDown / KeyUp / KeyPress:** the existing editable key-name field.
  Switching from mouse to keyboard clears the mouse-button value so a
  button such as **Left** cannot accidentally become the keyboard's
  left-arrow key.
- **Delay / WheelUp / WheelDown:** blank and read-only, since these actions
  do not require a key or mouse-button argument.

When changing a keyboard step to a mouse step, select the intended
mouse button from the dropdown. The field starts blank deliberately;
saving an incomplete mouse step is rejected. Existing mouse actions
show their saved button, including from recorded and pasted sequences.


## Extended mouse buttons and live hardware display

**XButton1** and **XButton2** (the mouse back/forward thumb buttons)
are now available in the macro editor's mouse button dropdown, recorder
and playback engine. The Windows low-level mouse hook distinguishes the
two buttons from `MSLLHOOKSTRUCT.mouseData`; playback generates
`MOUSEEVENTF_XDOWN`/`MOUSEEVENTF_XUP` with the correct
`MOUSEINPUT.mouseData`. A recording stopped with an X button held
produces the corresponding MouseUp. The main G-key mapping's
**Output action** selector also now offers `MouseXButton1` and
`MouseXButton2`. These use normal Windows SendInput forwarding, subject
to the same game/input restrictions as other mouse buttons.

**Use profile RGB** (default off) saves a colour with each profile.
Click **RGB colour...** to select its colour, then **Save profile**.
If enabled, G13Reborn applies that profile's colour once when forwarding
starts and when automatic executable switching activates that profile.
A failed colour command is reported without stopping normal forwarding.
Colour changes are sent to hardware only when this setting is enabled
and forwarding is started/switched.

**Show LCD status** (default off) is a global setting saved in the
profiles file. When enabled, while forwarding is active, G13Reborn
updates the 160x43 LCD approximately once per second to display
its current local clock, active profile name, and executing macro name.
The text is truncated to fit the display. If the LCD write fails,
status output is suspended until the next forwarding start, while
keyboard, mouse and vJoy forwarding continue unaffected.

Both options use the previously tested G13 HID output reports and are
opt-in to avoid interfering with Logitech Gaming Software or an LCD
applet. Settings are host-side profile preferences; they are not writes
to the G13's persistent onboard memory.


## M1/M2/M3 mapping layers

Each G13Reborn profile now has an **Enable M1/M2/M3 layers** checkbox
(default off for all previously saved profiles). When enabled, use
**Edit mapping layer: M1 / M2 / M3** above the mapping grid to assign
different G-key keyboard, mouse, vJoy and macro outputs for each layer.
The M1 mapping is the existing profile mapping. The first time layers
are enabled, M2 and M3 start as copies of M1 to preserve familiar
assignments. Subsequent changes to M2/M3 are independent and persist
with **Save profile**. Clone profile deep-copies all three layers.

During forwarding, physical M1/M2/M3 button presses select the
corresponding mapping layer; releases do not switch layers. In this
mode, M1/M2/M3 are reserved and cannot themselves be bound to a
key/mouse/vJoy output. On switching, the previous mapping releases all
held keyboard/mouse/vJoy buttons and cancels any active/looping macro.
Any other G-key already held during switching must be physically
released before it can trigger its new-layer mapping. The G13's
M1/M2/M3 indicator LEDs track the selected layer, using the confirmed
0x05 feature report and preserving MR state when readback is available.

Switching foreground-application profiles resets the layer to M1
and prevents held M keys from activating layers until released.
Profiles with this feature off retain their existing M-key behaviour
and mapping data. The LCD active-profile line displays M1/M2/M3
while the feature is enabled. This is host-side layer selection and
does not write layer mappings to the G13's onboard memory.


## Onboard-profile investigation — read-only first

The original G13 was advertised by Logitech as having space for up to
five onboard profiles, but **we do not yet know a verified USB read
protocol for those stored configurations**. The familiar G13 feature
reports `0x05` and `0x07` expose live M-key LED and RGB states, not
the contents of those profiles. Logitech's generic HID++ protocols for
modern mice must **not** be assumed to work on the old G13.

In **LCD / backlight...** there are now two separate, explicit actions:

- **Save read-only HID snapshot...** produces a JSON file containing
  the G13's HID collection paths, VID/PID/version, input/output/feature
  report lengths, usage and usage-page values, and the results of
  **GET-only** requests for feature IDs `0x05` and `0x07`.
  Unsupported reads are recorded as errors. **No HID feature SET,
  output report or unknown report-ID probe is sent.** This snapshot
  is **not** an onboard profile dump, and cannot reveal key assignments.
- **Backup local profiles (ZIP)...** archives the host's
  `%LOCALAPPDATA%\G13Reborn\profiles.json` (and `.bak`)
  and locally stored LGS profiles in
  `%LOCALAPPDATA%\Logitech\Logitech Gaming Software\Profiles`.
  The ZIP includes a scope manifest that explicitly says it is a
  **PC file backup**, not onboard storage. Files are copied without
  altering the originals. Unavailable locations are simply omitted,
  and file-size/count limits prevent accidentally archiving enormous
  directories. Only locations you choose through the Save dialog are
  written.

These operations are meant to establish a safe baseline before any
attempt to reverse-engineer persistent onboard profiles. If you
share a snapshot or local backup, review it first: HID paths may
identify a computer and profile files can contain personal game
paths and macro text. **No on-device memory writes are implemented.**

Primary sources: Logitech's 2008 G13 announcement documents the
onboard-profile claim; Linux `hid-lg-g15` documents G13's known
runtime LED report IDs but not persistent profile access.


## Input source diagnostic: LGS virtual HID versus G13Reborn SendInput

Open **Input source...** in the main G13Reborn window. This is a
separate, **read-only** Raw Input monitor and does not require the
G13Reborn input-forwarding service to be started. It can also run
while forwarding is active.

Click **Start capture** to register for the Windows generic keyboard
HID usage (0x01/0x06). It listens in the background using
RIDEV_INPUTSINK without disabling or suppressing Windows' ordinary
input. The display records **only the K virtual key** (press and
release), timestamp, descriptive device category, and full Raw Input
device path. All other keys are discarded. It neither saves events
to disk nor injects or changes any keyboard/mouse input.
**Stop capture** and closing the window unregister the listener.

To compare input paths, use the same K key in three distinct runs:

1. Press K on a physical keyboard. Expect a keyboard HID path.
2. With LGS configured to output K, press the assigned G13 key.
   If the Logitech virtual keyboard generates the report, the
   Raw Input device path may show VID_046D&PID_C232.
3. With LGS stopped and G13Reborn mapping a G13 key to K, press
   that key. Ordinary SendInput inserts a synthesized event into
   the Windows input stream, not a HID report, so it is not expected
   to appear as a hardware-origin Raw Input event.

The Logitech virtual keyboard PID C232 is shared by LGS and G HUB;
**the VID/PID alone cannot identify which virtual bus generated the
event**. Full raw device paths allow correlation with Windows
Device Manager and the PnP instances. You can click **Copy results**
to copy the displayed table to the clipboard; no automatic export
is performed. Review device paths before sharing them because they
may include machine-specific identifiers.

A *missing* Raw Input K event is informative but not definitive:
the intended application could receive a synthetic keystroke through
a different Windows input path. This monitor does not attribute
GetMessage/WM_KEYDOWN/SendInput origin or determine anti-cheat
compatibility. It is a narrow source-identification diagnostic and
does not install any drivers.


## Experimental virtual HID keyboard backend (source-stage only)

Our Windows Raw Input testing confirmed a difference between LGS and
G13Reborn: an LGS G-key mapped to K produces input from Logitech's
virtual HID keyboard (PID C232); G13Reborn's current SendInput
backend produces K events without a Raw Input device handle.

An independent, experimental Windows KMDF/VHF keyboard source project
is now in [drivers/G13RebornVhid](drivers/G13RebornVhid/README.md).
A separate C# project, `src/G13Reborn.VhfProbe`, encodes the binary
keyboard reports and exposes two explicit actions: `--probe` opens
the control device without injecting input, and `--test-k` requests
one K press and release. The latter is intended ONLY for later
testing in an isolated Windows virtual machine.

**The native WDK compilation now passes in GitHub Actions.** The
separate `Experimental VHF driver build (no installation)` workflow
restores the official Microsoft WDK/SDK 28000.2526 packages and
successfully produces an experimental `G13RebornVhid.sys` kernel
driver binary. The build does not sign it, validate or package the
draft INF, install it, or execute it in a Windows kernel.

The existing G13Reborn WinForms application and default SendInput
behaviour remain unchanged. The normal Windows CI separately builds
and tests the .NET VHF probe and wire-format protocol.

On the main computer, the following is safe and will only build C#
or show an expected device-not-found error:

```powershell
dotnet build G13Reborn.slnx -c Release
dotnet run --project src/G13Reborn.VhfProbe -- --probe
```

Do not enable Windows driver test-signing or alter Secure Boot on the
primary gaming PC to run this prototype. See the native project
notes for the build, security and verification requirements.
