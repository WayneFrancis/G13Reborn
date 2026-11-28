# G13Reborn — development status and handover

Last updated: 2026-11-27 (Adelaide local date).
Repository: https://github.com/WayneFrancis/G13Reborn
Current development branch: `feature/k8850-macropad`.
Latest functional commit at this update: `963374813727b2091451c1541a5cf2dc8830d1cd`.
This document describes this branch, not necessarily main.

## Instructions for future development sessions

Read this file, README.md, relevant source and recent Git history before making changes. Treat source and actual test results as authoritative; older README paragraphs may describe superseded limitations. Confirm the active branch and working tree. Do not assume a successful commit means compilation or hardware verification. Update this file and commit it alongside every significant development change, recording date, branch, decisions, files, tests actually performed, regressions, remaining work and next actionable step. Preserve important protocol discoveries and distinguish observed behavior from hypotheses. Do not include private machine-specific HID instance paths, credentials or personal macro contents. Do not merge feature branches into main without user approval. The user explicitly requested this ongoing documentation practice.

## Environment and workflow

- Windows 11 x64; Visual Studio 2026; .NET 10 SDK / Windows desktop workload. User checkout: D:\Dev\G13Reborn.
- C# WinForms configuration application, native Windows HID via P/Invoke, optional signed upstream vJoy driver, Windows SendInput for normal keyboard/mouse forwarding.
- GitHub connector can inspect and commit source directly to a feature branch. In prior chat sessions the local Windows build and physical hardware were unavailable to the assistant; the user ran builds and hardware tests. Do not claim tests not actually executed.
- On current branch, update with:
  ```powershell
  git fetch origin
  git switch feature/k8850-macropad
  git pull --ff-only
  dotnet build G13Reborn.slnx -c Release
  dotnet run --project tests/G13Reborn.SmokeTests -c Release
  dotnet run --project src/G13Reborn.Config
  ```
- Keep G13 and K8850 independent. K8850 writes native device assignments; G13Reborn G13 profiles are host-side software mappings. Neither requires replacing Logitech's physical HID driver.

## Project structure

- src/G13Reborn.Device: HID enumeration, G13 input decoding and hardware output; K8850Protocol.cs, K8850Device.cs and NativeHid.cs under Hid.
- src/G13Reborn.VirtualController: vJoy analog and button output.
- src/G13Reborn.Mapping: profiles, transitions, keyboard parsing, macro playback/recording logic, layers, VHF wire contract.
- src/G13Reborn.Config: primary WinForms application; MainForm.cs, Win32InputSender.cs, InputOutputRouter.cs, MacroEditorDialog.cs, MacroRecorderDialog.cs, MacroTestDialog.cs, G13HardwareDialog.cs, RawInputDiagnosticDialog.cs, K8850Dialog.cs, K8850Profiles.cs.
- src/G13Reborn.Diagnostics: earlier diagnostic application.
- src/G13Reborn.VhfProbe and drivers/G13RebornVhid: isolated experimental virtual keyboard driver/client.
- tests/G13Reborn.SmokeTests: no-hardware regression runner.

## Logitech G13 hardware and completed work

Hardware VID:PID 046D:C21C. G1–G22, analog X/Y thumbstick, two thumb buttons plus stick press, M1/M2/M3/MR, RGB backlight, 160x43 monochrome LCD. User has verified simultaneous G-key detection, thumbstick axes and press, vJoy analog/buttons, and native keyboard forwarding (e.g. G1 to A). Default vJoy buttons 1–22 correspond to G keys and 23–25 to thumb controls. Configure vJoy Device 1 with X/Y and 32 buttons. vJoy DLL retained per process to avoid native window-class registration failure on restart; device itself is released on stop.

Implemented host-side features (see README and source for details):
- Named profiles, clone/delete, executable associations, foreground switching, deadzone 0–30%, invert Y, opt-in vJoy and keyboard/mouse forwarding. Profile path %LOCALAPPDATA%\G13Reborn\profiles.json.
- Held-key/button semantics, shared modifier reference handling, wheel on fresh press, release/reset on stop, disconnect or profile/layer change; suppress already-held controls until released.
- Keyboard chords, mouse left/right/middle/XButton1/XButton2, wheel, vJoy buttons, named macros. Windows Keys enum parser already accepts F13–F24; standard SendInput route supports their virtual-key values. G13 F13–F24 in Minecraft not yet physically verified.
- Text and stepped macros, paced Unicode text (default 25ms), explicit delays, key/mouse down/up/press/click/double-click/wheel, once/while-held/toggle modes, cancellation/release, one macro at a time, minimum repeat gap. Text macro defers ordinary mapped events; discard on cancellation/profile change.
- Visible time-limited macro recorder, insertion into existing/empty sequence, add after/insert before, multi-row remove/copy/cut/paste, independent KeyDown/KeyUp timing normalization, duplicate/rename/delete with assignment reconciliation, unsaved changes prompts, macro dropdown, context-specific mouse parameter choices, test macro preview with countdown and log. Recording does not capture cursor positions.
- LCD test pattern and clear, RGB control, M-key LED write/readback, optional profile RGB and live LCD status. Physical LCD pattern/clear and RGB verified; user reported M-key LED mask readback verification in earlier development. LGS may overwrite live state.
- Opt-in M1/M2/M3 host mapping layers, M2/M3 initially copy M1; layer switch releases old outputs, cancels macros, updates LEDs; profile switch resets to M1.
- Read-only HID snapshot and local profile ZIP backup; not a dump of onboard memory.

