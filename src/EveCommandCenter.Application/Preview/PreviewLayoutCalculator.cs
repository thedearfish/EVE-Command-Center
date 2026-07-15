namespace EveCommandCenter.Application.Preview;

public static class PreviewLayoutCalculator
{
    public static PreviewRectangle Fit(
        int sourceWidth,
        int sourceHeight,
        PreviewRectangle available)
    {
        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }

        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        }

        if (available.Width <= 0 || available.Height <= 0)
        {
            return new PreviewRectangle(available.Left, available.Top, 0, 0);
        }

        double scale = Math.Min(
            (double)available.Width / sourceWidth,
            (double)available.Height / sourceHeight);

        int width = Math.Max(1, (int)Math.Floor(sourceWidth * scale));
        int height = Math.Max(1, (int)Math.Floor(sourceHeight * scale));
        int left = available.Left + ((available.Width - width) / 2);
        int top = available.Top + ((available.Height - height) / 2);

        return new PreviewRectangle(left, top, width, height);
    }
}

public readonly record struct PreviewRectangle(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);

    public int Bottom => checked(Top + Height);
}
