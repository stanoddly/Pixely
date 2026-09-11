using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Ui;

namespace Pixely.Tutorials.Hotbar;

/// <summary>
/// A row of slots that reacts to the pointer and the number keys. The slots are a custom element,
/// which is what a control needs when it wants a look or a pointer behaviour the built-in ones do
/// not have: here hover has to be reported upwards, so a label can follow it.
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultContent()
            .UseDefaultRendering(new WindowConfig(Size: (1280, 720), Title: "Hotbar"));

        builder.UseUi();
        builder.AddSingleton(new HotbarViewModel());
        builder.AddSingleton<IUiView, HotbarView>();

        builder.OnStart((IKeyboardService keyboardService, HotbarViewModel viewModel) =>
        {
            // The UI subscribes ahead of this and takes nothing from the keyboard here, so every
            // number key comes through. Repeats are ignored: holding 3 selects slot 3 once.
            keyboardService.KeyDown += eventArgs =>
            {
                int index = eventArgs.Scancode - Scancode.Number1;
                if (index >= 0 && index < HotbarViewModel.SlotCount && !eventArgs.Repeat)
                {
                    viewModel.SelectedSlot = index;
                }
            };
        });

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