### G13 HID output discoveries

- RGB feature report ID 0x07 with R/G/B bytes; M-key LEDs feature ID 0x05, four flags for M1/M2/M3/MR, readback using same feature report. Feature collection report length at least 5 bytes. Preserve MR where readback available.
- LCD output collection advertises 992 bytes; bitmap starts at offset 32, six vertical 8-row bands, one byte per column. See G13OutputDevice.cs for exact report construction.
- Onboard profiles advertised by Logitech (up to five) but persistent memory protocol remains unknown. LGS/G HUB profile-to-go investigation and Linux EV_KEY observations did not establish a reliable standalone macro playback/readback protocol. Do not confuse live LED/LCD reports with stored profiles or probe arbitrary feature IDs/write onboard memory.
- References: https://github.com/golgote/G13 ; https://github.com/cavefish-dev/g13-driver ; Linux hid-lg-g15 (linked in README); https://padforge.org/docs/reference/keyboard-screens-internals/ .
- LGS may generate duplicate inputs and overwrite LCD/LED state. Exit via tray, not merely close window. Do not run Diagnostics and Config as simultaneous vJoy feeders.

## Experimental VHF driver (separate workstream)

Raw Input diagnostic established LGS K output originates from Logitech virtual keyboard PID C232; SendInput K does not have a hardware Raw Input device handle. C232 alone cannot distinguish LGS from G HUB. Diagnostic captures only K down/up, read-only, no automatic logging to disk. SendInput remains default; subject to UIPI/focus and application acceptance.

Native KMDF/VHF source is separate, administrator-only exclusive control path `\\.\G13RebornVhid`. IOCTL 0x22A004; 16-byte little-endian packet: magic 0x33314856 at 0, version 1 at 4, report length 8 at 6, boot-format keyboard report at 8 (modifier, reserved zero, six usages). Descriptor and C# protocol currently restrict usages to 0..0x65; F13–F24 exceed this. Close releases keys. Probe --probe opens without injection; --test-k sends K down/up only with explicit opt-in.

Native WDK CI compilation passed per driver README; official InfVerif and Inf2Cat static signability checks for Windows 11 24H2/25H2 x64 also documented as passed (not independently rerun in this session). WDK/SDK 10.0.28000.2526; x86 WDK package supplies Inf2Cat host tool. See drivers/G13RebornVhid/README.md and workflow logs; branch feature/vhf-inf2cat-discovery exists. No signed package or live kernel validation established. Do not install on primary PC, change Secure Boot or enable test-signing. Next VHF steps: reviewed isolated VM deployment, verify Raw Input device, release on close/crash and disconnect, then consider integration and descriptor extension. Do not conflate VHF with working K8850 native HID.

## K8850 macropad — current active workstream

### Hardware identification

Generic/unbranded 16-key (4x4), three rotary encoders with push switches (two small knobs and one larger knob on right), translucent acrylic case. 25 programmable inputs total. Exact retail brand and MCU not confirmed; compatible with CH57x/K8850 protocol. USB VID 0x514C PID 0x8850. MI_00 vendor-defined configuration HID; MI_01 composite input collections COL01 keyboard, COL02 mouse, COL03 keyboard, COL04 consumer control. User observed Windows HID output report length **65 bytes**. No extra drivers or UsbDk needed for verified writes.

### Protocol source and decisions

References:
- https://github.com/kriomant/ch57x-keyboard-tool (src/keyboard/k8850_4x4.rs, src/keyboard/mod.rs, src/main.rs)
- https://github.com/jvspier/macropad-rebind (docs/PROTOCOL.md)
- Prior source inspection of CH57x tool found no obvious malicious code in inspected files; not a comprehensive audit of dependencies, release binaries, CI or firmware. Implement protocol natively rather than distribute external executable.
- Raw USB packet length 64 bytes INCLUDING report ID 0x03. Windows HID API may advertise 64 or 65; tested device advertises 65. Pad raw packet at end, do not prepend another report ID.
- NativeHid.OpenK8850Output restricts path to VID/PID/MI_00, opens overlapped write handle. Discover checks vendor usage page >=0xFF00. UploadAsync re-discovers selected interface, validates report header/length, writes binding and finish via asynchronous FileStream. Explicit write only, 5s timeout per binding in dialog.

