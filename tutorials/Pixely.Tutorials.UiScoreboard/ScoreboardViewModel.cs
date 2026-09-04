using Pixely.Ui;

namespace Pixely.Tutorials.UiScoreboard;

/// <summary>
/// View-side state. It knows nothing about elements: it raises <see cref="Changed"/>, and the view
/// decides what that means on screen.
/// </summary>
public sealed class ScoreboardViewModel : IUiViewModel
{
    public const int MaxHealth = 100;

    private int _score;
    private int _lives = 3;
    private int _health = MaxHealth;
    private TimeSpan _elapsed;

    public event Action? Changed;

    public int Score
    {
        get => _score;
        set => Set(ref _score, value);
    }

    public int Lives
    {
        get => _lives;
        set => Set(ref _lives, Math.Max(0, value));
    }

    public int Health
    {
        get => _health;
        set => Set(ref _health, Math.Clamp(value, 0, MaxHealth));
    }

    public TimeSpan Elapsed
    {
        get => _elapsed;
        set => Set(ref _elapsed, value);
    }

    public float HealthFraction => _health / (float)MaxHealth;

    public bool IsGameOver => _lives <= 0;

    /// <summary>Takes health, and spends a life to refill when it runs out.</summary>
    public void Damage(int amount)
    {
        if (IsGameOver)
        {
            return;
        }

        Health -= amount;

        if (_health == 0)
        {
            Lives -= 1;

            if (!IsGameOver)
            {
                Health = MaxHealth;
            }
        }
    }

    public void Reset()
    {
        Score = 0;
        Lives = 3;
        Health = MaxHealth;
        Elapsed = TimeSpan.Zero;
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Changed?.Invoke();
    }
}
