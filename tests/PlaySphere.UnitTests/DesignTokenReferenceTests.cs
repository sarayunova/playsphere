using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Guards against the XAML "cannot find a Resource with the Name/Key" crash class (a
/// <see cref="Microsoft.UI.Xaml.Markup.XamlParseException"/> fired at control-load time when a
/// <c>{ThemeResource ...}</c> names a key that is never defined). Theme resources resolve at runtime,
/// not at compile time, so the smoke test (which never opens <c>PlayerPage</c>) cannot catch it — this
/// test cross-checks every custom token key referenced across the UI XAML against the app's design
/// tokens to catch the mismatch statically.
/// </summary>
public class DesignTokenReferenceTests
{
    private static readonly string UiRoot = FindUiRoot();

    [Fact]
    public void Every_custom_theme_resource_referenced_is_defined()
    {
        var defined = new HashSet<string>(CollectDefinedKeys(), StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var xamlFile in Directory.GetFiles(UiRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(xamlFile);

            // Only custom token keys — WinUI system resources (Foreground, TextControlForeground,
            // etc.) resolve from XamlControlsResources and are out of scope here.
            foreach (Match m in Regex.Matches(content, @"\{ThemeResource\s+([A-Za-z][A-Za-z0-9._]*)\}"))
            {
                var key = m.Groups[1].Value;
                if (!IsCustomToken(key))
                {
                    continue;
                }

                if (!defined.Contains(key))
                {
                    missing.Add($"{Path.GetFileName(xamlFile)}: {key}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "Custom theme resources referenced but never defined:" + Environment.NewLine + string.Join(Environment.NewLine, missing.Distinct().OrderBy(x => x)));
    }

    private static IEnumerable<string> CollectDefinedKeys()
    {
        // Every x:Key across all UI XAML (design tokens + any page-local resources) counts as defined.
        foreach (var xamlFile in Directory.GetFiles(UiRoot, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(xamlFile), @"x:Key=""([^""]+)"""))
            {
                yield return m.Groups[1].Value;
            }
        }
    }

    private static bool IsCustomToken(string key)
        => key.StartsWith("Brush", StringComparison.Ordinal)
           || key.StartsWith("Color", StringComparison.Ordinal)
           || key.StartsWith("Gradient", StringComparison.Ordinal)
           || key.StartsWith("Spacing", StringComparison.Ordinal)
           || key.StartsWith("Control", StringComparison.Ordinal)
           || key.StartsWith("Card", StringComparison.Ordinal)
           || key == "ControlCornerRadius"
           || key == "CardCornerRadius";

    private static string FindUiRoot()
    {
        // XAML content is copied under PlaySphere.UI\ in the test output. Walk up from the assembly
        // location to find it; fall back to the repo-relative path if the copy layout differs.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "PlaySphere.UI"),
            Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\..\src\PlaySphere.UI"),
            Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\PlaySphere.UI"),
        };

        foreach (var candidate in candidates)
        {
            var normalized = Path.GetFullPath(candidate);
            if (Directory.Exists(normalized) && File.Exists(Path.Combine(normalized, "Themes", "DesignTokens.xaml")))
            {
                return normalized;
            }
        }

        throw new InvalidOperationException($"Could not locate PlaySphere.UI XAML under {AppContext.BaseDirectory}");
    }
}