### Wire format (zero-based byte offsets, hexadecimal unless specified)

All reports zero-filled except specified fields. Header: [03 FD slot layer kind]. Slot 1–16 keys; encoder slot = 17 + 3*(encoder index minus 1) + action (CCW=0, Press=1, CW=2). Encoder slots 17–25. Layer 1–16 accepted by software; actual supported layer count and switching mechanism not yet verified. Kind 01 keyboard, 02 consumer/media, 03 mouse.
- Keyboard: byte 6 = token count (1–18); tokens at 9+3*i. Modifier tokens F1 left Ctrl, F2 left Shift, F3 left Alt, F4 left Win, F5 right Ctrl, F6 right Shift, F7 right Alt, F8 right Win. These are hexadecimal protocol tokens, NOT function key names. A=04, F12=45, F13=68, F24=73.
- Media: byte 6=02; byte 9 usage low, byte 12 usage high. Known upstream values: brightness up 006F/down 0070, next B5, previous B6, stop B7, play/pause CD, mute E2, volume up E9/down EA, favorites 0182, calculator 0192, screen lock 019E, browser home 0223/back 0224/forward 0225. Current UI implements playback and volume subset.
- Mouse: byte 5=01, byte 6=04, byte 9 modifier (0 or F1–F3), byte 12 button mask (1/2/4), byte 15 signed dx, byte 18 signed dy, byte 21 signed wheel. Exactly one action type; drag not supported. UI exposes three clicks and wheel up/down.
- Finish: [03 FD FE FF] padded to report length, sent after EACH binding.
- No verified assignment readback or LED configuration protocol. Never infer existing assignments from a successful write or local profile.

### Implemented K8850 features and commits

- 002be636: K8850Protocol.cs packet construction.
- 39618691: NativeHid restricted output open.
- 17043609: K8850Device discovery/upload.
- 3ab33db2 / 1d6c7035: experimental dialog and main toolbar button.
- acf9a9f9 / 2250e636: independent profiles and selective uploads.
- 4e884ec3: categorized dropdowns and keyboard shortcut capture.
- 43f39621 / 76b5ad28: F13–F24 named mappings and raw hex normalization.
- 4daf8e37 / 96337481: extended keyboard category and named HID usages.

Dialog: K8850 Macropad... on main toolbar; device refresh, 4x4 key layout and nine encoder actions, layer/slot selection, profile new/clone/rename/delete/save, category and action editor, capture shortcut, forget local assignment, upload selected/modified/all configured bindings. Profile file %LOCALAPPDATA%\G13Reborn\k8850-profiles.json. Keys indexed by layer:slot. LastSent indexed by device path and binding key, shared across profiles to compare desired mapping against last successful transmission. Unknown bindings never written automatically. Save/close persists locally; upload is explicit with confirmation. [sent] means reports sent, NOT verified device state. * indicates different from last sent. Forget local assignment does not clear physical key. Existing profiles remain compatible.

Keyboard editor supports named chords (Ctrl+C), comma-separated sequences, raw hex tokens, standard keys and F1–F24. Advanced / hex category now automatically stores prefix hex: while displaying raw tokens. Earlier bug: entering 68 without prefix caused Unknown key: 68 before USB transmission; old assignment remained. Do not misdiagnose this as firmware rejection. Extended category includes 65 named choices: F13–F24, numpad digits/operators/Enter/decimal/equals, Num Lock, Print Screen, Scroll Lock, Pause, Insert, Non-US Backslash, Application/Menu, Execute/Help/Menu/Select/Stop/Again/Undo/Cut/Copy/Paste/Find, International 1–9 (87–8F), Language 1–9 (90–98). F13–F24 also remain in normal Keyboard dropdown. OS/game recognition of nonstandard usages is experimental.

### Verified test results and evidence boundaries

