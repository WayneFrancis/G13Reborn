using G13Reborn.Device;
using G13Reborn.Device.Hid;
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
Check(engine.Apply(g1!).Count == 0, "held key does not auto-repeat");

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
    !heldGate.Filter(g1g2!).IsPhysicalBitPressed(1),
    "held keys suppressed immediately after profile change");
Check(!heldGate.Filter(g2!).IsPhysicalBitPressed(1),
    "still-held key remains suppressed even if another key released");
G13InputReport.TryParse(new byte[] { 1, 127, 127, 0, 0, 0, 0, 0 }, out var emptyReport);
heldGate.Filter(emptyReport!);
Check(heldGate.Filter(g2!).IsPhysicalBitPressed(1),
    "previously held key may trigger once physically released and pressed again");
Console.WriteLine("Automatic profile switching tests passed.");


// Macro persistence, validation and deterministic execution without Windows injection.
var textMacro = new MacroDefinition
{
    Name = "Greeting", Mode = MacroMode.Text, Text = "Hi!\r\nNext\tcolumn"
};
textMacro.Validate();
var textEvents = new List<InputEvent>();
var textChunks = new List<string>();
await MacroPlayback.ExecuteAsync(textMacro,
    textEvents.Add, textChunks.Add);
Check(string.Concat(textChunks) == "Hi!Nextcolumn",
    "text macro sends Unicode text without keyboard layout translation");
Check(textChunks.Count == "Hi!Nextcolumn".Length &&
      textChunks.All(x => x.Length == 1),
    "text macro emits paced characters rather than flooding one batch");
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


// Regression: a short greeting MUST NOT be blasted as one huge SendInput
// event array; editors such as Notepad have been observed losing characters.
var greeting = new MacroDefinition
{
    Name = "Burst regression", Mode = MacroMode.Text, Text = "Hello from G13Reborn!!!"
};
var greetingChunks = new List<string>();
await MacroPlayback.ExecuteAsync(greeting, _ => { }, greetingChunks.Add);
Check(string.Concat(greetingChunks) == greeting.Text &&
      greetingChunks.Count == greeting.Text.Length &&
      greetingChunks.All(x => x.Length == 1),
    "text macro emits one character per paced SendInput call");
Check(greeting.TextDelayMs == 25, "new text macros default to 25 ms pacing");

// Legacy macros were serialized without a pacing field, so 0 must still
// provide safe pacing rather than reverting to the old broken bursts.
greeting.TextDelayMs = 0;
greetingChunks.Clear();
await MacroPlayback.ExecuteAsync(greeting, _ => { }, greetingChunks.Add);
Check(greetingChunks.Count == greeting.Text.Length,
    "legacy zero-delay text macros use paced output");
greeting.Text = "A\ud83d\ude00B";
greetingChunks.Clear();
await MacroPlayback.ExecuteAsync(greeting, _ => { }, greetingChunks.Add);
Check(greetingChunks.SequenceEqual(new[] { "A", "\ud83d\ude00", "B" }),
    "Unicode surrogate pair is sent as one paced scalar");
greeting.Text = "A\r\nB\tC";
greetingChunks.Clear();
var greetingKeys = new List<InputEvent>();
await MacroPlayback.ExecuteAsync(greeting, greetingKeys.Add, greetingChunks.Add);
Check(string.Concat(greetingChunks) == "ABC" &&
      greetingKeys.Count == 4,
    "newlines and tab keys are emitted once each");

// While a text macro runs, normal mapped G-key events must not interrupt
// its text. Completed macros replay them; cancelled macros discard them.
var deferred = new DeferredMappedInput();
var bDown = new InputEvent(InputEventKind.KeyDown, 0x42);
var bUp = new InputEvent(InputEventKind.KeyUp, 0x42);
Check(deferred.Receive(bDown).SequenceEqual(new[] { bDown }),
    "outside a macro ordinary mapped input passes immediately");
deferred.Begin();
Check(deferred.Receive(bDown).Count == 0 &&
      deferred.Receive(bUp).Count == 0,
    "ordinary mapped keys cannot interrupt paced macro text");
Check(deferred.End(replay: true).SequenceEqual(new[] { bDown, bUp }),
    "normal mapped keys replay in original order after macro completes");
deferred.Begin();
deferred.Receive(bDown);
Check(deferred.End(replay: false).Count == 0 &&
      deferred.Receive(bUp).SequenceEqual(new[] { bUp }),
    "stop or profile switch discards deferred key actions");
Console.WriteLine("Paced text and input deferral regression tests passed.");


// Macro recorder: input transitions, repeat suppression, timing and safety.
var capture = new MacroRecordingSession();
Check(capture.RecordKeyboard(0x11, true, 10), "recorder captures Ctrl press");
Check(!capture.RecordKeyboard(0x11, true, 20),
    "keyboard auto-repeat is not recorded as a second key-down");
Check(capture.RecordKeyboard(0x53, true, 120), "recorder captures S press");
Check(capture.RecordKeyboard(0x53, false, 190), "recorder captures S release");
Check(capture.RecordKeyboard(0x11, false, 200), "recorder captures Ctrl release");
Check(capture.RecordMouseButton("Left", true, 270),
    "recorder captures mouse down");
Check(capture.RecordMouseButton("Left", false, 300),
    "recorder captures mouse up");
Check(capture.RecordWheel(-120, 400), "recorder captures wheel");
var captureSteps = capture.Finish(410);
Check(captureSteps.Count == 7, "macro recorder captures seven distinct transitions");
Check(captureSteps.Select(x => x.Kind).SequenceEqual(new[]
{
    MacroStepKind.KeyDown, MacroStepKind.KeyDown, MacroStepKind.KeyUp,
    MacroStepKind.KeyUp, MacroStepKind.MouseDown, MacroStepKind.MouseUp,
    MacroStepKind.WheelDown
}), "recorded steps have correct event order");
Check(captureSteps.Select(x => x.DelayAfterMs).SequenceEqual(new[]
{
    110, 70, 10, 70, 30, 100, 0
}), "recorder preserves elapsed time between steps");
Check(!capture.RecordKeyboard(0x41, true, 420),
    "finished recorder ignores subsequent input");
Check(capture.Steps.Count == captureSteps.Count,
    "recorded steps remain available after recording ends");

var unfinished = new MacroRecordingSession();
Check(unfinished.RecordKeyboard(0x11, true, 1),
    "unfinished sequence begins with modifier down");
Check(unfinished.RecordMouseButton("Right", true, 20),
    "unfinished sequence begins with right mouse down");
var balanced = unfinished.Finish(50);
Check(balanced.Select(x => x.Kind).SequenceEqual(new[]
{
    MacroStepKind.KeyDown, MacroStepKind.MouseDown,
    MacroStepKind.KeyUp, MacroStepKind.MouseUp
}), "stopping recorder balances held modifier and mouse button");
Check(unfinished.Finish(60).Count == balanced.Count,
    "recorder Finish is idempotent");
var earlyClose = new MacroRecordingSession();
Check(!earlyClose.RecordKeyboard(0x41, true, 61_000),
    "recorder rejects events after the one-minute recording limit");

var recordedSequence = new MacroDefinition
{
    Name = "Recorded Ctrl+S", Mode = MacroMode.Steps,
    Steps = captureSteps.ToList()
};
recordedSequence.Validate();
var playbackEvents = new List<InputEvent>();
await MacroPlayback.ExecuteAsync(recordedSequence,
    playbackEvents.Add, _ => { });
Check(playbackEvents.Select(x => x.Kind).SequenceEqual(new[]
{
    InputEventKind.KeyDown, InputEventKind.KeyDown,
    InputEventKind.KeyUp, InputEventKind.KeyUp,
    InputEventKind.MouseDown, InputEventKind.MouseUp, InputEventKind.Wheel
}), "recorded control sequence is playable");

var cancellingSequence = new MacroDefinition
{
    Name = "Cancellation", Mode = MacroMode.Steps,
    Steps = new List<MacroStep>
    {
        new() { Kind = MacroStepKind.Delay, DelayAfterMs = 10_000 },
        new() { Kind = MacroStepKind.KeyPress, Parameter = "A" }
    }
};
using var cancelMidDelay = new CancellationTokenSource();
var duringWait = new List<InputEvent>();
Task cancelledTask = MacroPlayback.ExecuteAsync(cancellingSequence,
    duringWait.Add, _ => { }, cancelMidDelay.Token);
cancelMidDelay.Cancel();
bool stoppedDuringDelay = false;
try { await cancelledTask; }
catch (OperationCanceledException) { stoppedDuringDelay = true; }
Check(stoppedDuringDelay && duringWait.Count == 0,
    "cancelling during a long delay prevents later input");
