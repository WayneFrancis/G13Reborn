using G13Reborn.Device;
using G13Reborn.VirtualController;
using G13Reborn.Mapping;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    Console.WriteLine("PASS: " + description);
}

var allKeys = new byte[] { 1, 127, 127, 0xFF, 0xFF, 0x3F, 0, 0 };
Check(G13InputReport.TryParse(allKeys, out var packet) && packet is not null,
    "parse input report");
Check(packet!.PressedGKeys.SequenceEqual(Enumerable.Range(1, 22)),
    "all 22 G keys simultaneously");
Check(!G13InputReport.TryParse(new byte[] { 2, 1, 2, 3, 4, 5, 6, 7 }, out _),
    "reject non-G13 report ID");

var centred = ThumbstickMapper.Map(127, 128);
Check(centred == (0, 0), "center is neutral after deadzone");
var right = ThumbstickMapper.Map(255, 127);
Check(right.X > .99 && Math.Abs(right.Y) < .02, "full-right maps to +X");
var left = ThumbstickMapper.Map(0, 128);
Check(left.X < -.99 && Math.Abs(left.Y) < .02, "full-left maps to -X");
var diagonally = ThumbstickMapper.Map(255, 0);
Check(Math.Sqrt(diagonally.X * diagonally.X + diagonally.Y * diagonally.Y) <= 1.0000001,
    "diagonal movement fits unit circle");
var inverted = ThumbstickMapper.Map(127, 0, invertY: true);
Check(inverted.Y > .99, "invert Y flips direction");
Console.WriteLine("All analog mapping and report parsing smoke tests passed.");

// Physical G13 button mapping: simultaneous keys, shared targets and exclusions.
var map = G13ButtonMappings.CreateDefault();
var twoKeys = new byte[] { 1, 127, 127, 0b0000_0011, 0, 0, 0, 0 };
Check(G13InputReport.TryParse(twoKeys, out var two) && two is not null,
    "two-key packet parses");
Check(two!.IsPhysicalBitPressed(0) && two.IsPhysicalBitPressed(1),
    "raw physical button bit lookup");
Check(map.GetPressedMask(two, 32) == 3u, "two keys map to two vJoy buttons");
Check(map.GetPressedMask(two, 1) == 1u, "unavailable vJoy buttons are ignored");
var duplicateMap = new G13ButtonMappings(
    new Dictionary<int, int> { [0] = 1, [1] = 1 });
Check(duplicateMap.GetPressedMask(two, 32) == 1u, "duplicate mappings are ORed");
var onlySecond = new byte[] { 1, 127, 127, 0b0000_0010, 0, 0, 0, 0 };
G13InputReport.TryParse(onlySecond, out var second);
Check(duplicateMap.GetPressedMask(second!, 32) == 1u,
    "shared virtual button stays pressed while second physical key is held");
Check(new G13ButtonMappings(new Dictionary<int, int>()).GetPressedMask(two, 32) == 0,
    "unmapped buttons do not produce virtual input");
var thumbButtons = new byte[] { 1, 127, 127, 0, 0, 0, 0, 0b0000_1110 };
G13InputReport.TryParse(thumbButtons, out var thumb);
Check(map.GetPressedMask(thumb!, 32) == ((1u << 22) | (1u << 23) | (1u << 24)),
    "three thumb controls map to buttons 23-25");

var stickOnly = new byte[] { 1, 127, 127, 0, 0, 0, 0, 0b0000_1000 };
Check(G13InputReport.TryParse(stickOnly, out var stick) && stick is not null,
    "stick depress packet parses");
Check(stick!.IsPhysicalBitPressed(35), "thumbstick press is physical bit 35");
Check(map.GetAssignment(35) == 25, "default thumbstick press maps to vJoy button 25");
Check(map.GetPressedMask(stick, 32) == (1u << 24), "stick click reaches virtual button 25");
Check(map.GetPressedMask(stick, 24) == 0u, "stick click unavailable if vJoy has only 24 buttons");


