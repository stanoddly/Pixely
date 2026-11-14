using Pixely.App;
using Pixely.Audio;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Text;
using Pixely.Ui;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Testing.Tests;

public class TestAppSdlTests
{
    [Test]
    public void BuildRunAndDispose_WithEveryService_NeverStartSdl()
    {
        List<SDL_InitFlags> initialized = [SDL_WasInit(0)];
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.UseDefaultRendering(new WindowConfig(Size: (200, 100)));
        builder.UseUi();
        builder.RegisterAudio();
        builder.OnBuilt(provider =>
        {
            UiRoot root = provider.GetUiRoot();
            root.Style = new UiStyle(provider.GetRequiredService<IFontSystem>().Load("fonts/Any.ttf", 12));
            root.AddLayer(new Label("Score"));
        });

        using (IPixelyApp app = builder.Build())
        {
            initialized.Add(SDL_WasInit(0));
            TestInput input = app.GetRequiredService<TestInput>();
            input.KeyPress(Scancode.Space);
            input.MouseClick(MouseButton.Left, new System.Numerics.Vector2(10, 10));
            input.GamepadConnect();
            input.TextInput("a");
            app.GetRequiredService<ITextInputService>().Start();
            app.GetRequiredService<ICursorService>().IsCursorVisible = false;
            app.GetRequiredService<IClipboardService>().SetText("copied");
            app.GetRequiredService<IDisplayService>().GetDisplays();
            app.GetRequiredService<IPlatformSettings>().GetPreferredLocales();
            app.GetRequiredService<IMouseService>().GetGlobalState();
            IAudioSystem audio = app.GetRequiredService<IAudioSystem>();
            AudioSource source = audio.CreateSource();
            source.Clip = audio.LoadBuffer("sounds/Any.wav");
            source.Play();
            Window window = app.GetRequiredService<Window>();
            window.Size = (300, 200);
            app.RunFrame();
            app.RunFrame();
            initialized.Add(SDL_WasInit(0));
        }

        initialized.Add(SDL_WasInit(0));
        Assert.That(initialized, Is.All.EqualTo((SDL_InitFlags)0));
    }
}