Console.WriteLine("Macro recording and long-sequence cancellation tests passed.");


// Sequence editing regressions: record into a brand-new empty Steps macro,
// insert into an existing macro without replacing its surrounding actions,
// and standardize key-down/key-up delays independently.
var emptyDraft = new MacroDefinition
{
    Name = "AdvMacro", Mode = MacroMode.Steps, Steps = new()
};
bool emptyDraftRejected = false;
try { emptyDraft.Validate(); }
catch (ArgumentException) { emptyDraftRejected = true; }
Check(emptyDraftRejected, "empty Steps macro cannot be saved before recording");
emptyDraft.Validate(allowEmptySteps: true);
Check(true, "empty Steps macro is valid as an editor draft before recording");

var newCapture = new[]
{
    new MacroStep { Kind = MacroStepKind.KeyDown, Parameter = "A", DelayAfterMs = 7 },
    new MacroStep { Kind = MacroStepKind.KeyUp, Parameter = "A", DelayAfterMs = 11 }
};
var built = SequenceEditOperations.InsertRecording(
    emptyDraft.Steps, newCapture, null);
Check(built.Count == 2 && built[0].Kind == MacroStepKind.KeyDown &&
    built[1].Kind == MacroStepKind.KeyUp,
    "new recording can populate empty Steps macro without preexisting actions");
emptyDraft.Steps = built;
emptyDraft.Validate();

var originalSequence = new List<MacroStep>
{
    new() { Kind = MacroStepKind.KeyDown, Parameter = "Ctrl", DelayAfterMs = 20 },
    new() { Kind = MacroStepKind.KeyUp, Parameter = "Ctrl", DelayAfterMs = 30 },
    new() { Kind = MacroStepKind.WheelUp, DelayAfterMs = 40 }
};
var insert = SequenceEditOperations.InsertRecording(
    originalSequence, newCapture, selectedIndex: 0);
Check(insert.Count == 5 &&
    insert.Select(s => s.Kind).SequenceEqual(new[]
    {
        MacroStepKind.KeyDown, MacroStepKind.KeyDown,
        MacroStepKind.KeyUp, MacroStepKind.KeyUp, MacroStepKind.WheelUp
    }), "recording is inserted after selected step and preserves later actions");
Check(originalSequence.Count == 3 && newCapture[0].DelayAfterMs == 7 &&
    !ReferenceEquals(insert[1], newCapture[0]),
    "insertion does not mutate existing or newly recorded steps");
var appended = SequenceEditOperations.InsertRecording(
    originalSequence, newCapture, selectedIndex: null);
Check(appended.Select(s => s.Kind).SequenceEqual(new[]
{
    MacroStepKind.KeyDown, MacroStepKind.KeyUp, MacroStepKind.WheelUp,
    MacroStepKind.KeyDown, MacroStepKind.KeyUp
}), "recording appends when no step is explicitly selected");
bool invalidIndexRejected = false;
try { SequenceEditOperations.InsertRecording(originalSequence, newCapture, 3); }
catch (ArgumentOutOfRangeException) { invalidIndexRejected = true; }
Check(invalidIndexRejected, "invalid insertion indices are rejected");
bool capacityRejected = false;
var atCapacity = Enumerable.Range(0, 200).Select(_ =>
    new MacroStep { Kind = MacroStepKind.Delay }).ToArray();
try { SequenceEditOperations.InsertRecording(atCapacity, newCapture, null); }
catch (ArgumentException) { capacityRejected = true; }
Check(capacityRejected, "recording insertion enforces the 200-step limit");

var standard = insert.Select(x => x.Clone()).ToList();
int downCount = SequenceEditOperations.ApplyKeyboardDelay(
    standard, MacroStepKind.KeyDown, 25);
Check(downCount == 2 &&
    standard[0].DelayAfterMs == 25 &&
    standard[1].DelayAfterMs == 25 &&
    standard[2].DelayAfterMs == 11 &&
    standard[4].DelayAfterMs == 40,
    "KeyDown apply only changes delays after keyboard key-down events");
int upCount = SequenceEditOperations.ApplyKeyboardDelay(
    standard, MacroStepKind.KeyUp, 25);
Check(upCount == 2 &&
    standard[2].DelayAfterMs == 25 &&
    standard[3].DelayAfterMs == 25 &&
    standard[4].DelayAfterMs == 40,
    "KeyUp apply only changes delays after keyboard key-up events");
Check(originalSequence[0].DelayAfterMs == 20,
    "normalizing an inserted macro leaves the original macro unchanged");
Console.WriteLine("Record insertion and independent key timing tests passed.");


// Regression: clicking "Stop and use recording" from another foreground
// application must not capture its own left-mouse down/up transitions.
Check(RecordingEventFilter.ShouldCaptureMouse(true, true, false, false),
    "mouse clicks in the target application are recorded");
Check(!RecordingEventFilter.ShouldCaptureMouse(true, true, true, false),
    "Stop click is rejected even before recorder gains foreground focus");
Check(!RecordingEventFilter.ShouldCaptureMouse(true, false, true, false),
    "recorder window clicks are never captured");
Check(!RecordingEventFilter.ShouldCaptureMouse(true, true, false, true),
    "injected mouse events remain excluded");
Check(!RecordingEventFilter.ShouldCaptureMouse(false, true, false, false),
    "mouse is not recorded once recording is stopped");

// The user may wait several seconds between finishing the actual sequence
// and clicking Stop. This must not create a long final delay or extra actions.
var stopButtonRegression = new MacroRecordingSession();
Check(stopButtonRegression.RecordKeyboard(0x41, true, 10),
    "capture key down before final Stop click");
Check(stopButtonRegression.RecordKeyboard(0x41, false, 40),
    "capture key up before final Stop click");
var cleanStopSteps = stopButtonRegression.Finish(15_000);
Check(cleanStopSteps.Count == 2 &&
      cleanStopSteps[0].DelayAfterMs == 30 &&
      cleanStopSteps[1].DelayAfterMs == 0,
    "finishing after a long idle interval appends no Stop mouse events or final delay");

var pendingKeyRelease = new MacroRecordingSession();
pendingKeyRelease.RecordKeyboard(0x41, true, 10);
var balancedWithoutLongWait = pendingKeyRelease.Finish(12_000);
Check(balancedWithoutLongWait.Count == 2 &&
      balancedWithoutLongWait[0].DelayAfterMs == 25 &&
      balancedWithoutLongWait[1].Kind == MacroStepKind.KeyUp &&
      balancedWithoutLongWait[1].DelayAfterMs == 0,
    "synthetic final release uses at most 25 ms, not time spent clicking Stop");

var pendingMouseRelease = new MacroRecordingSession();
pendingMouseRelease.RecordMouseButton("Left", true, 12);
var shortMouseRelease = pendingMouseRelease.Finish(20_000);
Check(shortMouseRelease.Count == 2 &&
      shortMouseRelease[0].DelayAfterMs == 25 &&
      shortMouseRelease[1].Kind == MacroStepKind.MouseUp,
    "unfinished genuine mouse press releases without long Stop-button delay");
Console.WriteLine("Recorder Stop button isolation and final-delay regressions passed.");


// Editor-driven preview tests. No native Windows input is emitted: the runner
// receives delegates so cancellation and cleanup can be tested deterministically.
var previewOutput = new List<InputEvent>();
var previewText = new List<string>();
var preview = new MacroPreviewRunner(previewOutput.Add, previewText.Add);
var simplePreview = new MacroDefinition
{
    Name = "Preview text", Mode = MacroMode.Text, Text = "Z"
};
await preview.RunAsync(simplePreview);
Check(previewText.SequenceEqual(new[] { "Z" }) &&
      previewOutput.Count == 0 && !preview.IsRunning,
    "editor preview sends text through the injected output callback");
preview.Cancel();
Check(previewOutput.Count == 0,
    "cancelling an already completed preview is a no-op");

// A long KeyDown must be released immediately when the preview is cancelled,
// even though the KeyUp step was never reached.
var heldPreview = new MacroDefinition
{
    Name = "Held Ctrl", Mode = MacroMode.Steps,
    Steps = new()
    {
        new() { Kind = MacroStepKind.KeyDown, Parameter = "Ctrl", DelayAfterMs = 30_000 },
        new() { Kind = MacroStepKind.KeyUp, Parameter = "Ctrl" },
        new() { Kind = MacroStepKind.KeyPress, Parameter = "Z" }
    }
};
previewOutput.Clear();
Task heldTask = preview.RunAsync(heldPreview);
Check(preview.IsRunning &&
      previewOutput.SequenceEqual(new[]
      {
          new InputEvent(InputEventKind.KeyDown, 0x11)
      }), "preview starts with a held Ctrl input");