// Profiles and keyboard/mouse edge events (these do not invoke SendInput).
var profile = new MappingProfile { Name = "test" };
profile.Buttons[0] = new ButtonAction { Kind = MappingKind.Keyboard, Parameter = "Ctrl+A" };
profile.Buttons[1] = new ButtonAction { Kind = MappingKind.Keyboard, Parameter = "Ctrl+B" };
profile.Buttons[2] = new ButtonAction { Kind = MappingKind.MouseLeft };
profile.Buttons[3] = new ButtonAction { Kind = MappingKind.WheelDown };
profile.Buttons[35] = new ButtonAction { Kind = MappingKind.VJoyButton, Parameter = "25" };
profile.Validate();

var engine = new ButtonTransitionEngine(profile);
G13InputReport.TryParse(new byte[] { 1, 127, 127, 0b0000_0001, 0, 0, 0, 0 }, out var g1);
var firstEvents = engine.Apply(g1!);
Check(firstEvents.SequenceEqual(new[]
{
    new InputEvent(InputEventKind.KeyDown, 0x11),
    new InputEvent(InputEventKind.KeyDown, 0x41)
}), "Ctrl+A emits modifier before A");
Check(engine.Apply(g1).Count == 0, "held key does not auto-repeat");

G13InputReport.TryParse(new byte[] { 1, 127, 127, 0b0000_0011, 0, 0, 0, 0 }, out var g1g2);
Check(engine.Apply(g1g2!).SequenceEqual(
    new[] { new InputEvent(InputEventKind.KeyDown, 0x42) }),
    "Ctrl+B shares existing Ctrl without duplicate key-down");

G13InputReport.TryParse(new byte[] { 1, 127, 127, 0b0000_0010, 0, 0, 0, 0 }, out var g2);
Check(engine.Apply(g2!).SequenceEqual(
    new[] { new InputEvent(InputEventKind.KeyUp, 0x41) }),
    "releasing G1 releases A but retains Ctrl and B");

Check(engine.ReleaseAll().SequenceEqual(new[]
{
    new InputEvent(InputEventKind.KeyUp, 0x42),
    new InputEvent(InputEventKind.KeyUp, 0x11)
}), "shutdown releases main keys before modifiers");

G13InputReport.TryParse(new byte[] { 1, 127, 127, 0b0000_1100, 0, 0, 0, 0 }, out var mouseWheel);
var mixed = engine.Apply(mouseWheel!);
Check(mixed.Contains(new InputEvent(InputEventKind.MouseDown, (int)MouseControl.Left)),
    "mouse down emitted");
Check(mixed.Contains(new InputEvent(InputEventKind.Wheel, -120)),
    "wheel down emitted once");
Check(engine.Apply(mouseWheel!).Count == 0,
    "held wheel button does not scroll repeatedly");
Check(engine.ReleaseAll().Contains(new InputEvent(InputEventKind.MouseUp, (int)MouseControl.Left)),
    "mouse up emitted on shutdown");

Check(profile.GetVirtualButtons(stick!, 32) == 1u << 24,
    "profile forwards stick click to vJoy button 25");

Check(KeyboardChord.Parse("Alt+Shift+F12").Length == 3,
    "keyboard chord accepts modifiers and function keys");
bool invalidChordRejected = false;
try { KeyboardChord.Parse("Ctrl+Alt"); }
catch (ArgumentException) { invalidChordRejected = true; }
Check(invalidChordRejected, "modifier-only mapping rejected");

var doc = new ProfilesDocument();
doc.Profiles.Add(profile);
doc.SelectedProfile = profile.Name;
ProfileStore.Validate(doc);
Check(profile.Clone("test copy").Buttons[0].Parameter == "Ctrl+A",
    "profile cloning copies mappings");
Console.WriteLine("All profile and key/mouse edge tests passed.");


