using System;
using System.IO;
using System.Threading.Tasks;
using AeroSpot.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class FileBatchCollectorTests
    {
        private string _tempDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"AeroSpot_BatchTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch { }
        }

        [TestMethod]
        public async Task CollectFilesAsync_DirectMultiple_SortsNaturallyDeduplicatesAndFiltersMissing()
        {
            string f1 = Path.Combine(_tempDir, "foto_1.png");
            string f2 = Path.Combine(_tempDir, "foto_2.png");
            string f10 = Path.Combine(_tempDir, "foto_10.png");
            string missing = Path.Combine(_tempDir, "foto_99_missing.png");

            File.WriteAllBytes(f1, Array.Empty<byte>());
            File.WriteAllBytes(f2, Array.Empty<byte>());
            File.WriteAllBytes(f10, Array.Empty<byte>());

            var result = await FileBatchCollector.CollectFilesAsync(
                "images_to_pdf",
                new[] { f10, f1, missing, f2, f1.ToUpperInvariant() });

            Assert.IsNotNull(result);
            Assert.HasCount(3, result);
            Assert.AreEqual(f1, result[0]);
            Assert.AreEqual(f2, result[1]);
            Assert.AreEqual(f10, result[2]);
        }

        [TestMethod]
        public async Task CollectFilesAsync_LeaderAndFollowersOverPipe_AggregatesAndSortsNaturally()
        {
            string f1 = Path.Combine(_tempDir, "foto_1.png");
            string f2 = Path.Combine(_tempDir, "foto_2.png");
            string f10 = Path.Combine(_tempDir, "foto_10.png");

            File.WriteAllBytes(f1, Array.Empty<byte>());
            File.WriteAllBytes(f2, Array.Empty<byte>());
            File.WriteAllBytes(f10, Array.Empty<byte>());

            string uniqueKey = $"testbatch_{Guid.NewGuid():N}";
            string userSanitized = Environment.UserName.Replace("\\", "_");
            string lockPath = Path.Combine(Path.GetTempPath(), $"AeroSpot_BatchLock_{uniqueKey}_{userSanitized}.lock");

            try
            {
                // Leader acquires FileStream lock and opens NamedPipeServerStream synchronously before first await
                var leaderTask = FileBatchCollector.CollectFilesAsync(uniqueKey, new[] { f10 }, debounceMs: 700);
                var follower1Task = FileBatchCollector.CollectFilesAsync(uniqueKey, new[] { f2 }, debounceMs: 700);
                var follower2Task = FileBatchCollector.CollectFilesAsync(uniqueKey, new[] { f1 }, debounceMs: 700);

                var follower1Result = await follower1Task;
                var follower2Result = await follower2Task;
                var leaderResult = await leaderTask;

                Assert.IsNull(follower1Result, "Follower 1 must deliver its path and return null.");
                Assert.IsNull(follower2Result, "Follower 2 must deliver its path and return null.");
                Assert.IsNotNull(leaderResult);
                Assert.HasCount(3, leaderResult);
                Assert.AreEqual(f1, leaderResult[0]);
                Assert.AreEqual(f2, leaderResult[1]);
                Assert.AreEqual(f10, leaderResult[2]);
            }
            finally
            {
                try { if (File.Exists(lockPath)) File.Delete(lockPath); } catch { }
            }
        }
    }
}