bool overlapRejected = false;
try { await preview.RunAsync(simplePreview); }
catch (InvalidOperationException) { overlapRejected = true; }
Check(overlapRejected, "concurrent macro tests are rejected");

preview.Cancel();
bool previewCancelled = false;
try { await heldTask; }
catch (OperationCanceledException) { previewCancelled = true; }
Check(previewCancelled && !preview.IsRunning &&
      previewOutput.SequenceEqual(new[]
      {
          new InputEvent(InputEventKind.KeyDown, 0x11),
          new InputEvent(InputEventKind.KeyUp, 0x11)
      }), "cancel during a long key hold immediately releases Ctrl and suppresses later steps");

var mousePreview = new MacroDefinition
{
    Name = "Mouse preview", Mode = MacroMode.Steps,
    Steps = new()
    {
        new() { Kind = MacroStepKind.MouseClick, Parameter = "Left" }
    }
};
previewOutput.Clear();
Task mouseTask = preview.RunAsync(mousePreview);
Check(previewOutput.Count == 1 &&
      previewOutput[0] == new InputEvent(
          InputEventKind.MouseDown, (int)MouseControl.Left),
    "mouse preview emits the button-down before click delay");
preview.Cancel();
bool mouseCancelled = false;
try { await mouseTask; }
catch (OperationCanceledException) { mouseCancelled = true; }
Check(mouseCancelled && previewOutput.Count == 2 &&
      previewOutput[1] == new InputEvent(
          InputEventKind.MouseUp, (int)MouseControl.Left),
    "cancel during mouse click releases the button exactly once");

var unfinishedPreview = new MacroDefinition
{
    Name = "Unbalanced preview", Mode = MacroMode.Steps,
    Steps = new()
    {
        new() { Kind = MacroStepKind.KeyDown, Parameter = "Ctrl" },
        new() { Kind = MacroStepKind.MouseDown, Parameter = "Right" }
    }
};
previewOutput.Clear();
await preview.RunAsync(unfinishedPreview);
Check(previewOutput.Count == 4 &&
      previewOutput[2] == new InputEvent(
          InputEventKind.KeyUp, 0x11) &&
      previewOutput[3] == new InputEvent(
          InputEventKind.MouseUp, (int)MouseControl.Right),
    "normal preview completion releases all deliberately unbalanced held inputs");

var errorOutput = new List<InputEvent>();
var failingPreview = new MacroPreviewRunner(e =>
{
    errorOutput.Add(e);
    if (e.Kind == InputEventKind.KeyDown)
        throw new InvalidOperationException("Simulated injection failure");
}, _ => { });
var errorMacro = new MacroDefinition
{
    Name = "Failing preview", Mode = MacroMode.Steps,
    Steps = new()
    {
        new() { Kind = MacroStepKind.KeyDown, Parameter = "A" }
    }
};
bool errorPropagated = false;
try { await failingPreview.RunAsync(errorMacro); }
catch (InvalidOperationException) { errorPropagated = true; }
Check(errorPropagated && !failingPreview.IsRunning &&
      errorOutput.SequenceEqual(new[]
      {
          new InputEvent(InputEventKind.KeyDown, 0x41),
          new InputEvent(InputEventKind.KeyUp, 0x41)
      }), "output failure still triggers a best-effort key release");
Console.WriteLine("Direct macro preview, cancellation, and held-input cleanup tests passed.");


// Manual insertion regressions. These positions are also used by the
// WinForms button handler, so no-row-selection is never confused with row 1.
Check(SequenceEditOperations.GetInsertionIndex(0, null, before: false) == 0,
    "Add step to an empty macro inserts first step");
Check(SequenceEditOperations.GetInsertionIndex(0, null, before: true) == 0,
    "Insert before on empty macro also inserts first step");
Check(SequenceEditOperations.GetInsertionIndex(4, null, before: false) == 4,
    "Add step with no selection appends after last step");
Check(SequenceEditOperations.GetInsertionIndex(4, null, before: true) == 0,
    "Insert before without selection prepends at start");
Check(SequenceEditOperations.GetInsertionIndex(4, 0, before: false) == 1,
    "Add step with first row selected inserts after first row");
Check(SequenceEditOperations.GetInsertionIndex(4, 0, before: true) == 0,
    "Insert before with first row selected inserts ahead of first row");
Check(SequenceEditOperations.GetInsertionIndex(4, 2, before: false) == 3 &&
      SequenceEditOperations.GetInsertionIndex(4, 2, before: true) == 2,
    "selected middle row supports before and after insertion");
Check(SequenceEditOperations.GetInsertionIndex(4, 3, before: false) == 4 &&
      SequenceEditOperations.GetInsertionIndex(4, 3, before: true) == 3,
    "selected final row supports before and after insertion");

bool wrongManualIndexRejected = false;
try { SequenceEditOperations.GetInsertionIndex(4, 4, before: false); }
catch (ArgumentOutOfRangeException) { wrongManualIndexRejected = true; }
Check(wrongManualIndexRejected, "manual insertion rejects invalid selected row");

bool tooManyManualStepsRejected = false;
try { SequenceEditOperations.GetInsertionIndex(200, null, before: true); }
catch (ArgumentOutOfRangeException) { tooManyManualStepsRejected = true; }
Check(tooManyManualStepsRejected, "manual insertion preserves 200-step macro limit");

// Preview progress reports each step in execution order without changing
// input playback or the cancellation/held-release behaviour.
var progressMacro = new MacroDefinition
{
    Name = "Progress", Mode = MacroMode.Steps,
    Steps = new List<MacroStep>
    {
        new() { Kind = MacroStepKind.KeyDown, Parameter = "A" },
        new() { Kind = MacroStepKind.KeyUp, Parameter = "A" },
        new() { Kind = MacroStepKind.Delay, DelayAfterMs = 1 },
        new() { Kind = MacroStepKind.WheelUp }
    }
};
var progress = new List<int>();
var progressEvents = new List<InputEvent>();
var progressed = new MacroPreviewRunner(progressEvents.Add, _ => { });
await progressed.RunAsync(progressMacro, default,
    (index, step) =>
    {
        progress.Add(index);
        Check(progressMacro.Steps[index].Kind == step.Kind,
            $"preview progress correctly reports action {index + 1}");
    });
Check(progress.SequenceEqual(new[] { 0, 1, 2, 3 }),
    "preview reports every step once in order");
Check(progressEvents.SequenceEqual(new[]
{
    new InputEvent(InputEventKind.KeyDown, 0x41),
    new InputEvent(InputEventKind.KeyUp, 0x41),
    new InputEvent(InputEventKind.Wheel, 120)
}), "progress observers do not affect emitted macro events");
Console.WriteLine("Manual insertion and macro playback progress regressions passed.");


// Multi-row macro editor regressions and clipboard storage.
var sourceRows = new List<MacroStep>
{
    new() { Kind = MacroStepKind.KeyDown, Parameter = "A", DelayAfterMs = 19 },
    new() { Kind = MacroStepKind.KeyUp, Parameter = "A", DelayAfterMs = 31 },
    new() { Kind = MacroStepKind.KeyDown, Parameter = "B", DelayAfterMs = 42 },
    new() { Kind = MacroStepKind.KeyUp, Parameter = "B", DelayAfterMs = 53 },
    new() { Kind = MacroStepKind.MouseClick, Parameter = "Left", DelayAfterMs = 65 }
};
var multiCopied = SequenceEditOperations.CopySelected(
    sourceRows, new[] { 3, 1, 3 });
Check(multiCopied.Count == 2 &&
      multiCopied[0].Parameter == "A" &&
      multiCopied[0].DelayAfterMs == 31 &&
      multiCopied[1].Parameter == "B" &&
      multiCopied[1].DelayAfterMs == 53,
    "Ctrl+C copies disjoint rows in sequence order with exact delays");
Check(!ReferenceEquals(multiCopied[0], sourceRows[1]),
    "copied macro rows are deep copies");

var afterDelete = SequenceEditOperations.RemoveSelected(
    sourceRows, new[] { 3, 1 });
Check(afterDelete.Count == 3 &&
      afterDelete.Select(s => s.Kind).SequenceEqual(new[]
      {
          MacroStepKind.KeyDown,
          MacroStepKind.KeyDown,
          MacroStepKind.MouseClick
      }) &&
      sourceRows.Count == 5,
    "Del and Ctrl+X remove all selected rows without modifying source snapshots");
