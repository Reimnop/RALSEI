namespace Ralsei.Rendering;

public interface IRenderer
{
    void ProcessFrame(IDrawDataProvider drawDataProvider);
}