// Automatic per-executable profile selection, duplicate rejection and button safety.
var fallbackProfile = MappingProfile.CreateDefault("Fallback");
var gameProfile = MappingProfile.CreateDefault("Game");
gameProfile.ExecutableNames.Add("notepad.exe");
var autoprofiles = new ProfilesDocument
{
    SelectedProfile = "Fallback",
    AutoSwitchEnabled = true,
    Profiles = new List<MappingProfile> { fallbackProfile, gameProfile }
};
ProfileStore.Validate(autoprofiles);
Check(ExecutableProfileMatcher.Resolve(autoprofiles, "Fallback", "NOTEPAD.EXE", true).Name == "Game",
    "matching executable selects game profile regardless of case");
Check(ExecutableProfileMatcher.Resolve(autoprofiles, "Fallback", "explorer.exe", true).Name == "Fallback",
    "unmatched executable returns manual fallback");
Check(ExecutableProfileMatcher.Resolve(autoprofiles, "Fallback", "notepad.exe", false).Name == "Fallback",
    "automatic matching is opt-in");
Check(ExecutableProfileMatcher.NormalizeExecutableName("Notepad") == "notepad.exe",
    "normalizing executable name without extension");
Check(gameProfile.Clone("Copy").ExecutableNames.SequenceEqual(gameProfile.ExecutableNames),
    "profile cloning includes executable association");
bool collisionRejected = false;
fallbackProfile.ExecutableNames.Add("NOTEPAD.EXE");
try { ProfileStore.Validate(autoprofiles); }
catch (ArgumentException) { collisionRejected = true; }
Check(collisionRejected, "duplicate executable assignments are rejected");
fallbackProfile.ExecutableNames.Clear();

var heldGate = new ButtonReleaseGate();
heldGate.BlockCurrentlyHeld(g1g2);
Check(!heldGate.Filter(g1g2!).IsPhysicalBitPressed(0) &&
    !heldGate.Filter(g1g2).IsPhysicalBitPressed(1),
    "held keys suppressed immediately after profile change");
Check(!heldGate.Filter(g2!).IsPhysicalBitPressed(1),
    "still-held key remains suppressed even if another key released");
G13InputReport.TryParse(new byte[] { 1, 127, 127, 0, 0, 0, 0, 0 }, out var emptyReport);
heldGate.Filter(emptyReport!);
Check(heldGate.Filter(g2).IsPhysicalBitPressed(1),
    "previously held key may trigger once physically released and pressed again");
Console.WriteLine("Automatic profile switching tests passed.");


// Macro persistence, validation and deterministic execution without Windows injection.
var textMacro = new MacroDefinition
{
    Name = "Greeting", Mode = MacroMode.Text, Text = "Hi!\r\nNext\tcolumn"
};
textMacro.Validate();
var textEvents = new List<InputEvent>();
var textCharacters = new List<char>();
await MacroPlayback.ExecuteAsync(textMacro,
    textEvents.Add, textCharacters.Add);
Check(new string(textCharacters.ToArray()) == "Hi!Nextcolumn",
    "text macro sends characters without keyboard layout translation");
Check(textEvents.SequenceEqual(new[]
{
    new InputEvent(InputEventKind.KeyDown, 0x0D),
    new InputEvent(InputEventKind.KeyUp, 0x0D),
    new InputEvent(InputEventKind.KeyDown, 0x09),
    new InputEvent(InputEventKind.KeyUp, 0x09)
}), "text macro inserts Enter and Tab events");