Check(SequenceEditOperations.RemoveSelected(sourceRows,
    Enumerable.Range(0, sourceRows.Count)).Count == 0,
    "multi-row deletion can temporarily leave an empty editing draft");

var pastedMiddle = SequenceEditOperations.PasteSteps(
    sourceRows, multiCopied, new[] { 0, 2 });
Check(pastedMiddle.Count == 7 &&
      pastedMiddle[3].Kind == MacroStepKind.KeyUp &&
      pastedMiddle[3].Parameter == "A" &&
      pastedMiddle[4].Parameter == "B" &&
      pastedMiddle[5].Kind == MacroStepKind.KeyUp,
    "Ctrl+V pastes after last selected row, preserving following steps");
var pastedAppend = SequenceEditOperations.PasteSteps(
    sourceRows, multiCopied, Array.Empty<int>());
Check(pastedAppend.Count == 7 &&
      pastedAppend[5].DelayAfterMs == 31 &&
      pastedAppend[6].DelayAfterMs == 53,
    "Ctrl+V without selection appends copied rows preserving recorded timing");

bool tooManyPastedRejected = false;
try
{
    SequenceEditOperations.PasteSteps(atCapacity,
        multiCopied, new[] { 0 });
}
catch (ArgumentException) { tooManyPastedRejected = true; }
Check(tooManyPastedRejected, "paste respects 200-step macro maximum");

string stepPayload = MacroStepsClipboardCodec.Serialize(multiCopied);
var decodedSteps = MacroStepsClipboardCodec.Deserialize(stepPayload);
Check(decodedSteps.Count == 2 &&
      decodedSteps[0].Kind == MacroStepKind.KeyUp &&
      decodedSteps[0].Parameter == "A" &&
      decodedSteps[0].DelayAfterMs == 31 &&
      decodedSteps[1].DelayAfterMs == 53,
    "typed clipboard JSON roundtrip preserves step order, action and delays");
bool invalidClipboardRejected = false;
try { MacroStepsClipboardCodec.Deserialize("{not JSON}"); }
catch (ArgumentException) { invalidClipboardRejected = true; }
Check(invalidClipboardRejected, "foreign or corrupt clipboard payload is rejected");
bool maliciousClipboardRejected = false;
try
{
    MacroStepsClipboardCodec.Deserialize(
        "[{\"Kind\":999,\"Parameter\":\"A\",\"DelayAfterMs\":0}]");
}
catch (ArgumentException) { maliciousClipboardRejected = true; }
Check(maliciousClipboardRejected, "invalid clipboard action codes are rejected");

var recordedBlock = new List<MacroStep>
{
    new() { Kind = MacroStepKind.KeyDown, Parameter = "Z", DelayAfterMs = 25 },
    new() { Kind = MacroStepKind.KeyUp, Parameter = "Z", DelayAfterMs = 0 }
};
var spliceInMiddle = SequenceEditOperations.InsertRecording(
    sourceRows, recordedBlock, selectedIndex: 1, joinDelayMs: 25);
Check(spliceInMiddle.Count == 7 &&
      spliceInMiddle[2].DelayAfterMs == 25 &&
      spliceInMiddle[3].DelayAfterMs == 25 &&
      spliceInMiddle[4].Kind == MacroStepKind.KeyDown,
    "recorded block inserted mid-sequence receives a nonzero trailing join delay");
Check(recordedBlock[^1].DelayAfterMs == 0 &&
      sourceRows[2].DelayAfterMs == 42,
    "recording insertion does not mutate original steps");
var appendedBlock = SequenceEditOperations.InsertRecording(
    sourceRows, recordedBlock, selectedIndex: null, joinDelayMs: 25);
Check(appendedBlock[^1].DelayAfterMs == 0,
    "recorded block appended at end retains final zero delay");
var customEnding = recordedBlock.Select(x => x.Clone()).ToList();
customEnding[^1].DelayAfterMs = 80;
var preservedJoin = SequenceEditOperations.InsertRecording(
    sourceRows, customEnding, selectedIndex: 0, joinDelayMs: 25);
Check(preservedJoin[2].DelayAfterMs == 80,
    "user-specified nonzero join delay is preserved");
Console.WriteLine("Macro multi-selection, clipboard and recording-join tests passed.");


// Macro management: deep duplication, stable identities through rename,
// assignment repair and safe staging before Save profile.
var managedOriginal = new MacroDefinition
{
    Name = "AdvMacro", Mode = MacroMode.Steps, Steps = new()
    {
        new() { Kind = MacroStepKind.KeyDown, Parameter = "Ctrl", DelayAfterMs = 25 },
        new() { Kind = MacroStepKind.KeyUp, Parameter = "Ctrl", DelayAfterMs = 25 }
    }
};
var managedSecond = new MacroDefinition
{
    Name = "MouseMacro", Mode = MacroMode.Text, Text = "Click me!"
};
var managedCollection = new List<MacroDefinition> { managedOriginal, managedSecond };
var firstDuplicate = MacroCatalogEdits.Duplicate(managedOriginal, managedCollection);
Check(firstDuplicate.Name == "AdvMacro copy" &&
      firstDuplicate.Steps.Count == 2 &&
      !ReferenceEquals(firstDuplicate.Steps[0], managedOriginal.Steps[0]),
    "Duplicate creates uniquely named deep copy of existing sequence");
firstDuplicate.Steps[0].DelayAfterMs = 70;
Check(managedOriginal.Steps[0].DelayAfterMs == 25,
    "Editing duplicated steps does not mutate the source macro");
var secondDuplicate = MacroCatalogEdits.Duplicate(managedOriginal,
    managedCollection.Append(firstDuplicate));
Check(secondDuplicate.Name == "AdvMacro copy 2",
    "Repeated duplicates receive collision-free numbered names");
var caseDuplicate = MacroCatalogEdits.Duplicate(managedOriginal,
    managedCollection.Append(firstDuplicate).Append(
        new MacroDefinition
        {
            Name = "ADVMACRO COPY 2", Mode = MacroMode.Text, Text = "X"
        }));
Check(caseDuplicate.Name == "AdvMacro copy 3",
    "Duplicate naming uses case-insensitive collision checks");

var editedOriginals = new List<MacroDefinition>
{
    managedOriginal.Clone(), managedSecond.Clone(), firstDuplicate.Clone()
};
editedOriginals[0].Name = "AdvMacro v2";
editedOriginals[1].Name = "Mouse v2";
var identityRenames = MacroCatalogEdits.BuildRenames(
    new string?[] { "AdvMacro", "MouseMacro", null }, editedOriginals);
Check(identityRenames.Count == 2 &&
      identityRenames["AdvMacro"] == "AdvMacro v2" &&
      identityRenames["MouseMacro"] == "Mouse v2",
    "Existing macro renames preserve original identities; duplicates do not rename bindings");

var originalBindings = new Dictionary<int, ButtonAction>
{
    [0] = new() { Kind = MappingKind.Macro, Parameter = "AdvMacro" },
    [1] = new() { Kind = MappingKind.Macro, Parameter = "MouseMacro" },
    [2] = new() { Kind = MappingKind.Keyboard, Parameter = "C" },
    [3] = new() { Kind = MappingKind.Macro, Parameter = "OtherMacro" }
};
var reconciledRenames = MacroCatalogEdits.ReconcileBindings(
    originalBindings, new[] { "AdvMacro", "MouseMacro" },
    editedOriginals, identityRenames);
Check(reconciledRenames.Buttons[0].Parameter == "AdvMacro v2" &&
      reconciledRenames.Buttons[1].Parameter == "Mouse v2" &&
      reconciledRenames.RenamedBits.SequenceEqual(new[] { 0, 1 }),
    "Renaming a macro repairs all matching G-key assignments");
Check(reconciledRenames.Buttons[2].Kind == MappingKind.Keyboard &&
      reconciledRenames.Buttons[3].Parameter == "OtherMacro" &&
      originalBindings[0].Parameter == "AdvMacro",
    "Assignment repairs leave other kinds and original profile untouched");

var deletedManaged = new List<MacroDefinition> { editedOriginals[0] };
var reconciledDeletes = MacroCatalogEdits.ReconcileBindings(
    originalBindings, new[] { "AdvMacro", "MouseMacro" },
    deletedManaged,
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { ["AdvMacro"] = "AdvMacro v2" });
Check(reconciledDeletes.Buttons[0].Parameter == "AdvMacro v2" &&
      reconciledDeletes.Buttons[1].Kind == MappingKind.None &&
      reconciledDeletes.Buttons[1].Parameter == "" &&
      reconciledDeletes.ClearedBits.SequenceEqual(new[] { 1 }),
    "Deleting an assigned macro clears the orphaned mapping safely");
