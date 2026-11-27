using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if UNITY_EDITOR
internal sealed class ReachyStandaloneBuildPostprocessor : IPostprocessBuildWithReport
{
    private static readonly HashSet<string> IgnoredDirectoryNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "__pycache__"
        };

    private static readonly HashSet<string> IgnoredExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".meta",
            ".pyc",
            ".pyo"
        };

    private static readonly HashSet<string> IgnoredFileNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".DS_Store",
            "Thumbs.db"
        };

    private static readonly string[] PortablePythonRuntimeDirectories =
    {
        "DLLs",
        "Lib",
        "libs"
    };

    private static readonly string[] PortablePythonRuntimeFiles =
    {
        "python.exe",
        "pythonw.exe",
        "python3.dll",
        "vcruntime140.dll",
        "vcruntime140_1.dll",
        "LICENSE.txt"
    };

    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report == null)
        {
            return;
        }

        BuildTarget target = report.summary.platform;
        if (target != BuildTarget.StandaloneWindows &&
            target != BuildTarget.StandaloneWindows64)
        {
            return;
        }

        string projectRoot = GetProjectRootPath();
        string buildRoot = GetBuildRootPath(report.summary.outputPath);
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            Debug.LogWarning("[Reachy Build] Project root could not be resolved. Skipping local voice bundle copy.");
            return;
        }

        if (string.IsNullOrWhiteSpace(buildRoot) || !Directory.Exists(buildRoot))
        {
            Debug.LogWarning(
                $"[Reachy Build] Build root could not be resolved from '{report.summary.outputPath}'. " +
                "Skipping local voice bundle copy.");
            return;
        }

        int copiedFileCount = 0;
        CopyFileIfPresent(
            Path.Combine(projectRoot, "Assets", "ReachyControlApp", "voice_agent_config.json"),
            Path.Combine(buildRoot, "ReachyControlApp", "voice_agent_config.json"),
            ref copiedFileCount);

        string buildCustomPersonalityDirectory = Path.Combine(
            buildRoot,
            "ReachyControlApp",
            "OnlineAiCustomPersonalities");
        Directory.CreateDirectory(buildCustomPersonalityDirectory);

        CopyDirectoryIfPresent(
            Path.Combine(projectRoot, "UserSettings", "ReachyControlApp", "OnlineAiCustomPersonalities"),
            buildCustomPersonalityDirectory,
            ref copiedFileCount);

        CopyDirectoryIfPresent(
            Path.Combine(projectRoot, "Assets", "ReachyControlApp", "LocalVoiceAgent"),
            Path.Combine(buildRoot, "ReachyControlApp", "LocalVoiceAgent"),
            ref copiedFileCount);

        CopyDirectoryIfPresent(
            Path.Combine(projectRoot, ".local_voice_models"),
            Path.Combine(buildRoot, ".local_voice_models"),
            ref copiedFileCount);

        CopyPortablePythonRuntimeIfPresent(projectRoot, buildRoot, ref copiedFileCount);
        PatchBundledVoiceAgentConfigIfPresent(buildRoot);

        Debug.Log(
            $"[Reachy Build] Copied {copiedFileCount} local voice runtime file(s) into '{buildRoot}'.");
    }

    private static string GetProjectRootPath()
    {
        try
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetBuildRootPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return string.Empty;
        }

        try
        {
            if (Directory.Exists(outputPath))
            {
                return Path.GetFullPath(outputPath);
            }

            string directory = Path.GetDirectoryName(outputPath);
            return string.IsNullOrWhiteSpace(directory)
                ? string.Empty
                : Path.GetFullPath(directory);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void CopyDirectoryIfPresent(
        string sourceDirectory,
        string destinationDirectory,
        ref int copiedFileCount)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);

        string[] files = Directory.GetFiles(sourceDirectory);
        for (int i = 0; i < files.Length; i++)
        {
            string sourceFile = files[i];
            if (ShouldSkipFile(sourceFile))
            {
                continue;
            }

            string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            TryCopyFile(sourceFile, destinationFile, ref copiedFileCount);
        }

        string[] directories = Directory.GetDirectories(sourceDirectory);
        for (int i = 0; i < directories.Length; i++)
        {
            string childDirectory = directories[i];
            string childName = Path.GetFileName(childDirectory);
            if (ShouldSkipDirectory(childName))
            {
                continue;
            }

            CopyDirectoryIfPresent(
                childDirectory,
                Path.Combine(destinationDirectory, childName),
                ref copiedFileCount);
        }
    }

    private static void CopyFileIfPresent(
        string sourceFile,
        string destinationFile,
        ref int copiedFileCount)
    {
        if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile) || ShouldSkipFile(sourceFile))
        {
            return;
        }

        TryCopyFile(sourceFile, destinationFile, ref copiedFileCount);
    }

    private static void TryCopyFile(string sourceFile, string destinationFile, ref int copiedFileCount)
    {
        try
        {
            CopyFile(sourceFile, destinationFile);
            copiedFileCount++;
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[Reachy Build] Failed to copy '{sourceFile}' to '{destinationFile}': {ex.Message}");
        }
    }

    private static void CopyFile(string sourceFile, string destinationFile)
    {
        string destinationDirectory = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourceFile, destinationFile, true);
    }

    private static void CopyPortablePythonRuntimeIfPresent(
        string projectRoot,
        string buildRoot,
        ref int copiedFileCount)
    {
        string runtimeSource = ResolvePortablePythonRuntimeSource(projectRoot);
        if (string.IsNullOrWhiteSpace(runtimeSource) || !Directory.Exists(runtimeSource))
        {
            Debug.LogWarning(
                "[Reachy Build] Portable Python runtime source was not found. " +
                "The bundled LocalVoiceAgent .venv may still depend on a system Python install.");
            return;
        }

        string runtimeDestination = Path.Combine(
            buildRoot,
            "ReachyControlApp",
            "LocalVoiceAgent",
            "PythonRuntime");

        for (int i = 0; i < PortablePythonRuntimeFiles.Length; i++)
        {
            string fileName = PortablePythonRuntimeFiles[i];
            CopyFileIfPresent(
                Path.Combine(runtimeSource, fileName),
                Path.Combine(runtimeDestination, fileName),
                ref copiedFileCount);
        }

        string versionedDll = FindVersionedPythonDll(runtimeSource);
        if (!string.IsNullOrWhiteSpace(versionedDll))
        {
            CopyFileIfPresent(
                versionedDll,
                Path.Combine(runtimeDestination, Path.GetFileName(versionedDll)),
                ref copiedFileCount);
        }

        for (int i = 0; i < PortablePythonRuntimeDirectories.Length; i++)
        {
            string directoryName = PortablePythonRuntimeDirectories[i];
            CopyDirectoryIfPresent(
                Path.Combine(runtimeSource, directoryName),
                Path.Combine(runtimeDestination, directoryName),
                ref copiedFileCount);
        }

        string voiceDependencies = Path.Combine(
            projectRoot, "Assets", "ReachyControlApp", "LocalVoiceAgent", ".venv", "Lib", "site-packages");
        if (Directory.Exists(voiceDependencies))
        {
            CopyDirectoryIfPresent(
                voiceDependencies,
                Path.Combine(runtimeDestination, "Lib", "site-packages"),
                ref copiedFileCount);
        }
        else
        {
            Debug.LogWarning("[Reachy Build] Voice dependencies are missing from .venv/Lib/site-packages.");
        }
    }

    private static string ResolvePortablePythonRuntimeSource(string projectRoot)
    {
        string venvConfigPath = Path.Combine(
            projectRoot,
            "Assets",
            "ReachyControlApp",
            "LocalVoiceAgent",
            ".venv",
            "pyvenv.cfg");
        if (!File.Exists(venvConfigPath))
        {
            return string.Empty;
        }

        try
        {
            string[] lines = File.ReadAllLines(venvConfigPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                if (!string.Equals(key, "home", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = line.Substring(separatorIndex + 1).Trim();
                if (Directory.Exists(value))
                {
                    return Path.GetFullPath(value);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Reachy Build] Failed to parse Python runtime source from '{venvConfigPath}': {ex.Message}");
        }

        return string.Empty;
    }

    private static string FindVersionedPythonDll(string runtimeSource)
    {
        if (string.IsNullOrWhiteSpace(runtimeSource) || !Directory.Exists(runtimeSource))
        {
            return string.Empty;
        }

        try
        {
            string[] dllPaths = Directory.GetFiles(runtimeSource, "python*.dll", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < dllPaths.Length; i++)
            {
                string fileName = Path.GetFileName(dllPaths[i]);
                if (string.Equals(fileName, "python3.dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return dllPaths[i];
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Reachy Build] Failed to discover versioned Python DLL in '{runtimeSource}': {ex.Message}");
        }

        return string.Empty;
    }

    private static void PatchBundledVoiceAgentConfigIfPresent(string buildRoot)
    {
        string configPath = Path.Combine(buildRoot, "ReachyControlApp", "voice_agent_config.json");
        if (!File.Exists(configPath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(configPath);
            string updated = Regex.Replace(
                json,
                "(\"sidecar_python_command\"\\s*:\\s*\")([^\"]*)(\")",
                "$1PythonRuntime\\\\python.exe$3",
                RegexOptions.CultureInvariant);
            if (!string.Equals(updated, json, StringComparison.Ordinal))
            {
                File.WriteAllText(configPath, updated);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Reachy Build] Failed to patch bundled voice config '{configPath}': {ex.Message}");
        }
    }

    private static bool ShouldSkipDirectory(string directoryName)
    {
        return string.IsNullOrWhiteSpace(directoryName) ||
            IgnoredDirectoryNames.Contains(directoryName.Trim());
    }

    private static bool ShouldSkipFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return true;
        }

        string fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName) || IgnoredFileNames.Contains(fileName))
        {
            return true;
        }

        string extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(extension) && IgnoredExtensions.Contains(extension);
    }
}
#endif
