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
