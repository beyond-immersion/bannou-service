using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Platform;

/// <summary>
/// Converts paths between WSL (Linux) and Windows formats.
/// Required when running in WSL but invoking Windows tools (like Stride compiler).
/// </summary>
public static class WslPathConverter
{
    private static bool? _isWsl;

    /// <summary>
    /// Gets whether the current environment is WSL.
    /// </summary>
    public static bool IsWsl
    {
        get
        {
            if (_isWsl.HasValue)
                return _isWsl.Value;

            // Only check if we're on Linux
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _isWsl = false;
                return false;
            }

            // Check for WSL-specific indicators
            try
            {
                // WSL sets WSL_DISTRO_NAME environment variable
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")))
                {
                    _isWsl = true;
                    return true;
                }

                // Also check /proc/version for "microsoft" or "WSL"
                if (File.Exists("/proc/version"))
                {
                    var version = File.ReadAllText("/proc/version");
                    if (version.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
                        version.Contains("WSL", StringComparison.OrdinalIgnoreCase))
                    {
                        _isWsl = true;
                        return true;
                    }
                }
            }
            catch
            {
                // Fall through to false
            }

            _isWsl = false;
            return false;
        }
    }

    /// <summary>
    /// Converts a Unix/WSL path to a Windows path.
    /// </summary>
    /// <param name="path">The Unix path to convert.</param>
    /// <returns>The Windows-formatted path, or the original path if conversion is not needed/possible.</returns>
    public static string ToWindowsPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // If not on WSL, return path unchanged
        if (!IsWsl)
            return path;

        // Handle /mnt/X/ paths (Windows drives mounted in WSL)
        // Example: /mnt/c/Users/... -> C:\Users\...
        if (path.StartsWith("/mnt/", StringComparison.OrdinalIgnoreCase) && path.Length > 6)
        {
            var driveLetter = char.ToUpperInvariant(path[5]);
            if (char.IsLetter(driveLetter))
            {
                var remainder = path.Substring(6).Replace('/', '\\');
                return $"{driveLetter}:{remainder}";
            }
        }

        // For native WSL paths (like /tmp, /home), use wslpath to convert
        return ConvertWithWslPath(path, toWindows: true) ?? path;
    }

    /// <summary>
    /// Converts a Windows path to a Unix/WSL path.
    /// </summary>
    /// <param name="path">The Windows path to convert.</param>
    /// <returns>The Unix-formatted path, or the original path if conversion is not needed/possible.</returns>
    public static string ToUnixPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // If not on WSL, return path unchanged
        if (!IsWsl)
            return path;

        // Handle drive letter paths (C:\Users\... -> /mnt/c/Users/...)
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            var driveLetter = char.ToLowerInvariant(path[0]);
            var remainder = path.Substring(2).Replace('\\', '/');
            return $"/mnt/{driveLetter}{remainder}";
        }

        // Handle UNC paths with wslpath
        return ConvertWithWslPath(path, toWindows: false) ?? path;
    }

    /// <summary>
    /// Creates a command string for running a Windows command from WSL.
    /// Uses cmd.exe /c with pushd/popd for directory context.
    /// </summary>
    /// <param name="workingDirectory">The working directory (Unix path).</param>
    /// <param name="command">The command to run.</param>
    /// <returns>A tuple of (executable, arguments) for Process.Start.</returns>
    public static (string Executable, string Arguments) CreateWindowsCommand(
        string workingDirectory,
        string command)
    {
        if (!IsWsl)
        {
            // On Windows, just return the command directly
            // Caller should use cmd.exe /c or similar
            return ("cmd.exe", $"/c {command}");
        }

        var windowsDir = ToWindowsPath(workingDirectory);

        // Use pushd/popd because pushd can handle UNC paths by creating
        // a temporary drive mapping, whereas cd cannot
        var fullCommand = $"pushd \"{windowsDir}\" && {command} && popd";

        return ("cmd.exe", $"/c {fullCommand}");
    }

    /// <summary>
    /// Creates ProcessStartInfo configured for running Windows commands from WSL.
    /// </summary>
    /// <param name="workingDirectory">The working directory (Unix path).</param>
    /// <param name="command">The command to run.</param>
    /// <returns>A configured ProcessStartInfo.</returns>
    public static ProcessStartInfo CreateWindowsProcessStartInfo(
        string workingDirectory,
        string command)
    {
        var (executable, arguments) = CreateWindowsCommand(workingDirectory, command);

        return new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    private static string? ConvertWithWslPath(string path, bool toWindows)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wslpath",
                Arguments = toWindows ? $"-w \"{path}\"" : $"-u \"{path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                return output;
        }
        catch
        {
            // Fall through to return null
        }

        return null;
    }
}