- User ran Config build and existing SmokeTests after initial K8850 protocol/device commits; build succeeded (reported 8.4s), all existing smoke tests passed, including VHF wire-format tests. This was before subsequent GUI/profile commits; not a current full regression run or dedicated K8850 packet test.
- User confirmed discovery, 65-byte report length and initial key programming test (steps 3–6) worked.
- User confirmed profile-management version worked; categorized assignment editor improved usability. No individual exhaustive test log for every media/mouse/profile operation.
- Earlier raw 68 attempt retained D/L; PowerShell reported D=68 decimal or L=76 decimal, NOT HID usage 68 hex. Status Unknown key 68 confirmed parser failure. Fixed by automatic hex prefix and named F13–F24.
- User explicitly confirmed Minecraft ATM10 detects F13 programmed via hex and F24 via named Keyboard option. This verifies endpoints of extended function-key range and native hardware output, not every intermediate key or every combination.
- User subsequently confirmed International 1 (HID 0x87) is detected in Minecraft ATM10 as scancode.115 (decimal). This is a raw scancode binding candidate; distinctness, persistence and actual action activation remain to be tested. International 6 (HID 0x8C) is detected as Minecraft scancode.92 (decimal); PowerShell Console.ReadKey reports Key: 234 | Code: 234 | Modifiers: 0 (Windows virtual key 0xEA). International 9 (HID 0x8F) produces no detected event in either Minecraft or the PowerShell test. This does not establish whether firmware or Windows translation is responsible. Other international/language keys have no individually reported results yet. Extended-category hardware testing has therefore begun; no new full build/smoke-test log supplied.
- No dedicated automated K8850 tests added yet. No current CI result independently checked.

## Current position / next steps

1. User to pull current feature branch, build and run smoke tests; verify extended category and old saved profiles. Check F13–F24, numpad (Num Lock state), Application/Menu, then international/language/editing usages in ATM10. Record exact detected names or no event; distinguish Windows recognition from firmware acceptance. Use disposable target for editing/system keys.
2. Add automated packet tests: 64-byte header, all 25 slots, layer boundaries, token placement/count, F12/F13/F24, media high byte, signed mouse data, finish and Windows padding. Add parser tests for named chords, hex normalization and all extended choices; consider moving parser to a non-UI testable class. Run full regression suite.
3. Improve UI validation: validate draft before upload, distinguish parse/transport/persistence failures, preserve previous assignment on invalid edit, highlight selected key, show current configured binding even while browsing categories. Shortcut capture for keypad/system keys needs alias mapping (WinForms names differ); Windows-reserved combinations may not be capturable. Advanced raw codes are not guaranteed supported.
4. Investigate actual K8850 layer count and switching, explicit clear/disable, media extras, LED controls and readback only with documented/observed protocol; no blind writes. Consider per-device profile association, stale LastSent state after external reprogramming and partial-upload reporting. Unknown assignments cannot be backed up from device yet.
5. Test G13 F13–F24 via normal SendInput in Minecraft separately. Do not expand VHF descriptor without matching native and managed validation/tests and reviewed VM workflow.
6. Keep main unchanged until user approves integration; review branch divergence and CI before PR/merge. Maintain this document with significant changes.

## Useful diagnostic commands

PowerShell keyboard check (Escape exits):
```powershell
while ($true) {
    $key = [Console]::ReadKey($true)
    Write-Host "Key: $($key.Key) | Code: $([int]$key.Key) | Modifiers: $($key.Modifiers)"
    if ($key.Key -eq 'Escape') { break }
}
```
Expected F13 Windows ConsoleKey decimal 124, F24 decimal 135. USB usage and Windows virtual key are different numbering systems. Minecraft's own key binding UI is the relevant final acceptance test for ATM10.

## Maintenance log

- 2026-11-27: Initial consolidated handover added on K8850 feature branch. Includes prior G13 work, VHF safety boundaries, K8850 protocol and verified hardware milestones, extended-key work and outstanding tests. Historical details reconstructed from development conversation and repository documentation; not a new independent test run.

- 2026-11-27: Recorded International 1 -> Minecraft scancode.115 observation; no source changes.

- 2026-11-27: Recorded International 6 (Minecraft scancode.92, Windows VK 234) and International 9 (no detected event in either test). No source changes.

## Complete international-key detection matrix (2026-11-27)

User tested all nine international usages on K8850 in ATM10 and PowerShell Console.ReadKey. This supersedes the earlier partial observations.

| International | HID usage (hex) | Minecraft | ConsoleKey/VK decimal |
|---|---|---|---|
| 1 | 87 | scancode.115 | 193 |
| 2 | 88 | scancode.112 | No response |
| 3 | 89 | scancode.125 | No response |
| 4 | 8A | scancode.121 | No response |
| 5 | 8B | scancode.123 | 235 |
| 6 | 8C | scancode.92 | 234 |
| 7 | 8D | No response | No response |
| 8 | 8E | No response | No response |
| 9 | 8F | No response | No response |

All three reported ConsoleKey events had Modifiers: 0. Six distinct Minecraft scancodes detected; actual action activation and persistence still unverified. Console.ReadKey misses International 2–4 despite Minecraft recognition; do not use console detection alone as a compatibility criterion. International 7–9 failure location remains unknown. Next test Language 1–9. Consider a verified ATM10-compatible category or compatibility annotations after testing. F13/F24 endpoints plus six international keys detected; intermediate F14–F23 remain individually unverified. No source changes or new automated test run.