Check(originalBindings[1].Kind == MappingKind.Macro,
    "Staged deletion never mutates persisted binding objects");

var persistedManagedProfile = MappingProfile.CreateDefault("Macro save boundary");
persistedManagedProfile.Macros = managedCollection.Select(m => m.Clone()).ToList();
persistedManagedProfile.Buttons[0] = new ButtonAction
    { Kind = MappingKind.Macro, Parameter = "AdvMacro" };
persistedManagedProfile.Validate();
var stagingProfile = persistedManagedProfile.Clone(persistedManagedProfile.Name);
stagingProfile.Macros = editedOriginals.Select(m => m.Clone()).ToList();
stagingProfile.Buttons[0].Parameter = "AdvMacro v2";
stagingProfile.Validate();
Check(persistedManagedProfile.Macros[0].Name == "AdvMacro" &&
      persistedManagedProfile.Buttons[0].Parameter == "AdvMacro" &&
      stagingProfile.Macros[0].Name == "AdvMacro v2",
    "Editing a cloned pending profile cannot change unsaved persisted macro bindings");
Console.WriteLine("Macro duplication, rename, assignment and staged-save tests passed.");


// Main mapping grid: only Macro actions are dropdown-bound to the current
// profile's macro list; other mappings keep free-form parameter text.
var macroOptions = new List<MacroDefinition>
{
    new() { Name = "Zebra", Mode = MacroMode.Text, Text = "z" },
    new() { Name = "alpha", Mode = MacroMode.Text, Text = "a" },
    new() { Name = "Bravo", Mode = MacroMode.Text, Text = "b" }
};
var sortedMacroNames = MappingParameterChoices.MacroNames(macroOptions);
Check(sortedMacroNames.SequenceEqual(new[] { "alpha", "Bravo", "Zebra" }),
    "macro dropdown sorts only names from current profile");
Check(MappingParameterChoices.ExistingMacroChoice(
    "ALPHA", sortedMacroNames) == "alpha",
    "macro dropdown canonicalizes case-insensitive existing assignments");
Check(MappingParameterChoices.ExistingMacroChoice(
    "macro in different profile", sortedMacroNames) == "",
    "macro dropdown does not allow names from other profiles");
Check(MappingParameterChoices.ExistingMacroChoice(
    "", sortedMacroNames) == "",
    "macro dropdown supports unassigned blank during editing");
Check(MappingParameterChoices.MacroNames(
    Array.Empty<MacroDefinition>()).Length == 0,
    "empty profile has no selectable macro names");
Check(MappingParameterChoices.ParameterAfterKindChange(
    MappingKind.Keyboard, MappingKind.Macro, "Ctrl+A") == "",
    "changing a key chord to Macro clears incompatible parameter text");
Check(MappingParameterChoices.ParameterAfterKindChange(
    MappingKind.Macro, MappingKind.Keyboard, "Zebra") == "",
    "changing Macro to a keyboard mapping clears the old macro name");
Check(MappingParameterChoices.ParameterAfterKindChange(
    MappingKind.Keyboard, MappingKind.Keyboard, "Ctrl+A") == "Ctrl+A",
    "retaining Keyboard keeps its existing chord");
Check(MappingParameterChoices.ParameterAfterKindChange(
    MappingKind.Macro, MappingKind.Macro, "Zebra") == "Zebra",
    "refreshing an existing Macro action preserves its selected name");

var laterEdited = new List<MacroDefinition>
{
    new() { Name = "New name", Mode = MacroMode.Text, Text = "typed" }
};
var laterChoices = MappingParameterChoices.MacroNames(laterEdited);
Check(MappingParameterChoices.ExistingMacroChoice("Old name", laterChoices) == "" &&
      MappingParameterChoices.ExistingMacroChoice("New name", laterChoices) == "New name",
    "macro dropdown refresh removes deleted or renamed names and accepts new names");
Console.WriteLine("Profile macro dropdown option/transition tests passed.");


// G13 output report regressions; these never open a real HID handle.
var hardwareRgb = G13HardwareReports.Rgb(0x12, 0x34, 0x56);
Check(hardwareRgb.SequenceEqual(new byte[] { 0x07, 0x12, 0x34, 0x56, 0x00 }),
    "G13 RGB report uses ID 7 and 5 bytes (R/G/B)");
Check(G13HardwareReports.MacroLeds(true, false, true, true)
    .SequenceEqual(new byte[] { 0x05, 0x0D, 0, 0, 0 }),
    "G13 macro LED report sets M1/M3/MR bits");
Check(G13HardwareReports.MacroLeds(false, false, false, false)[1] == 0,
    "G13 M LED report can explicitly clear all four lights");

var lcdBlank = G13HardwareReports.BlankLcd();
Check(lcdBlank.Length == 992 && lcdBlank[0] == 3 &&
      lcdBlank.Skip(1).All(b => b == 0),
    "empty G13 LCD output has the correct 992-byte header and clear pixels");

var lcdSingle = G13HardwareReports.LcdFrame(
    (x, y) => (x == 0 && y == 0) ||
               (x == 0 && y == 1) ||
               (x == 159 && y == 42) ||
               (x == 83 && y == 15));
Check(lcdSingle.Length == 992 && lcdSingle[0] == 3 &&
      lcdSingle.Skip(1).Take(31).All(b => b == 0),
    "G13 LCD report ID and 31-byte header padding are correct");
Check(lcdSingle[32] == 0x03,
    "LCD top-left pixel uses bit 0 and next row uses bit 1");
Check(lcdSingle[32 + 160 + 83] == 0x80,
    "LCD second-band final row sets bit 7");
Check(lcdSingle[991] == 0x04,
    "LCD last visible pixel (159,42) maps to last byte bit 2");
Check(lcdSingle.Skip(32).Sum(b => System.Numerics.BitOperations.PopCount((uint)b)) == 4,
    "LCD frame packs exactly four selected dark pixels");

var lcdTest = G13HardwareReports.TestPattern();
Check(lcdTest.Length == 992 && lcdTest[32] != 0 &&
      lcdTest[991] != 0 && lcdTest[32 + 2 * 160 + 80] != 0,
    "G13 LCD test pattern includes border and central crosshair");
Console.WriteLine("G13 hardware report encoder tests passed (no device output).");


// G13 M-key LED readback uses the same feature ID and mask layout as
// the write path. No real USB device is accessed in these tests.
Check(G13HardwareReports.ReadMacroLedMask(
    new byte[] { 0x05, 0x0D, 0, 0, 0 }) == 0x0D,
    "M1/M3/MR LED readback uses low four bits of feature report 05");
Check(G13HardwareReports.ReadMacroLedMask(
    new byte[] { 0x05, 0xFF, 0, 0, 0 }) == 0x0F,
    "macro LED readback ignores reserved high bits");
Check(G13HardwareReports.ReadMacroLedMask(
    G13HardwareReports.MacroLeds(false, true, false, true)) == 0x0A,
    "M2/MR write/read mask roundtrip matches bit layout");
bool shortLedReportRejected = false;
try { G13HardwareReports.ReadMacroLedMask(new byte[] { 0x05, 0x01 }); }
catch (ArgumentException) { shortLedReportRejected = true; }
Check(shortLedReportRejected, "LED readback rejects incomplete reports");
bool wrongLedReportRejected = false;
try { G13HardwareReports.ReadMacroLedMask(new byte[] { 0x07, 1, 0, 0, 0 }); }
catch (ArgumentException) { wrongLedReportRejected = true; }
Check(wrongLedReportRejected, "LED readback rejects RGB report IDs");
Console.WriteLine("M-key LED readback decoding tests passed.");


// Macro repeat modes are persisted with each macro; older profiles default
// to Once. The preview remains a deliberate single-pass test.
var loopOnce = new MacroDefinition
{
    Name = "Once on G1", Mode = MacroMode.Steps,
    RepeatMode = MacroRepeatMode.Once,
    Steps = new() { new() { Kind = MacroStepKind.WheelUp } }
};
var loopHeld = loopOnce.Clone();
loopHeld.Name = "Held on G1";
loopHeld.RepeatMode = MacroRepeatMode.WhileHeld;
var loopToggle = loopHeld.Clone();
loopToggle.Name = "Toggle on G1";
loopToggle.RepeatMode = MacroRepeatMode.Toggle;
Check(loopHeld.Clone().RepeatMode == MacroRepeatMode.WhileHeld &&
      loopToggle.Clone().RepeatMode == MacroRepeatMode.Toggle,
    "cloned macros retain repeat mode without changing original");
