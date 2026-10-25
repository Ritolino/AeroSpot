using System;
using System.IO;

namespace AeroSpot.Tests
{
    internal static class TestEnvironment
    {
        private static string? _repoRoot;

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
}
