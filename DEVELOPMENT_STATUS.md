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