## Complete language-key detection matrix (2026-11-27)

| Language | HID usage (hex) | Minecraft ATM10 | PowerShell Console.ReadKey |
|---|---|---|---|
| 1 | 90 | Not bound | No response |
| 2 | 91 | Not bound | No response |
| 3 | 92 | scancode.120 | No response |
| 4 | 93 | scancode.119 | No response |
| 5 | 94 | F24 | F24, decimal 135, Modifiers 0 |
| 6 | 95 | No response | No response |
| 7 | 96 | No response | No response |
| 8 | 97 | No response | No response |
| 9 | 98 | No response | No response |

Language 1–2 change the binding to Not bound, distinct from no detected keypress on 6–9. Language 5 duplicates F24 and is not an independent binding. Language 3–4 add two unique scancodes to the six international scancodes. Potential gaming set: F13–F24 plus International 1–6 plus Language 3–4 = 20 distinct candidates; only F13/F24 endpoints individually verified so far, giving ten individually observed distinct candidates. Actual action activation and persistence still unverified. Next: test F14–F23 and action activation, then add Gaming / Extended keys category with verified names/scancodes, retaining experimental choices separately. No application changes or automated test run in this documentation update.

## Complete extended function-key detection (2026-11-27)

User verified Minecraft ATM10 correctly detects every F13–F24 assignment (HID 0x68–0x73). PowerShell Console.ReadKey detected F13, F14, F15, F18, F19, F20, F21, F23, F24 (VK decimal 124,125,126,129,130,131,132,134,135 respectively; modifiers zero); no output for F16, F17, F22. This supersedes earlier endpoint-only results. Combined with International 1–6 and Language 3–4, 20 distinct additional key identities are now observed in Minecraft's binding UI. Language 5 duplicates F24. Console detects only 12 distinct identities across this set. Actual bound-action activation, simultaneous use and persistence after restart remain unverified. Next implementation: Gaming / Extended keys category with these 20 verified detection choices, preserving experimental extended keys and stored binding compatibility. No application code changes or automated test run in this update.

## Gaming category implementation

Commit 1dfddfbb adds Gaming / Extended keys to K8850Dialog.cs. Fixed dropdown contains F13–F24, International 1–6 and Language 3–4; eight scancode labels reflect user tests. Selection stores canonical existing binding names, not annotated labels; existing matching profiles reopen in Gaming category. TextChanged ignores gaming dropdown labels; SelectionChangeCommitted maps label index to canonical name. Browsing category does not overwrite assignments. Existing Keyboard and Extended keyboard options retained; no protocol/parser changes. Capture disabled for gaming fixed list. Pending local build and regression tests: select each gaming entry, switch slots/categories, save/reopen profile, upload and confirm Minecraft action activation and restart persistence. No local compilation or hardware tests performed by assistant.

## Gaming category user verification

2026-11-27: User reported 'That all works' after the gaming category implementation and requested selection, slot switching and Minecraft upload checks. Treat the feature as user-tested successfully; no separate detailed per-case log, automated test run, or explicit game-restart/action-activation result supplied. Next priority: automated K8850 protocol and binding encoder regression tests, followed by remaining UI and hardware improvements.

## K8850 automated regression coverage

Commits cdf3f6e and ea60013 add hardware-free K8850 tests to existing G13Reborn.SmokeTests. The test project links the actual K8850Profiles.cs source to exercise the internal binding encoder without referencing the WinForms executable. Tests cover exact 64-byte packets and zero padding, finish, button/knob slots, modifier chords, multimedia including high-byte usage, mouse buttons/wheel, F1–F24, International/Language 1–9, hex equivalence, sequences, maximum tokens and rejection of invalid arguments. These test protocol encoding, not Windows recognition or hardware delivery. Run `dotnet run --project tests/G13Reborn.SmokeTests`. Compilation and execution pending user verification; assistant has not run them. No production behavior changed. Next: gaming profile generator after test verification.

## Multimedia Minecraft compatibility observation

User reports Previous Track and Next Track both appear as scancode.256 in Minecraft. Existing encoder uses distinct standard Consumer Page usages Previous 0xB6 and Next 0xB5, so no mapping collision is apparent in source. Actual firmware output and Windows media action behavior remain unverified; Minecraft/GLFW input translation is a possible cause, not confirmed. Test with a media player containing multiple tracks, or inspect Windows consumer input events, before changing protocol. Do not count these as independent Minecraft bindings. No production code changes.

## Modifier checkbox UI and multimedia verification

User confirmed Previous Track and Next Track both work correctly in Spotify, despite both appearing as scancode.256 in Minecraft. Keep distinct consumer usages unchanged; collision is game/input-path specific in observed tests.

