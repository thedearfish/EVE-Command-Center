using EveCommandCenter.Application.Preview;

namespace EveCommandCenter.Application.Tests;

public sealed class PreviewLayoutCalculatorTests
{
    [Fact]
    public void Fit_centers_widescreen_source_inside_square_area()
    {
        var available = new PreviewRectangle(10, 20, 400, 400);

        PreviewRectangle result = PreviewLayoutCalculator.Fit(1920, 1080, available);

        Assert.Equal(10, result.Left);
        Assert.Equal(107, result.Top);
        Assert.Equal(400, result.Width);
        Assert.Equal(225, result.Height);
    }

    [Fact]
    public void Fit_centers_portrait_source_inside_widescreen_area()
    {
        var available = new PreviewRectangle(0, 0, 800, 450);

        PreviewRectangle result = PreviewLayoutCalculator.Fit(1080, 1920, available);

        Assert.Equal(273, result.Left);
        Assert.Equal(0, result.Top);
        Assert.Equal(253, result.Width);
        Assert.Equal(450, result.Height);
    }

    [Fact]
    public void Fit_returns_empty_rectangle_for_empty_available_area()
    {
        var available = new PreviewRectangle(5, 7, 0, 100);

        PreviewRectangle result = PreviewLayoutCalculator.Fit(1920, 1080, available);

        Assert.Equal(new PreviewRectangle(5, 7, 0, 0), result);
    }
}
