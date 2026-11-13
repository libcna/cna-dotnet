namespace CNA.Integration.Tests;

/// <summary>
/// The test host never calls this. A test that needs a process of its own -- one without a
/// display -- runs this assembly again with a command and reads what it prints.
/// </summary>
internal static class Program
{
    internal const string ConstructGame = "construct-game";
    internal const string ParentRelativeContentRoot = "parent-relative-content-root";

    private static int Main(string[] args)
    {
        if (args is [ParentRelativeContentRoot])
        {
            try
            {
                CnaDotnet.Integration.Tests.Compat.CompatResourceContentTests.CheckParentRelativeContentRoot();
                Console.WriteLine("title-root-ok");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        if (args is not [ConstructGame])
        {
            return 2;
        }

        try
        {
            using var game = new Microsoft.Xna.Framework.Game();
            Console.WriteLine("constructed");
            // Which renderer built it (CNA plans/plan_apple_m4.md AM4-224): a renderer that draws on
            // the CPU needs no display, and the parent has to tell that from a defect.
            // ApplyChanges creates the device before the run, where XNA's created the device a
            // game's constructor then reads.
            var manager = new Microsoft.Xna.Framework.GraphicsDeviceManager(game);
            manager.ApplyChanges();
            Console.WriteLine(manager.GraphicsDevice is { } device
                ? CNA.XnaCompat.Extensions.CnaGraphicsDeviceExtensions.GetCnaRendererName(device)
                : "no-device");
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.GetType().FullName);
            Console.WriteLine(exception.Message);
        }

        return 0;
    }
}
