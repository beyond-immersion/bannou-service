using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Validates NuGet package references and project file hygiene across all service projects.
/// <para>
/// Three categories of validation:
/// <list type="number">
///   <item><b>Version freshness</b> — flags packages with newer stable versions on nuget.org</item>
///   <item><b>Duplicate detection</b> — flags plugin packages already provided transitively by bannou-service</item>
///   <item><b>Unused detection</b> — flags plugin-specific packages with no matching <c>using</c> directives in source</item>
/// </list>
/// </para>
/// <para>
/// All tests are gated by <see cref="SkipUnless.InformationalTest"/> and require explicit opt-in:
/// <code>BANNOU_RUN_INFORMATIONAL_TESTS=true dotnet test structural-tests/ --filter "ProjectValidation"</code>
/// The version freshness test additionally requires network access to query the NuGet V3 flat container API.
/// </para>
/// </summary>
public class ProjectValidationTests
{
    /// <summary>
    /// Shared HttpClient for NuGet V3 flat container API queries.
    /// The flat container API is public and requires no authentication.
    /// </summary>
    private static readonly HttpClient NuGetClient = new()
    {
        BaseAddress = new Uri("https://api.nuget.org/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Overrides for packages whose primary namespace differs from the package ID.
    /// Packages not listed here use the package ID itself as the namespace prefix for
    /// <c>using</c>-directive matching. Empty arrays indicate build-time-only packages
    /// that have no runtime namespace (excluded from unused detection).
    /// </summary>
    private static readonly Dictionary<string, string[]> PackageNamespaceOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // AWS SDKs use Amazon.* namespaces
        ["AWSSDK.S3"] = ["Amazon.S3"],
        ["AWSSDK.SimpleEmailV2"] = ["Amazon.SimpleEmailV2"],
        // Package name != namespace
        ["BCrypt.Net-Next"] = ["BCrypt.Net"],
        ["Otp.NET"] = ["OtpNet"],
        ["KubernetesClient"] = ["k8s"],
        ["MailKit"] = ["MailKit", "MimeKit"],
        ["Fluid.Core"] = ["Fluid"],
        ["JsonPatch.Net"] = ["Json.Patch"],
        // OpenTelemetry packages all use OpenTelemetry.* namespaces
        ["OpenTelemetry.Extensions.Hosting"] = ["OpenTelemetry"],
        ["OpenTelemetry.Instrumentation.AspNetCore"] = ["OpenTelemetry"],
        ["OpenTelemetry.Instrumentation.Http"] = ["OpenTelemetry"],
        ["OpenTelemetry.Exporter.OpenTelemetryProtocol"] = ["OpenTelemetry"],
        ["OpenTelemetry.Exporter.Prometheus.AspNetCore"] = ["OpenTelemetry"],
        // EF Core providers share Microsoft.EntityFrameworkCore namespace
        ["Pomelo.EntityFrameworkCore.MySql"] = ["Pomelo.EntityFrameworkCore.MySql", "Microsoft.EntityFrameworkCore"],
        ["Microsoft.EntityFrameworkCore.Sqlite"] = ["Microsoft.EntityFrameworkCore"],
        // Framework packages with different namespace roots
        ["Microsoft.AspNetCore.Mvc.Core"] = ["Microsoft.AspNetCore.Mvc"],
        ["Microsoft.AspNetCore.WebSockets"] = ["System.Net.WebSockets", "Microsoft.AspNetCore.WebSockets"],
        ["System.ComponentModel.Annotations"] = ["System.ComponentModel.DataAnnotations"],
        ["System.Threading.Channels"] = ["System.Threading.Channels"],
        // Build-time only (no runtime namespace)
        ["NSwag.MSBuild"] = [],
        ["Microsoft.VisualStudio.Azure.Containers.Tools.Targets"] = [],
    };

    /// <summary>
    /// Checks all NuGet package references across bannou-service and plugin projects against
    /// the latest stable versions on nuget.org. Reports outdated packages, version mismatches
    /// across projects, and major-version updates available for wildcard-pinned packages.
    /// Requires network access to the NuGet V3 flat container API (public, no auth).
    /// </summary>
    [Fact]
    public async Task PackageReferences_AreLatestStableVersions()
    {
        SkipUnless.InformationalTest("NuGet version freshness check — requires network access");

        var allPackages = CollectAllPackageReferences();
        var grouped = allPackages
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var findings = new List<string>();

        foreach (var group in grouped)
        {
            var packageId = group.Key;
            var entries = group.ToList();

            // Flag version inconsistencies across projects
            var distinctVersions = entries.Select(e => e.Version).Distinct().ToList();
            if (distinctVersions.Count > 1)
            {
                findings.Add($"  MISMATCH: {packageId} — " +
                    string.Join(", ", entries.Select(e => $"{e.Version} ({e.Source})")));
            }

            var currentVersion = entries[0].Version;

            // Query NuGet for latest stable version
            string? latestVersion;
            try
            {
                latestVersion = await QueryLatestStableVersionAsync(packageId);
            }
            catch (Exception ex)
            {
                findings.Add($"  ERROR: {packageId} — NuGet query failed: {ex.Message}");
                continue;
            }

            if (latestVersion == null)
                continue;

            // Wildcard versions: check for major-version updates only
            if (currentVersion.Contains('*'))
            {
                var majorStr = currentVersion.Split('.')[0];
                if (int.TryParse(majorStr, out var major) &&
                    Version.TryParse(latestVersion, out var latestParsed) &&
                    latestParsed.Major > major)
                {
                    var sources = string.Join(", ", entries.Select(e => e.Source).Distinct());
                    findings.Add($"  MAJOR: {packageId} {currentVersion} → {latestVersion} ({sources})");
                }

                continue;
            }

            // Fixed versions: compare directly
            if (!Version.TryParse(currentVersion, out var current) ||
                !Version.TryParse(latestVersion, out var latest))
                continue;

            if (latest > current)
            {
                var sources = string.Join(", ", entries.Select(e => e.Source).Distinct());
                findings.Add($"  {packageId} {currentVersion} → {latestVersion} ({sources})");
            }
        }

        Assert.True(
            findings.Count == 0,
            $"{findings.Count} package finding(s):\n" + string.Join("\n", findings));
    }

    /// <summary>
    /// Detects plugin packages that duplicate packages already provided transitively by
    /// bannou-service via ServiceLib.targets. These redundant references can be removed
    /// from the plugin .csproj — the package is already available through the transitive
    /// dependency chain.
    /// </summary>
    [Fact]
    public void PluginPackages_DoNotDuplicateBannouServicePackages()
    {
        SkipUnless.InformationalTest("Detects plugin packages already provided transitively by bannou-service");

        var bannouServicePath = Path.Combine(
            TestAssemblyDiscovery.RepoRoot, "bannou-service", "bannou-service.csproj");
        Assert.True(File.Exists(bannouServicePath),
            $"bannou-service.csproj not found at {bannouServicePath}");

        var sharedPackages = ParsePackageReferences(bannouServicePath)
            .ToDictionary(p => p.Id, p => p.Version, StringComparer.OrdinalIgnoreCase);

        var duplicates = new List<string>();
        foreach (var pluginCsproj in GetPluginCsprojPaths())
        {
            var pluginName = Path.GetFileNameWithoutExtension(pluginCsproj);
            foreach (var pkg in ParsePackageReferences(pluginCsproj))
            {
                if (!sharedPackages.TryGetValue(pkg.Id, out var sharedVersion))
                    continue;

                var versionNote = string.Equals(pkg.Version, sharedVersion, StringComparison.Ordinal)
                    ? $"same version {pkg.Version}"
                    : $"plugin: {pkg.Version}, bannou-service: {sharedVersion}";
                duplicates.Add($"{pluginName}: {pkg.Id} ({versionNote})");
            }
        }

        Assert.True(
            duplicates.Count == 0,
            $"{duplicates.Count} plugin package(s) duplicate bannou-service " +
            $"(available transitively via ServiceLib.targets):\n" +
            string.Join("\n", duplicates.Select(d => $"  - {d}")));
    }

    /// <summary>
    /// Detects plugin-specific NuGet packages (not in bannou-service) that have no matching
    /// <c>using</c> directives in the plugin's source files. This is a heuristic check — some
    /// packages provide runtime assets, native libraries, or framework providers whose namespaces
    /// differ from the package ID. Known namespace overrides are handled via
    /// <see cref="PackageNamespaceOverrides"/>. Review flagged packages manually before removing.
    /// </summary>
    [Fact]
    public void PluginPackages_AreReferencedInSource()
    {
        SkipUnless.InformationalTest("Detects plugin-specific packages with no using directives in source");

        var bannouServicePackages = ParsePackageReferences(
                Path.Combine(TestAssemblyDiscovery.RepoRoot, "bannou-service", "bannou-service.csproj"))
            .Select(p => p.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unused = new List<string>();
        foreach (var pluginCsproj in GetPluginCsprojPaths())
        {
            var pluginName = Path.GetFileNameWithoutExtension(pluginCsproj);
            var pluginDir = Path.GetDirectoryName(pluginCsproj)!;

            // Only check packages unique to this plugin (not from bannou-service)
            var pluginOnlyPackages = ParsePackageReferences(pluginCsproj)
                .Where(p => !bannouServicePackages.Contains(p.Id))
                .Where(p => !p.IsBuildTimeOnly)
                .ToList();

            if (pluginOnlyPackages.Count == 0)
                continue;

            // Collect all using directives from the plugin's source files
            var usings = CollectUsingDirectives(pluginDir);

            foreach (var pkg in pluginOnlyPackages)
            {
                var namespaces = GetNamespacePrefixes(pkg.Id);
                if (namespaces.Length == 0)
                    continue; // Build-time only per override

                var isUsed = namespaces.Any(ns =>
                    usings.Any(u => u.StartsWith(ns, StringComparison.Ordinal)));

                if (!isUsed)
                {
                    var searched = string.Join(", ", namespaces);
                    unused.Add($"{pluginName}: {pkg.Id} — no 'using' matching [{searched}]");
                }
            }
        }

        Assert.True(
            unused.Count == 0,
            $"{unused.Count} plugin package(s) may be unused " +
            $"(no matching 'using' directives found in source):\n" +
            string.Join("\n", unused.Select(u => $"  - {u}")));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private record PackageInfo(string Id, string Version, bool IsBuildTimeOnly, string Source);

    /// <summary>
    /// Collects all PackageReference entries from bannou-service and all plugin .csproj files.
    /// </summary>
    private static List<PackageInfo> CollectAllPackageReferences()
    {
        var all = new List<PackageInfo>();

        var bannouServicePath = Path.Combine(
            TestAssemblyDiscovery.RepoRoot, "bannou-service", "bannou-service.csproj");
        if (File.Exists(bannouServicePath))
            all.AddRange(ParsePackageReferences(bannouServicePath));

        foreach (var pluginCsproj in GetPluginCsprojPaths())
            all.AddRange(ParsePackageReferences(pluginCsproj));

        return all;
    }

    /// <summary>
    /// Parses PackageReference elements from a .csproj file.
    /// Returns package ID, version, whether it is build-time only, and the source project name.
    /// </summary>
    private static List<PackageInfo> ParsePackageReferences(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        var source = Path.GetFileNameWithoutExtension(csprojPath);
        var results = new List<PackageInfo>();

        foreach (var element in doc.Descendants("PackageReference"))
        {
            var id = element.Attribute("Include")?.Value;
            var version = element.Attribute("Version")?.Value;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version))
                continue;

            var privateAssets = element.Element("PrivateAssets")?.Value;
            var isBuildTimeOnly = string.Equals(privateAssets, "all", StringComparison.OrdinalIgnoreCase);

            results.Add(new PackageInfo(id, version, isBuildTimeOnly, source));
        }

        return results;
    }

    /// <summary>
    /// Discovers all non-test plugin .csproj files under the plugins/ directory.
    /// Excludes lib-*.tests directories.
    /// </summary>
    private static List<string> GetPluginCsprojPaths()
    {
        var pluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");
        var results = new List<string>();

        if (!Directory.Exists(pluginsDir))
            return results;

        foreach (var dir in Directory.GetDirectories(pluginsDir, "lib-*"))
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.EndsWith(".tests", StringComparison.Ordinal))
                continue;

            var csproj = Path.Combine(dir, $"{dirName}.csproj");
            if (File.Exists(csproj))
                results.Add(csproj);
        }

