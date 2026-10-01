namespace MonitorCenter;

internal sealed record CommandLineOptions(bool IsStartupLaunch)
{
    public static CommandLineOptions Parse(IEnumerable<string> arguments)
    {
        var startup = arguments.Any(argument =>
            string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));

        return new CommandLineOptions(startup);
    }
}