Commit e844d6b adds Ctrl, Shift, Alt, Win and right-side RCtrl/RShift/RAlt/RWin checkboxes to Keyboard, Extended keyboard and Gaming / Extended keys. Existing single-key chords are split into base key and modifier state when reopened; stored values remain canonical chord strings. Gaming scancode labels are not persisted. Raw hex, mouse and media categories disable modifiers. Manual shortcut/sequence text remains supported with checkboxes cleared; combining manual compound text with checked modifiers is rejected in the editor. Browsing categories resets editor controls without erasing stored binding. No protocol changes. Pending build and user tests: checkbox toggle, gaming key selection, slot switching, save/reopen, upload, actual modifier delivery and Minecraft chord recognition. Assistant has not compiled or hardware-tested this change; prior regression suite execution still not reported.

## Modifier hardware verification

User confirmed modifiers work with function keys and international keys. Minecraft also triggers actions independently bound to Left Shift when Shift is used as a chord modifier. Treat this as observed game input behavior, not a K8850 encoding defect. Prefer unmodified gaming keys and avoid modifiers with conflicting standalone game actions. Modifier checkbox feature is user-tested for these key families; exhaustive left/right modifier combinations, language-key chords, persistence and automated regression execution are not separately confirmed. Do not assume 160 independently usable Minecraft chords. No production code changes. Next proposed feature: gaming profile generator with 20 verified unmodified key identities and explicit review before upload.

## Gaming profile generator

Commit c944649 adds Generate gaming profile... to K8850 dialog. Creates a uniquely named new local profile on chosen layer, default 20 unmodified verified Minecraft key identities in slot order (F13–24, International 1–6, Language 3–4). Remaining slots 21–25 unknown by default; optionally choose one of eight left/right modifiers to fill them with modifier+F13–F17. No modifier selected by default; game chord independence not guaranteed. Preview, cancel and name validation included. All generated bindings pre-encoded before adding profile. No hardware upload or existing profile overwrite. Existing unknown hardware assignments remain unchanged. Pending compile and user verification: create/cancel/duplicate name, target layer, 20/25 assignment counts, save/reopen and explicit upload. Assistant has not compiled or executed tests. Next: assignment validation and duplicate warnings.

## Window placement persistence

Commits f03cc96, b400e8e, b80d66d add shared WindowPlacement helper and attach it to MainForm and K8850Dialog. Separate local JSON files main-window.json and k8850-window.json under %LOCALAPPDATA%/G13Reborn store normal bounds and maximized state on FormClosed. Minimized state is not restored. Negative coordinates supported; restore only if a usable portion of top edge intersects a current monitor working area, otherwise retain default centering. Optional settings I/O failures do not block startup/shutdown. Other dialogs unchanged. Pending user compile and verification: move both windows across monitors, resize, close/reopen, maximize, disconnect monitor and verify fallback. No local compile/test performed.

## Profile diagnostics and portability

User confirmed window placement works (specific monitor/maximize cases not separately reported). Commits 49b96d0, 609fa86, 9ea3a25 add Validate profile, Export profile, Import profile and toolbar wrapping. Validation encodes each local binding, groups identical packet bytes ignoring slot but retaining layer, reports invalid bindings and observed Minecraft aliases F24/Language 5 and Previous/Next Track. Aliases are warnings, not upload blockers; no claim to detect all game conflicts. Export serializes only active profile, not LastSent/device state. Import checks name, bindings, canonical layer:slot and encoding, creates unique name with numeric suffix, confirms before local save; no hardware writes. Pending compile and user verification: duplicates including named/hex equivalents, aliases, invalid input, cross-layer duplicates not warned, export/import roundtrip and malformed file rejection. Assistant has not compiled. Next possible work: bulk assignment editing, keyboard punctuation coverage, profile validation integrated with upload review.

## Hardware layer investigation

Public reverse engineering of related 4x4/3-knob FD device: https://github.com/kriomant/ch57x-keyboard-tool/issues/153 (yawor, Oct 31 2025) reports vendor app readback of 25 assignments on each of three layers. VID/PID reused across different hardware; this does not verify our device's layer count or readback command. https://github.com/kriomant/ch57x-keyboard-tool/blob/master/example-mapping.yaml documents physical side button and temporary LED indication on other CH57x models; README notes some lack layer switching. No verified active-layer software command for our model. Prior 1–16 range is an application addressing allowance, not proven capacity. Commit 30e9291 renames selector Edit layer, adds Layer help / test instructions and warns on uploads targeting non-layer-one. No speculative HID commands, no reduction of existing range or profile migration. Manual test: export profile (local state only), use one known restorable slot, assign distinct harmless key on layer 2, upload selected only, observe in Notepad before/after any clearly marked layer control; restore known mapping if overwritten. No automatic writes. User hardware tests and compile pending. Readback and layer activation remain unresolved.

