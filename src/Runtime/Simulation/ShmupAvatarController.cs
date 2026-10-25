using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

/// <summary>Game-owned circle avatar/character controller. Movement is free and bounded, not
/// a rigid-body response solver; native circle overlaps/sweeps provide contact damage facts.</summary>
internal sealed class ShmupAvatarController
{
    private readonly ShmupSettings settings;
    internal double2 Position { get; private set; }
    internal double2 PreviousPosition { get; private set; }
    internal double2 Aim { get; private set; }
    internal int Health { get; private set; }
    internal double InvulnerabilityRemaining { get; private set; }
    internal ShmupAvatarController(ShmupSettings settings) => this.settings = settings;
    internal void Reset()
    {
        double r = settings.Avatar.Radius;
        Position = PreviousPosition = new(settings.Arena.Width / 2, Math.Clamp(settings.Arena.Height - 2 * r, r, settings.Arena.Height - r));
        Aim = new(0, -1); Health = settings.Avatar.Health; InvulnerabilityRemaining = 0;
    }
    internal void Advance(ShmupCommand command, double dt, double width)
    {
        PreviousPosition = Position;
        InvulnerabilityRemaining = Math.Max(0, InvulnerabilityRemaining - dt);
        if (command.AimDirection is { } direction && Arena.Finite(direction) && (direction.X != 0 || direction.Y != 0)) Aim = Arena.Normalize(direction);
        double2 move = Arena.Finite(command.MoveDirection) ? command.MoveDirection : default;
        double magnitude = Math.Max(Math.Abs(move.X), Math.Abs(move.Y));
        if (magnitude > 1 || (magnitude > 0 && move.X * move.X + move.Y * move.Y > 1)) move = Arena.Normalize(move);
        double2 next = Position + move * (settings.Avatar.MoveSpeed * dt);
        double left = (settings.Arena.Width - width) / 2, r = settings.Avatar.Radius;
        Position = new(Math.Clamp(next.X, left + r, left + width - r), Math.Clamp(next.Y, r, settings.Arena.Height - r));
    }
    internal void Contact(bool touching, double dt)
    {
        if (!touching || InvulnerabilityRemaining > dt * 1e-9) return;
        Damage(settings.Avatar.ContactDamage); InvulnerabilityRemaining = settings.Avatar.InvulnerabilitySeconds;
    }
    internal void Damage(int damage) => Health = Math.Max(0, Health - damage);
}
