using Pixely.Models;

namespace Pixely.Tutorials.SkinnedModel;

// Plays the model's first animation on one instance. The renderer draws that instance three times, at different placements.
public sealed class AnimatedModel : IUpdatable, IDisposable
{
    private readonly FrameClock _frameClock;

    private AnimatedModel(ModelInstance instance, FrameClock frameClock)
    {
        Instance = instance;
        _frameClock = frameClock;
    }

    public ModelInstance Instance { get; }

    public void Update()
    {
        if (!Instance.Model.Animations.IsEmpty)
        {
            Instance.Animate(Instance.Model.Animations[0], _frameClock.ElapsedTime);
        }
    }

    public void Dispose()
    {
        Instance.Dispose();
        Instance.Model.Dispose();
    }

    public static AnimatedModel Create(IModelLoader modelLoader, ModelInstanceFactory modelInstanceFactory, FrameClock frameClock)
    {
        Model model = modelLoader.Load("models/CesiumMan.glb");
        try
        {
            return new AnimatedModel(modelInstanceFactory.Create(model), frameClock);
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }
}