## Three hardware layers verified; RGB and switch research

User confirmed physical button cycles layers 1–3; fourth layer did not work. Additional physical switch has no observed effect during wired use; acrylic housing has coloured LED. Independent layer colours suspected, not verified. Three layers / 75 addressable actions now replace prior experimental 16-layer expectation for this unit. Commit 967a59e restricts both edit and generator selectors to 1–3, updates help and upload confirmation, flags legacy layer >3 in validation and blocks any upload batch containing those layers before any hardware write. Legacy data preserved in JSON/export; protocol encoder still accepts 1–16 for research and existing tests. Old unsupported entries can be removed from exported JSON and reimported as a new profile. No local compile performed.

LED research: upstream exact 4x4 FD binding implementation at https://github.com/kriomant/ch57x-keyboard-tool/blob/master/src/keyboard/k8850_4x4.rs still rejects LED configuration. Fork commit https://github.com/skarard/ch57x-keyboard-tool/commit/1e6dd9c implements another K8850 variant, with K884x binding dialect, INIT 03 FB FB FB then 03 FE B0 <zero-based layer> <mode> <RGB> <16 RGB triples>, padded to 64. Different protocol despite shared VID/PID: not established compatible with user's FD dialect. https://github.com/ciiiii/knobctl/blob/master/docs/protocol.md documents another FD but different binding offsets and RGB effect-only command. Do not issue these commands to user's device without stronger matching evidence or explicit controlled test. Issue https://github.com/kriomant/ch57x-keyboard-tool/issues/153 reports wired, dedicated wireless dongle and Bluetooth on related 4x4 device, with F13–F24 not functioning via Bluetooth in reported tests. Switch could be wireless power/mode, unverified. Suggested non-writing test: unplug USB, toggle switch and check LED/Bluetooth discovery; check for dongle and markings. Next: capture matching vendor LED traffic (prefer USB capture without running untrusted software on main PC), implement per-layer RGB only after protocol established; add layer names and copy/swap functionality.

## Named layers, action labels and local assignment editing

User confirmed three-layer UI update works. Commits c9892db and 67d95ee add per-profile LayerNames (integer layer key) and Labels (canonical layer:slot key), both optional dictionaries with empty defaults for backward compatibility. Name and action label edit fields persist through normal profile save/close, clone and JSON export/import. Button captions show label plus encoded assignment and sent/dirty state; hover tooltip shows full text. Copy takes snapshot of selected configured binding and label; paste works across profiles/layers; swap uses current values of two configured slots within same profile only, including across layers, with confirmation for replacement. Forget local assignment also clears its label; does not clear hardware. All actions are local only and maintain LastSent tracking by physical address, never write implicitly. Fixed literal backslash newline sequences in generator/validation text. Pending compile and user tests: labels restored after close, named layer changes, clone/import, paste/swap, upload modified; no local compiler/hardware run. RGB remains unresolved; next useful feature layer duplication and upload preview. No speculative RGB commands added.

## Layer copying, upload review and Minecraft keybinding tooltips

User asked whether ATM10 bindings can be read for matching tooltips and requested continued features. Commit cf707d9 adds Copy layer: source current edit layer, target one of other two, default fills only unassigned local destination slots, optional replace overlapping known assignments with confirmation, optional layer name copy. Copies labels and validates every planned binding before editing; unknown source slots never erase destination values. Upload confirmation replaced with table showing physical layer:slot, label, last sent local history (not readback), new assignment; cancel before any writes. No automatic hardware changes.

Minecraft integration commits 29c76ed, 3c9525a, 3412249: read-only options.txt selection and refresh, path saved locally in K8850Profiles (not exported profile), global across K8850 profiles, auto reload when dialog opens, clear link. MinecraftBindings parser reads key_ lines, recognizes key.keyboard, key.mouse and scancode (also key.scancode prefix), NONE/CONTROL/SHIFT/ALT suffix; matches encoded HID reports rather than labels, preserving all exact matches. Known Windows Minecraft scancode mappings for International 1–6, Language 3–4, Language 5/F24 and next/prev scancode.256 included. Single supported modifier and ordinary keyboard/mouse click mappings supported; multi-key sequences, multiple modifiers, Win modifier and unsupported keys not guessed. Tooltips append raw translation IDs; human-readable mod language resources, modifier conflict warnings and per-profile game links remain future work. No Minecraft file modifications or mod JAR execution. Sources: https://docs.neoforged.net/docs/1.21.1/misc/keymappings/ and https://github.com/un0rder/1.21.1-NeoForge_21.1.228/blob/main/options.txt; modifier suffix example https://www.curseforge.com/minecraft/modpacks/sunsetilla/files/7830836. Real user ATM10 options.txt not yet inspected. Smoke test additions 6b38200 and a72ff77 cover synthetic parser and HID matching; no local compile/test. User test: link instance options.txt (not global launcher instance), hover F13/F24/international and modified keys, refresh after game saves, close/reopen, test copy layer and cancel/confirm upload review. Pending: translation resource lookup, real options sample, bulk editing and RGB/readback research.