var sequence = new MacroDefinition
{
    Name = "Test sequence", Mode = MacroMode.Steps,
    Steps = new()
    {
        new() { Kind = MacroStepKind.KeyDown, Parameter = "Ctrl" },
        new() { Kind = MacroStepKind.Delay, DelayAfterMs = 2 },
        new() { Kind = MacroStepKind.KeyPress, Parameter = "A" },
        new() { Kind = MacroStepKind.KeyUp, Parameter = "Ctrl" },
        new() { Kind = MacroStepKind.MouseClick, Parameter = "Left" },
        new() { Kind = MacroStepKind.MouseDoubleClick, Parameter = "Right" },
        new() { Kind = MacroStepKind.WheelDown }
    }
};
sequence.Validate();
var macroEvents = new List<InputEvent>();
await MacroPlayback.ExecuteAsync(sequence, macroEvents.Add, _ => { });
Check(macroEvents.SequenceEqual(new[]
{
    new InputEvent(InputEventKind.KeyDown, 0x11),
    new InputEvent(InputEventKind.KeyDown, 0x41),
    new InputEvent(InputEventKind.KeyUp, 0x41),
    new InputEvent(InputEventKind.KeyUp, 0x11),
    new InputEvent(InputEventKind.MouseDown, (int)MouseControl.Left),
    new InputEvent(InputEventKind.MouseUp, (int)MouseControl.Left),
    new InputEvent(InputEventKind.MouseDown, (int)MouseControl.Right),
    new InputEvent(InputEventKind.MouseUp, (int)MouseControl.Right),
    new InputEvent(InputEventKind.MouseDown, (int)MouseControl.Right),
    new InputEvent(InputEventKind.MouseUp, (int)MouseControl.Right),
    new InputEvent(InputEventKind.Wheel, -120)
}), "stepped macro emits keys, held modifiers, clicks and scroll in order");

var macroProfile = MappingProfile.CreateDefault("With macros");
macroProfile.Macros.Add(textMacro);
macroProfile.Macros.Add(sequence);
macroProfile.Buttons[0] = new ButtonAction
{
    Kind = MappingKind.Macro, Parameter = "Greeting"
};
macroProfile.Validate();
var cloneWithMacros = macroProfile.Clone("Copy of macro profile");
Check(cloneWithMacros.Macros.Count == 2 &&
      cloneWithMacros.Macros[0].Text == textMacro.Text,
    "macros survive profile clone");
Check(!ReferenceEquals(cloneWithMacros.Macros[0], textMacro),
    "clones do not share macro definitions");
var detector = new MacroTriggerDetector(macroProfile);
Check(detector.Detect(g1!).SequenceEqual(new[] { "Greeting" }),
    "macro triggers on initial physical press");
Check(detector.Detect(g1!).Count == 0,
    "held macro button never retriggers");
Check(detector.Detect(emptyReport!).Count == 0,
    "release arms next macro trigger");
Check(detector.Detect(g1!).SequenceEqual(new[] { "Greeting" }),
    "macro retriggers after release then press");

bool missingMacroRejected = false;
macroProfile.Buttons[0].Parameter = "Missing";
try { macroProfile.Validate(); }
catch (ArgumentException) { missingMacroRejected = true; }
Check(missingMacroRejected, "mapping referring to a missing macro is rejected");
macroProfile.Buttons[0].Parameter = "Greeting";

using var preCancelled = new CancellationTokenSource();
preCancelled.Cancel();
bool cancelled = false;
try
{
    await MacroPlayback.ExecuteAsync(sequence,
        _ => throw new Exception("cancelled macro unexpectedly emitted input"),
        _ => throw new Exception("cancelled macro emitted text"), preCancelled.Token);
}
catch (OperationCanceledException) { cancelled = true; }
Check(cancelled, "pre-cancelled macro emits no output");

var documentWithMacros = new ProfilesDocument
{
    SelectedProfile = macroProfile.Name,
    Profiles = new() { macroProfile }
};
ProfileStore.Validate(documentWithMacros);
string encoded = System.Text.Json.JsonSerializer.Serialize(documentWithMacros);
Check(encoded.Contains("Greeting") && encoded.Contains("Steps"),
    "macro definitions are serialized inside saved profiles");
Console.WriteLine("Macro validation, playback, cancellation and persistence tests passed.");