        return results;
    }

    /// <summary>
    /// Collects all <c>using</c> directive namespaces from .cs files in a directory tree.
    /// Handles standard, static, alias, and global using directives. Skips bin/ and obj/.
    /// </summary>
    private static HashSet<string> CollectUsingDirectives(string directory)
    {
        var usings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            // Skip bin/obj directories
            var relativePath = Path.GetRelativePath(directory, file);
            if (relativePath.StartsWith("bin", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("obj", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();

                // Handle "global using ..." by stripping "global " prefix
                if (trimmed.StartsWith("global ", StringComparison.Ordinal))
                    trimmed = trimmed["global ".Length..].TrimStart();

                // Must be a using directive, not a using statement (using (...))
                if (!trimmed.StartsWith("using ", StringComparison.Ordinal) ||
                    trimmed.StartsWith("using (", StringComparison.Ordinal))
                    continue;

                var ns = trimmed["using ".Length..];

                // Trim trailing ; and whitespace
                var semicolonIdx = ns.IndexOf(';');
                if (semicolonIdx >= 0)
                    ns = ns[..semicolonIdx];
                ns = ns.Trim();

                // Strip "static " prefix
                if (ns.StartsWith("static ", StringComparison.Ordinal))
                    ns = ns["static ".Length..].Trim();

                // For alias usings ("using Foo = Bar.Baz"), take the right-hand side
                var equalsIdx = ns.IndexOf('=');
                if (equalsIdx >= 0)
                    ns = ns[(equalsIdx + 1)..].Trim();

                if (ns.Length > 0)
                    usings.Add(ns);
            }
        }

        return usings;
    }

    /// <summary>
    /// Returns the namespace prefixes to search for in <c>using</c> directives for a given package.
    /// Uses <see cref="PackageNamespaceOverrides"/> when available, otherwise falls back to the
    /// package ID as the namespace prefix.
    /// </summary>
    private static string[] GetNamespacePrefixes(string packageId)
    {
        if (PackageNamespaceOverrides.TryGetValue(packageId, out var overrides))
            return overrides;

        return [packageId];
    }

    /// <summary>
    /// Queries the NuGet V3 flat container API for the latest stable version of a package.
    /// Returns null if the package is not found or has no stable versions.
    /// The flat container API is public and requires no authentication.
    /// </summary>
    private static async Task<string?> QueryLatestStableVersionAsync(string packageId)
    {
        var url = $"v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
        var response = await NuGetClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("versions", out var versions))
            return null;

        // Versions are sorted ascending by the API; find the last stable (non-prerelease) version
        string? latest = null;
        foreach (var version in versions.EnumerateArray())
        {
            var v = version.GetString();
            if (v != null && !v.Contains('-'))
                latest = v;
        }

        return latest;
    }
}