## Minecraft action descriptions and browser

User confirmed prior Minecraft integration/layer copy/upload preview version compiles and runs; actual ATM10 matching and smoke test results not separately confirmed. Commits 7b02da7 and 838fa2b extend MinecraftBindings.Load to scan adjacent mods/*.jar for assets/*/lang/en_us.json (4 MiB cap per resource, read-only ZIP access, no extraction or code execution). Only IDs present in saved bindings are retained; sorted archive order and first translation wins. Missing translations fall back to IDs; vanilla client JAR, resource packs, nested JARs and other languages are not searched. Corrupt/unreadable archives are counted, unreadable individual resources skipped. Scan is synchronous during link/reload; large packs may briefly block UI. Tooltip descriptions resolved automatically. Browse Minecraft actions lists saved supported bound actions, input signature, matching slots across active profile layers, exact shared-signature count and raw ID; text search and shared/assigned filters. Shared signature is not proof of a game conflict because contexts differ. Suggest action labels previews checkable candidates across profile layers, only empty labels with a single translated match; no automatic overwrite, hardware upload or Minecraft modification. All local names persist on normal save/close. Commit 9bea2ff adds synthetic JAR and translation fallback/shared count tests. No new build or real ATM10 options/JAR testing by assistant. Pending user verification of translated names, search, suggestion checkboxes, existing custom labels preserved; next unused extended key finder, saved modifier conflicts, vanilla language resources, file monitoring and printable layout. RGB/readback unresolved.

## Unused gaming key finder and modifier warnings

Commits 0f1618e and 16dc119 add MinecraftBindings.Inspect and Find unused gaming keys dialog. Uses 20 verified gaming key identities, selectable None/Ctrl/Shift/Alt/RCtrl/RShift/RAlt, default only unused candidates and reserve all profile layers (optional current layer only). Destination excluded from existing reservations. Candidate is unused in saved data only if no exact Minecraft match, no other-modifier same-base Minecraft actions, no standalone physical modifier-key actions, and no other local slot with exact canonical Minecraft signature. Right/left chord modifiers canonicalize to NeoForge CONTROL/SHIFT/ALT but standalone modifier warnings respect sided HID token. This is a conservative saved-data heuristic, not proof of runtime availability: unknown hardware, game contexts, unsupported macros/multi-modifier assignments and unsaved settings are not checked. User can show all candidates and assign one locally with overwrite/conflict confirmation; existing custom label retained, Minecraft options never changed, explicit upload still required. Hover tooltips include same-base and standalone modifier actions. Commit fd568c0 adds hardware-free tests for related chords, left/right Shift, international reservation and unknown sequences. No local compilation/test or real ATM10 data verification; prior translated action browser build not yet confirmed. Next priorities: automatic options refresh without rescanning all JARs unnecessarily, vanilla translations, printable layout, and separate hardware RGB/readback investigation.

## Automatic Minecraft options refresh

Commits 491e1cf, 25e9eea, 2685557 add automatic refresh (enabled by default, persisted as AutoRefreshMinecraft in local store), separate Minecraft status text and snapshot freshness indication. WinForms timer polls length and UTC last-write timestamp every 1500 ms while K8850 dialog is open, skips hardware upload, requires two consecutive matching observations before reload and checks metadata again after reading. File access/change failures retain last successful snapshot, mark tooltips stale, block opening key finder/action browser/label suggestions until fresh, and retry without modal error loops. Closing dialog disposes timer. Manual refresh performs full mod scan; ordinary automatic reload reuses translations for previously seen action IDs including unbound ones, rescans if new IDs appear. Existing snapshots are not mutated. New mod language content with unchanged IDs requires explicit refresh. Polling detects common saves but not changes preserving both size and timestamp; modal child dialogs retain snapshot from opening; no live runtime game state. No Minecraft writes or automatic hardware writes. Initial/full scans and new-ID rescans synchronous. Commit ecfd71d adds hardware-free reload tests proving changed binding and cached previously unbound translation survive removal of source JAR; no local compile/test performed. Pending user testing of prior key finder and translations, current automatic refresh, disabled setting persistence, temporary file failure/recovery. Next: printable layout and vanilla language resources, then drag/drop or macro editor; hardware RGB/readback research remains outstanding.