Check(loopOnce.RepeatMode == MacroRepeatMode.Once,
    "new macro defaults to nonlooping Once");
var oldMacro = System.Text.Json.JsonSerializer.Deserialize<MacroDefinition>(
    """{"Name":"Legacy","Mode":0,"Text":"Hi"}""");
Check(oldMacro is not null && oldMacro.RepeatMode == MacroRepeatMode.Once,
    "old saved macro without RepeatMode loads as Once");
bool badRepeatRejected = false;
try { var broken = loopOnce.Clone();
    broken.RepeatMode = (MacroRepeatMode)99; broken.Validate(); }
catch (ArgumentException) { badRepeatRejected = true; }
Check(badRepeatRejected, "invalid repeat modes are rejected");

Check(MacroRepeatPolicy.OnTransition(MacroRepeatMode.Once, true, false) ==
      MacroRepeatAction.Start &&
      MacroRepeatPolicy.OnTransition(MacroRepeatMode.Once, false, true) ==
      MacroRepeatAction.Ignore,
    "Once triggers on key press, not release");
Check(MacroRepeatPolicy.OnTransition(MacroRepeatMode.WhileHeld, true, false) ==
      MacroRepeatAction.Start &&
      MacroRepeatPolicy.OnTransition(MacroRepeatMode.WhileHeld, false, true) ==
      MacroRepeatAction.Stop &&
      MacroRepeatPolicy.OnTransition(MacroRepeatMode.WhileHeld, false, false) ==
      MacroRepeatAction.Ignore,
    "WhileHeld starts on press and stops only on its owning key release");
Check(MacroRepeatPolicy.OnTransition(MacroRepeatMode.Toggle, true, false) ==
      MacroRepeatAction.Start &&
      MacroRepeatPolicy.OnTransition(MacroRepeatMode.Toggle, false, true) ==
      MacroRepeatAction.Ignore &&
      MacroRepeatPolicy.OnTransition(MacroRepeatMode.Toggle, true, true) ==
      MacroRepeatAction.Stop,
    "Toggle starts on first press and stops on the next press, ignoring release");

var holdProfile = MappingProfile.CreateDefault("Holding");
holdProfile.Macros.Add(loopHeld);
holdProfile.Buttons[0] = new ButtonAction
{
    Kind = MappingKind.Macro, Parameter = loopHeld.Name
};
holdProfile.Validate();
var holdDetector = new MacroTriggerDetector(holdProfile);
Check(holdDetector.DetectTransitions(g1!).Single() ==
      new MacroButtonTransition(0, loopHeld.Name, true),
    "macro G-key press edge reports physical bit and macro name");
Check(holdDetector.DetectTransitions(g1!).Count == 0,
    "repeated HID reports do not produce repeat press edges");
Check(holdDetector.DetectTransitions(emptyReport!).Single() ==
      new MacroButtonTransition(0, loopHeld.Name, false),
    "macro G-key release edge is detected for WhileHeld cancellation");
Check(holdDetector.DetectTransitions(emptyReport!).Count == 0,
    "repeated released HID reports do not produce duplicate stop edges");

var onceEvents = new List<InputEvent>();
int onceReleaseCalls = 0;
await MacroRepeatRunner.ExecuteAsync(loopOnce, onceEvents.Add,
    _ => { }, () => onceReleaseCalls++, CancellationToken.None);
Check(onceEvents.Count == 1 && onceReleaseCalls == 0,
    "Once executes a single pass without repeating");

using var repeatingCancel = new CancellationTokenSource();
var repeatEvents = new List<InputEvent>();
int repeatReleaseCalls = 0;
bool repeatingCancelled = false;
try
{
    await MacroRepeatRunner.ExecuteAsync(loopToggle, action =>
    {
        repeatEvents.Add(action);
        if (repeatEvents.Count >= 3) repeatingCancel.Cancel();
    }, _ => { }, () => repeatReleaseCalls++, repeatingCancel.Token);
}
catch (OperationCanceledException) { repeatingCancelled = true; }
Check(repeatingCancelled && repeatEvents.Count == 3 &&
      repeatReleaseCalls == 2 &&
      MacroRepeatRunner.MinimumRepeatGapMs > 0,
    "Toggle repeats until cancelled, releases between passes and throttles fast loops");

var delayStep = new MacroStep
{
    Kind = MacroStepKind.Delay, Parameter = "", DelayAfterMs = 200
};
delayStep.Validate();
var delayOnly = new MacroDefinition
{
    Name = "Pause", Mode = MacroMode.Steps,
    Steps = new() { delayStep }
};
var delayEvents = new List<InputEvent>();
await MacroPlayback.ExecuteAsync(delayOnly, delayEvents.Add, _ => { });
Check(delayEvents.Count == 0 && delayStep.Parameter == "",
    "Delay-only macro needs no key or mouse parameter and emits no input");
Console.WriteLine("Macro repeat, trigger transitions and Delay regressions passed.");


// Each macro step parameter is edited according to its action. Tests
// exercise the pure policy without starting WinForms or injecting input.
Check(MacroStepParameterChoices.MouseButtons.SequenceEqual(
    new[] { "Left", "Right", "Middle", "XButton1", "XButton2" }),
    "mouse dropdown contains Left/Right/Middle plus XButton1/XButton2");
foreach (var mouseKind in new[]
{
    MacroStepKind.MouseDown, MacroStepKind.MouseUp,
    MacroStepKind.MouseClick, MacroStepKind.MouseDoubleClick
})
{
    Check(MacroStepParameterChoices.TypeFor(mouseKind) ==
          MacroStepParameterType.MouseButton,
        $"{mouseKind} selects a mouse button dropdown");
    Check(MacroStepParameterChoices.ParameterFor(mouseKind, "rIgHt") == "Right",
        $"{mouseKind} loads existing mouse button names case-insensitively");
    Check(MacroStepParameterChoices.ParameterFor(mouseKind, "Ctrl",
        MacroStepKind.KeyDown) == "",
        $"{mouseKind} clears incompatible keyboard parameter on action change");
}
foreach (var keyKind in new[]
{
    MacroStepKind.KeyDown, MacroStepKind.KeyUp, MacroStepKind.KeyPress
})
{
    Check(MacroStepParameterChoices.TypeFor(keyKind) ==
          MacroStepParameterType.Keyboard,
        $"{keyKind} retains an editable text key parameter");
}
foreach (var noParameterKind in new[]
{
    MacroStepKind.Delay, MacroStepKind.WheelUp, MacroStepKind.WheelDown
})
{
    Check(MacroStepParameterChoices.TypeFor(noParameterKind) ==
          MacroStepParameterType.None &&
          MacroStepParameterChoices.ParameterFor(noParameterKind, "Left") == "",
        $"{noParameterKind} has a blank read-only parameter");
}
Check(MacroStepParameterChoices.ParameterFor(
    MacroStepKind.MouseUp, "Middle", MacroStepKind.MouseDown) == "Middle",
    "switching between mouse actions retains the selected mouse button");
Check(MacroStepParameterChoices.ParameterFor(
    MacroStepKind.MouseClick, "invalid") == "" &&
      MacroStepParameterChoices.ParameterFor(
    MacroStepKind.MouseClick, "", MacroStepKind.Delay) == "",
    "mouse dropdown never invents an invalid or default mouse button");
Check(MacroStepParameterChoices.ParameterFor(
    MacroStepKind.KeyUp, "Ctrl", MacroStepKind.KeyDown) == "Ctrl",
    "switching keyboard actions retains the existing key");
Check(MacroStepParameterChoices.ParameterFor(
    MacroStepKind.KeyDown, "Left", MacroStepKind.MouseClick) == "",
    "switching mouse to keyboard clears the mouse button instead of interpreting Left as an arrow key");
foreach (string button in MacroStepParameterChoices.MouseButtons)
{
    var validatedMouseStep = new MacroStep
    {
        Kind = MacroStepKind.MouseDoubleClick, Parameter = button
    };
    validatedMouseStep.Validate();
}
bool missingMouseButtonRejected = false;
try
{
    new MacroStep
    {
        Kind = MacroStepKind.MouseClick, Parameter = ""
    }.Validate();
}
catch (ArgumentException) { missingMouseButtonRejected = true; }
Check(missingMouseButtonRejected,
    "macro validation requires selecting a mouse button before saving");
