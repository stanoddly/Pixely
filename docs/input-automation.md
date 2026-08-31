# Input automation

`IInputAutomation` synchronously delivers synthetic mouse, keyboard, and text input through the ordinary Pixely input services. Existing view-scoped subscriptions, priorities, consumption, and device state apply to automated input. Handlers finish before an automation method returns.

Resolve the application-lifetime service from the app:

```csharp
IInputAutomation input = app.GetRequiredService<IInputAutomation>();
```

Mouse positions use logical coordinates relative to the target window's top-left corner. `MouseMoveTo` moves to a window position and derives the relative motion from the synthetic mouse's previous position. `MouseMoveBy` applies a delta to that previous position.

```csharp
input.MouseMoveTo(new Vector2(320, 180));
input.MouseMoveBy(new Vector2(10, -5));
input.MouseClick(MouseButton.Left, new Vector2(330, 175));
input.MouseWheel(new Vector2(0, -1), new Vector2(330, 175));
```

Use explicit down and up calls for held input:

```csharp
input.KeyDown(Scancode.W);
input.KeyUp(Scancode.W);

input.MouseDown(MouseButton.Left, new Vector2(100, 100));
input.MouseMoveTo(new Vector2(200, 100));
input.MouseUp(MouseButton.Left, new Vector2(200, 100));
```

`KeyPress` dispatches a key down followed by a key up. `TextInput` delivers text directly and supports characters that do not have a corresponding keyboard scancode.

The default `ViewScope` targets the ordinary single-window application. Pass a scope explicitly for another registered window.

Automated input affects Pixely's event-derived synthetic device state. It does not move the operating-system cursor, change window focus, or modify SDL's physical/global device state.
