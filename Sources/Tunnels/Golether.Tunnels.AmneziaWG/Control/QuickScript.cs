using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// The short shell script that brings a tunnel up or down with <c>awg-quick</c> as root on Linux and macOS.
/// </summary>
/// <remarks>
/// <c>awg-quick</c> runs the commands of a configuration (<c>PreUp</c>, <c>PostUp</c>, <c>PreDown</c>,
/// <c>PostDown</c>) as root, and the configuration written by the application lies in the data folder, which every
/// program of the user can change. So the script never hands that file over: it copies it into
/// <see cref="SYSTEM_DIRECTORY"/>, which only root can write, checks the copy line by line against the keys the
/// application writes and starts <c>awg-quick</c> on the copy. What is checked is what runs. The script is fixed text;
/// the paths and names reach it as arguments, never as part of the text.
/// </remarks>
public static class QuickScript
{
    /// <summary>
    /// The folder of the configurations of the tunnels, writable by root only.
    /// </summary>
    public const string SYSTEM_DIRECTORY = "/etc/amnezia/amneziawg";

    /// <summary>
    /// The exit code of the script for a configuration that is not one the application writes (<c>EX_DATAERR</c>).
    /// </summary>
    public const int REJECTED = 65;

    /// <summary>
    /// The name the script runs under (<c>$0</c>), seen in the list of processes.
    /// </summary>
    public const string NAME = "golether-tunnel";

    /// <summary>
    /// The script: arguments are the configuration of the application, the name of the tunnel, the verb, the
    /// <c>awg-quick</c> program and the folder of the copies. Only empty lines, comments, the two section headers and
    /// the keys the application writes may be in a configuration; one line more and nothing runs. "up" first takes down
    /// a tunnel of the same name, so a changed configuration takes effect with one password prompt.
    /// </summary>
    public const string TEXT =
        "set -eu; src=$1; name=$2; verb=$3; quick=$4; dir=$5; umask 077; mkdir -p -- \"$dir\"; target=\"$dir/$name.conf\"; " +
        "if [ \"$verb\" = up ] || [ ! -f \"$target\" ]; then " +
        "if [ -L \"$src\" ] || [ ! -f \"$src\" ]; then exit 65; fi; " +
        "cat -- \"$src\" > \"$target.new\"; " +
        "if grep -Eiqv '^[[:space:]]*($|#|\\[(interface|peer)\\][[:space:]]*$|(privatekey|address|listenport|mtu|jc|jmin|jmax|s1|s2|h1|h2|h3|h4|publickey|presharedkey|allowedips|endpoint|persistentkeepalive)[[:space:]]*=)' \"$target.new\"; " +
        "then rm -f -- \"$target.new\"; exit 65; fi; " +
        "mv -f -- \"$target.new\" \"$target\"; fi; " +
        "if [ \"$verb\" = up ]; then \"$quick\" down \"$target\" >/dev/null 2>&1 || true; fi; " +
        "\"$quick\" \"$verb\" \"$target\"; " +
        "if [ \"$verb\" = down ]; then rm -f -- \"$target\"; fi";

    /// <summary>
    /// Builds the arguments of <c>/bin/sh</c> for a change of a tunnel.
    /// </summary>
    /// <param name="verb"><c>up</c> or <c>down</c>.</param>
    /// <param name="configuration">The configuration file the application wrote.</param>
    /// <param name="interfaceName">The name of the tunnel.</param>
    /// <param name="quickExecutable">The <c>awg-quick</c> program.</param>
    /// <param name="systemDirectory">The folder of the copies; <see cref="SYSTEM_DIRECTORY"/> outside the tests.</param>
    /// <returns>The arguments, the script first.</returns>
    public static string[] BuildArguments(string verb, string configuration, string interfaceName, string quickExecutable, string systemDirectory = SYSTEM_DIRECTORY)
    {
        if (verb is not ("up" or "down"))
        {
            throw new ArgumentException("The verb must be up or down.", nameof(verb));
        }

        TunnelNames.Validate(interfaceName);
        return ["-c", TEXT, NAME, configuration, interfaceName, verb, quickExecutable, systemDirectory];
    }
}
