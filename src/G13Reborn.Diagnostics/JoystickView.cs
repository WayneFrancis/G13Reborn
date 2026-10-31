using System.Drawing.Drawing2D;

namespace G13Reborn.Diagnostics;

/// <summary>Plots raw 0-255 coordinates; no calibration or deadzone.</summary>
internal sealed class JoystickView : Control
{
    private byte _x = 127;
    private byte _y = 127;

    public JoystickView()
    {
        DoubleBuffered = true;
        MinimumSize = new Size(150, 150);
    }

    public void SetPosition(byte x, byte y)
    {
        _x = x;
        _y = y;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float side = Math.Max(40, Math.Min(ClientSize.Width, ClientSize.Height) - 22);
        float left = (ClientSize.Width - side) / 2f;
        float top = (ClientSize.Height - side) / 2f;
        using var border = new Pen(Color.DimGray, 1.5f);
        using var axes = new Pen(Color.LightGray);
        using var dot = new SolidBrush(Color.CornflowerBlue);
        e.Graphics.DrawEllipse(border, left, top, side, side);
        e.Graphics.DrawLine(axes, left + side / 2, top, left + side / 2, top + side);
        e.Graphics.DrawLine(axes, left, top + side / 2, left + side, top + side / 2);
        float x = left + _x / 255f * side;
        float y = top + _y / 255f * side;
        e.Graphics.FillEllipse(dot, x - 6, y - 6, 12, 12);
    }
}
