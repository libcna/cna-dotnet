namespace CNA.XnaCompat.Extensions;

/// <summary>CNA's host-driven game loop, outside the strict XNA facade.</summary>
public static class CnaGameExtensions
{
    /// <summary>
    /// Initializes and advances a host-driven run by one frame. After Exit, ends the run and
    /// returns false; later calls do nothing. XNA's Game.RunOneFrame only ticks.
    /// </summary>
    public static bool RunFrameEXT(this Microsoft.Xna.Framework.Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Backend.RunFrameEXT();
    }
}
