using System;
using System.IO;
using AeroSpot.Core.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    internal static class TestEnvironment
    {
        private static string? _repoRoot;

        public static string? SessionDataDir { get; internal set; }

        public static string RepoRoot
        {
            get
            {
                if (_repoRoot != null) return _repoRoot;

                string? dir = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (File.Exists(Path.Combine(dir, "AeroSpot.sln")))
                    {
                        _repoRoot = dir;
                        return _repoRoot;
                    }
                    string? parent = Directory.GetParent(dir)?.FullName;
                    if (parent == dir || parent == null) break;
                    dir = parent;
                }

                dir = Directory.GetCurrentDirectory();
                while (!string.IsNullOrEmpty(dir))
                {
                    if (File.Exists(Path.Combine(dir, "AeroSpot.sln")))
                    {
                        _repoRoot = dir;
                        return _repoRoot;
                    }
                    string? parent = Directory.GetParent(dir)?.FullName;
                    if (parent == dir || parent == null) break;
                    dir = parent;
                }

                return AppContext.BaseDirectory;
            }
        }

        public static string TestsDir => Path.Combine(RepoRoot, "tests", "AeroSpot.Tests");
        public static string DocsDir => Path.Combine(RepoRoot, "Docs");
    }

    [TestClass]
    public sealed class TestAssemblySetup
    {
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            string sessionDir = Path.Combine(Path.GetTempPath(), $"AeroSpot_TestSession_{Guid.NewGuid():N}");
            Directory.CreateDirectory(sessionDir);
            TestEnvironment.SessionDataDir = sessionDir;

            Environment.SetEnvironmentVariable("AEROSPOT_TEST_DATA_DIR", sessionDir);
            AppDataPath.ResetCacheForTesting();
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            StaTestRunner.Shutdown();

            Environment.SetEnvironmentVariable("AEROSPOT_TEST_DATA_DIR", null);
            AppDataPath.ResetCacheForTesting();

            string? sessionDir = TestEnvironment.SessionDataDir;
            TestEnvironment.SessionDataDir = null;
            if (!string.IsNullOrEmpty(sessionDir) && Directory.Exists(sessionDir))
            {
                try
                {
                    Directory.Delete(sessionDir, recursive: true);
                }
                catch { }
            }
        }
    }
}
