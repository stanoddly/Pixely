using System.Numerics;
using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Gamepad;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultRendering(
                new WindowConfig(Size: (640, 480), Title: "Gamepad Tutorial"));

        builder.OnStart((IGamepadService gamepadService) =>
        {
            Console.WriteLine($"Gamepads connected at startup: {gamepadService.Gamepads.Count}");
            foreach (Pixely.Input.Gamepad gamepad in gamepadService.Gamepads)
            {
                Console.WriteLine($"  Gamepad {gamepad.DeviceId}");
            }

            if (gamepadService.Gamepads.Count == 0)
            {
                Console.WriteLine("No gamepads detected. Connect a gamepad and it will be picked up automatically.");
            }

            Console.WriteLine("Listening for gamepad input...");

            gamepadService.LeftStickMotion += motion =>
            {
                Vector2 value = motion.Value;
                Console.WriteLine($"[Gamepad {motion.Gamepad.DeviceId}] Left Stick: ({value.X:F2}, {value.Y:F2})");
            };

            gamepadService.RightStickMotion += motion =>
            {
                Vector2 value = motion.Value;
                Console.WriteLine($"[Gamepad {motion.Gamepad.DeviceId}] Right Stick: ({value.X:F2}, {value.Y:F2})");
            };

            gamepadService.LeftTriggerMotion += motion =>
            {
                Console.WriteLine($"[Gamepad {motion.Gamepad.DeviceId}] Left Trigger: {motion.Value:F2}");
            };

            gamepadService.RightTriggerMotion += motion =>
            {
                Console.WriteLine($"[Gamepad {motion.Gamepad.DeviceId}] Right Trigger: {motion.Value:F2}");
            };

            gamepadService.ButtonPress += button =>
            {
                Console.WriteLine($"[Gamepad {button.Gamepad.DeviceId}] Button Pressed: {button}");
            };

            gamepadService.ButtonRelease += button =>
            {
                Console.WriteLine($"[Gamepad {button.Gamepad.DeviceId}] Button Released: {button}");
            };

            gamepadService.GamepadConnected += gamepad =>
            {
                Console.WriteLine($"[Gamepad {gamepad.DeviceId}] Connected");
            };

            gamepadService.GamepadDisconnected += gamepad =>
            {
                Console.WriteLine($"[Gamepad {gamepad.DeviceId}] Disconnected");
            };
        });

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
