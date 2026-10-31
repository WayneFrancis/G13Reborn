using G13Reborn.Device;
using G13Reborn.VirtualController;

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