Console.WriteLine("Macro mouse parameter dropdown policy tests passed.");


// XBUTTON1 / XBUTTON2 are physical mouse back/forward buttons.
// Their codes, recorder messages and outbound MOUSEINPUT mouseData must
// remain distinct, including when a recording is stopped while held.
Check(MouseControl.XButton1 != MouseControl.XButton2 &&
      MacroStep.ParseMouse("XButton1") == (int)MouseControl.XButton1 &&
      MacroStep.ParseMouse("XButton2") == (int)MouseControl.XButton2,
    "XBUTTON1 and XBUTTON2 are distinct macro mouse parameters");
Check(MacroStep.ParseMouse("x1") == (int)MouseControl.XButton1 &&
      MacroStep.ParseMouse("x2") == (int)MouseControl.XButton2,
    "X1 and X2 input aliases map to the proper extended mouse buttons");
Check(MacroStepParameterChoices.MouseButtons.SequenceEqual(
    new[] { "Left", "Right", "Middle", "XButton1", "XButton2" }),
    "macro mouse dropdown contains both X buttons after the original three");
Check(MacroStepParameterChoices.ParameterFor(
    MacroStepKind.MouseClick, "xbutton2", MacroStepKind.MouseDown) == "XButton2",
    "XBUTTON2 is canonicalized and preserved between mouse actions");
Check(MouseExtraButtons.DecodeHookButton(1u << 16) == "XButton1" &&
      MouseExtraButtons.DecodeHookButton(2u << 16) == "XButton2" &&
      MouseExtraButtons.DecodeHookButton(3u << 16) is null,
    "mouse hook high-word discriminates XBUTTON1 and XBUTTON2");
Check(MouseExtraButtons.MouseData(MouseControl.XButton1) == 1 &&
      MouseExtraButtons.MouseData(MouseControl.XButton2) == 2,
    "SendInput uses the documented XBUTTON1/2 mouseData identifiers");
bool regularMouseRejected = false;
try { MouseExtraButtons.MouseData(MouseControl.Left); }
catch (ArgumentOutOfRangeException) { regularMouseRejected = true; }
Check(regularMouseRejected, "extended mouseData helper rejects ordinary buttons");

var xRecorded = new MacroRecordingSession();
Check(xRecorded.RecordMouseButton("XButton1", true, 20) &&
      xRecorded.RecordMouseButton("XButton2", true, 30) &&
      xRecorded.RecordMouseButton("XButton1", false, 60) &&
      !xRecorded.RecordMouseButton("XButton1", false, 70),
    "macro recorder captures distinct X-button edges without duplicates");
var xFinished = xRecorded.Finish(200);
Check(xFinished.Count == 4 &&
      xFinished[0].Parameter == "XButton1" &&
      xFinished[1].Parameter == "XButton2" &&
      xFinished[2].Kind == MacroStepKind.MouseUp &&
      xFinished[3].Parameter == "XButton2" &&
      xFinished[3].Kind == MacroStepKind.MouseUp,
    "stopping recording releases any held X button by its correct name");

var xMacro = new MacroDefinition
{
    Name = "X buttons", Mode = MacroMode.Steps,
    Steps = new()
    {
        new() { Kind = MacroStepKind.MouseDown, Parameter = "XButton1" },
        new() { Kind = MacroStepKind.MouseUp, Parameter = "XButton1" },
        new() { Kind = MacroStepKind.MouseClick, Parameter = "XButton2" }
    }
};
var xEvents = new List<InputEvent>();
await MacroPlayback.ExecuteAsync(xMacro, xEvents.Add, _ => { });
Check(xEvents.Select(x => x.Kind).SequenceEqual(new[]
{
    InputEventKind.MouseDown, InputEventKind.MouseUp,
    InputEventKind.MouseDown, InputEventKind.MouseUp
}) && xEvents.Select(x => x.Code).SequenceEqual(new[]
{
    (int)MouseControl.XButton1, (int)MouseControl.XButton1,
    (int)MouseControl.XButton2, (int)MouseControl.XButton2
}), "macro playback preserves X-button press/release and click semantics");

var xProfile = MappingProfile.CreateDefault("X buttons profile");
xProfile.Buttons[0] = new ButtonAction { Kind = MappingKind.MouseXButton1 };
xProfile.Buttons[1] = new ButtonAction { Kind = MappingKind.MouseXButton2 };
var xEngine = new ButtonTransitionEngine(xProfile);
Check(xEngine.Apply(two!).Count(x =>
      x.Kind == InputEventKind.MouseDown &&
      x.Code is (int)MouseControl.XButton1 or (int)MouseControl.XButton2) == 2,
    "G-key mapping sends both extended mouse buttons independently");
Check(xEngine.ReleaseAll().Count(x => x.Kind == InputEventKind.MouseUp) == 2,
    "all held X buttons release on Stop/profile switch");

var rgbProfile = MappingProfile.CreateDefault("RGB status profile");
rgbProfile.UseProfileRgb = true;
rgbProfile.ProfileRgb = 0x123ABC;
rgbProfile.Validate();
var rgbCloned = rgbProfile.Clone("RGB clone");
Check(rgbCloned.UseProfileRgb && rgbCloned.ProfileRgb == 0x123ABC,
    "profile cloning preserves RGB opt-in and 24-bit colour");
var rgbDocument = new ProfilesDocument
{
    SelectedProfile = rgbProfile.Name,
    LcdStatusEnabled = true,
    Profiles = new() { rgbProfile }
};
string rgbJson = System.Text.Json.JsonSerializer.Serialize(rgbDocument);
var rgbRestored = System.Text.Json.JsonSerializer.Deserialize<ProfilesDocument>(rgbJson);
Check(rgbRestored is not null && rgbRestored.LcdStatusEnabled &&
      rgbRestored.Profiles[0].UseProfileRgb &&
      rgbRestored.Profiles[0].ProfileRgb == 0x123ABC,
    "LCD opt-in and per-profile RGB survive profile serialization");
bool badColourRejected = false;
rgbProfile.ProfileRgb = 0x1000000;
try { rgbProfile.Validate(); }
catch (ArgumentException) { badColourRejected = true; }
Check(badColourRejected, "profile rejects colours outside 24-bit RGB");
Console.WriteLine("X-button input and opt-in RGB/LCD profile tests passed.");


// Physical M1/M2/M3 selectors map to button bits 29/30/31. When
// enabled, changing layers selects a separate, persisted mapping
// and never forwards an M-key as a game input.
var m1Frame = new byte[] { 1, 127, 127, 0, 0, 0, 0b0010_0000, 0 };
var m2Frame = new byte[] { 1, 127, 127, 0, 0, 0, 0b0100_0000, 0 };
var m3Frame = new byte[] { 1, 127, 127, 0, 0, 0, 0b1000_0000, 0 };
bool m1Parsed = G13InputReport.TryParse(m1Frame, out var m1Input);
bool m2Parsed = G13InputReport.TryParse(m2Frame, out var m2Input);
bool m3Parsed = G13InputReport.TryParse(m3Frame, out var m3Input);
Check(m1Parsed && m1Input is not null &&
      m2Parsed && m2Input is not null &&
      m3Parsed && m3Input is not null,
    "all three M-mode selector reports parse");
Check(m1Input!.IsPhysicalBitPressed(G13MappingLayers.M1Bit) &&
      m2Input!.IsPhysicalBitPressed(G13MappingLayers.M2Bit) &&
      m3Input!.IsPhysicalBitPressed(G13MappingLayers.M3Bit),
    "M1/M2/M3 are physical report bits 29/30/31");
Check(G13MappingLayers.SelectorBits.SequenceEqual(new[] { 29, 30, 31 }),
    "mode selectors cover exactly M1/M2/M3 (not MR)");

var modeDetector = new G13LayerSelector();
Check(modeDetector.Detect(m2Input!) == 2,
    "M2 rising edge activates layer 2");
Check(modeDetector.Detect(m2Input!) is null,
    "held M2 does not continually reactivate its layer");
Check(modeDetector.Detect(emptyReport!) is null &&
      modeDetector.Detect(m3Input!) == 3,
    "releasing and pressing M3 activates layer 3");
modeDetector.Seed(m1Input!);
Check(modeDetector.Detect(m1Input!) is null,
    "seeding the detector prevents a held M1 key from reactivating after a profile switch");
Check(modeDetector.Detect(emptyReport!) is null &&
      modeDetector.Detect(m1Input!) == 1,
    "M1 is selectable again after physical release and fresh press");

