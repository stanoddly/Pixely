using XdlSharp;

namespace Pixely.Input;

public enum GamepadButton
{
    Invalid = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID,
    South = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH,
    East = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST,
    West = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST,
    North = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH,
    Back = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK,
    Guide = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE,
    Start = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START,
    LeftStick = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK,
    RightStick = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK,
    LeftShoulder = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER,
    RightShoulder = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER,
    DPadUp = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP,
    DPadDown = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN,
    DPadLeft = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT,
    DPadRight = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT,
    Misc1 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC1,
    RightPaddle1 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE1,
    LeftPaddle1 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE1,
    RightPaddle2 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE2,
    LeftPaddle2 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE2,
    Touchpad = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_TOUCHPAD,
    Misc2 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC2,
    Misc3 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC3,
    Misc4 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC4,
    Misc5 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC5,
    Misc6 = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC6,
    Count = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT
}