var mMode = new MappingProfile { Name = "M-layer test", UseMappingLayers = true };
mMode.Buttons[0] = new ButtonAction { Kind = MappingKind.Keyboard, Parameter = "A" };
mMode.Buttons[G13MappingLayers.M2Bit] = new ButtonAction
    { Kind = MappingKind.Keyboard, Parameter = "Z" };
mMode.M2Buttons[0] = new ButtonAction { Kind = MappingKind.Keyboard, Parameter = "B" };
mMode.M3Buttons[0] = new ButtonAction { Kind = MappingKind.VJoyButton, Parameter = "1" };
mMode.Validate();
Check(mMode.ForLayer(1).Buttons[0].Parameter == "A" &&
      mMode.ForLayer(2).Buttons[0].Parameter == "B" &&
      mMode.ForLayer(3).Buttons[0].Kind == MappingKind.VJoyButton,
    "each layer returns its own G-key assignments");
Check(mMode.ForLayer(3).GetVirtualButtons(g1!, 32) == 1 &&
      mMode.ForLayer(2).GetVirtualButtons(g1!, 32) == 0,
    "vJoy assignments follow the effective mapping layer");
var m2Engine = new ButtonTransitionEngine(mMode.ForLayer(2));
Check(m2Engine.Apply(g1!).SequenceEqual(new[]
{
    new InputEvent(InputEventKind.KeyDown, 0x42)
}), "M2 G1 key emits its B mapping");
Check(m2Engine.ReleaseAll().SequenceEqual(new[]
{
    new InputEvent(InputEventKind.KeyUp, 0x42)
}), "layer switching can release the previous layer's held keyboard input");
var noMkeys = G13MappingLayers.WithoutModeKeys(m2Input!);
Check(!noMkeys.IsPhysicalBitPressed(G13MappingLayers.M2Bit) &&
      m2Input!.IsPhysicalBitPressed(G13MappingLayers.M2Bit),
    "input filtering removes M mode controls without modifying the original HID report");
var preservedMr = new byte[] { 1, 127, 127, 0, 0, 0, 0, 0b0000_0001 };
G13InputReport.TryParse(preservedMr, out var mrInput);
Check(G13MappingLayers.WithoutModeKeys(mrInput!).IsPhysicalBitPressed(32),
    "MR remains a normal physical input when M layers are enabled");

var mModeClone = mMode.Clone("M-layer clone");
mModeClone.M2Buttons[0].Parameter = "C";
Check(mMode.M2Buttons[0].Parameter == "B" &&
      mModeClone.M2Buttons[0].Parameter == "C",
    "clone produces independent mapping dictionaries for each M layer");
var mDoc = new ProfilesDocument
{
    SelectedProfile = mMode.Name, Profiles = new() { mMode }
};
string mJson = System.Text.Json.JsonSerializer.Serialize(mDoc);
var mReload = System.Text.Json.JsonSerializer.Deserialize<ProfilesDocument>(mJson);
Check(mReload is not null && mReload.Profiles[0].UseMappingLayers &&
      mReload.Profiles[0].M2Buttons[0].Parameter == "B" &&
      mReload.Profiles[0].M3Buttons[0].Kind == MappingKind.VJoyButton,
    "M2/M3 mapping layers persist through profile serialization");
var legacyProfile = System.Text.Json.JsonSerializer.Deserialize<MappingProfile>(
    """{"Name":"Old single-layer","Buttons":{"0":{"Kind":1,"Parameter":"A"}}}""");
Check(legacyProfile is not null && !legacyProfile.UseMappingLayers &&
      legacyProfile.M2Buttons.Count == 0 && legacyProfile.M3Buttons.Count == 0,
    "existing profiles default to disabled layers without data migration");
var invalidLayer = mMode.Clone("Invalid M2");
invalidLayer.M2Buttons[0].Parameter = "NoSuchKey";
bool invalidM2Rejected = false;
try { invalidLayer.Validate(); }
catch (ArgumentException) { invalidM2Rejected = true; }
Check(invalidM2Rejected, "M2 mappings are validated, not only M1 assignments");
Console.WriteLine("M1/M2/M3 layer selector, edge, forwarding and persistence tests passed.");


// Read-only device research: no unidentified firmware report IDs and
// backup contains PC files only, with an explicit scope manifest.
var fakeDevice = new G13DeviceInfo(
    @"\\?\hid#vid_046d&pid_c21c#test", 0x046D, 0xC21C, 1, 8, 992, 5);
bool blockedUnknownFeature = false;
try { G13OutputDevice.ReadKnownFeature(fakeDevice, 0x41); }
catch (ArgumentOutOfRangeException) { blockedUnknownFeature = true; }
Check(blockedUnknownFeature,
    "read-only diagnostics forbid probing unverified HID feature IDs");

var testDeviceSnapshot = new G13ReadOnlySnapshot(1,
    DateTimeOffset.UtcNow, G13ReadOnlySnapshot.SnapshotScope,
    new[]
    {
        new G13InterfaceSnapshot(fakeDevice.Path, fakeDevice.VendorId,
            fakeDevice.ProductId, fakeDevice.Version, 8, 992, 5, 0xFF00, 1,
            new[]
            {
                new G13KnownFeatureSnapshot("Live RGB backlight", 7,
                    true, "075AFF6E00", null)
            })
    });
var snapshotJson = testDeviceSnapshot.ToJson();
Check(snapshotJson.Contains("READ-ONLY HID") &&
      snapshotJson.Contains("NOT contain stored onboard") &&
      snapshotJson.Contains("075AFF6E00") &&
      !snapshotJson.Contains("firmware memory backup"),
    "device snapshot explains scope and records known feature bytes");

string tempProfiles = Path.Combine(Path.GetTempPath(),
    "G13Reborn-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(tempProfiles);
    string profileFile = Path.Combine(tempProfiles, "profiles.json");
    string lgsDirectory = Path.Combine(tempProfiles, "LGS");
    Directory.CreateDirectory(Path.Combine(lgsDirectory, "Shooter"));
    File.WriteAllText(profileFile, "{\"G13Local\":true}");
    File.WriteAllText(profileFile + ".bak", "previous");
    File.WriteAllText(Path.Combine(lgsDirectory, "Shooter", "game.xml"),
        "<Profile Name=\"Shooter\" />");
    string zipPath = Path.Combine(tempProfiles, "local-backup.zip");
    var copied = G13LocalProfileBackup.Create(
        zipPath, profileFile, lgsDirectory);
    Check(copied.FileCount == 3 && File.Exists(zipPath),
        "backup includes G13Reborn live + previous and local LGS profiles");

    using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
    {
        var entries = zip.Entries.Select(x => x.FullName).ToArray();
        Check(entries.Contains("G13Reborn/profiles.json") &&
              entries.Contains("G13Reborn/profiles.json.bak") &&
              entries.Contains("Logitech Gaming Software/Profiles/Shooter/game.xml") &&
              entries.Contains("backup-manifest.json"),
            "backup includes exactly labelled host-side profile paths and manifest");
        using var manifestReader = new StreamReader(
            zip.GetEntry("backup-manifest.json")!.Open());
        string manifestText = manifestReader.ReadToEnd();
        Check(manifestText.Contains("HOST FILE BACKUP ONLY") &&
              manifestText.Contains("Does NOT contain G13 firmware"),
            "archive manifest expressly disclaims onboard profile data");
    }
}
finally
{
    if (Directory.Exists(tempProfiles))
        Directory.Delete(tempProfiles, recursive: true);
}
Console.WriteLine("Read-only diagnostic and local profile ZIP backup tests passed.");


// Read-only Raw Input diagnostics never assume PID C232 identifies the
// LGS bus rather than the G HUB bus: both use Logitech virtual HID PIDs.
Check(RawKeyboardDeviceLabel.Describe(
    @"\\?\HID#VID_046D&PID_C232#1&abcd") ==
    "Logitech virtual keyboard (C232; LGS/G HUB unknown)",
    "C232 keyboard raw input is classified as virtual, without guessing LGS versus G HUB");
Check(RawKeyboardDeviceLabel.Describe(
    @"\\?\HID#VID_046D&PID_C539#1&abcd") ==
    "Other Logitech HID keyboard",
    "different Logitech keyboard is not mislabeled as a virtual device");
Check(RawKeyboardDeviceLabel.Describe(
    @"\\?\HID#VID_1234&PID_9876#1&abcd") == "Other keyboard" &&
    RawKeyboardDeviceLabel.Describe(null) == "Unidentified keyboard",
    "non-Logitech and missing paths are labeled conservatively");
Console.WriteLine("Read-only Raw Input device classification tests passed.");
