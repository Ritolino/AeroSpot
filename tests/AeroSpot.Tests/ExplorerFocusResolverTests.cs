using System;
using System.IO;
using AeroSpot.Core.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class ExplorerFocusResolverTests
    {
        [TestMethod]
        public void IsInputFieldClass_IdentifiesAllKnownInputControls()
        {
            // Input controls that MUST be recognized to prevent Space interception
            string[] inputClasses = new[]
            {
                "Edit",
                "edit",
                "EDIT",
                "SearchEditBox",
                "Address Band Root",
                "UniversalSearchBand",
                "ComboBox",
                "ComboBoxEx32",
                "ModernSearchBar",
                "RichEdit",
                "RichEdit20W",
                "RichEdit50W",
                "RICHEDIT60W",
                "MyCustomTextBoxControl"
            };

            foreach (var cls in inputClasses)
            {
                bool isInput = ExplorerFocusResolver.IsInputFieldClass(cls);
                Assert.IsTrue(isInput, $"Expected class '{cls}' to be recognized as an input field.");
            }
        }

        [TestMethod]
        public void IsInputFieldClass_RejectsExplorerFileListViews()
        {
            // File list views where Space MUST trigger QuickLook preview
            string[] viewClasses = new[]
            {
                "SHELLDLL_DefView",
                "DirectUIHWND",
                "SysListView32",
                "CabinetWClass",
                "ExploreWClass",
                "WorkerW",
                "Progman",
                "ShellTabWindowClass"
            };

            foreach (var cls in viewClasses)
            {
                bool isInput = ExplorerFocusResolver.IsInputFieldClass(cls);
                Assert.IsFalse(isInput, $"Expected class '{cls}' NOT to be recognized as an input field.");
            }
        }

        [TestMethod]
        public void IsValidDiskPath_ValidatesFilesAndDirectoriesProperly()
        {
            // Existing file
            string thisDll = typeof(ExplorerFocusResolverTests).Assembly.Location;
            Assert.IsTrue(ExplorerFocusResolver.IsValidDiskPath(thisDll), "Existing assembly DLL should be valid.");

            // Existing directory
            string tempDir = Path.GetTempPath();
            Assert.IsTrue(ExplorerFocusResolver.IsValidDiskPath(tempDir), "Existing temp directory should be valid.");

            // Non-existent path
            string fakePath = @"C:\NonExistent_FakeDirectory_AeroSpot_Test\FakeFile.xyz";
            Assert.IsFalse(ExplorerFocusResolver.IsValidDiskPath(fakePath), "Non-existent file should be invalid.");

            // Virtual shell folder GUID
            string shellGuid = @"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            Assert.IsFalse(ExplorerFocusResolver.IsValidDiskPath(shellGuid), "Virtual Shell GUIDs must be rejected.");

            // Null or whitespace
            Assert.IsFalse(ExplorerFocusResolver.IsValidDiskPath(null), "Null should be invalid.");
            Assert.IsFalse(ExplorerFocusResolver.IsValidDiskPath("   "), "Whitespace should be invalid.");
            Assert.IsFalse(ExplorerFocusResolver.IsValidDiskPath(string.Empty), "Empty should be invalid.");
        }

        [TestMethod]
        public void ExtractPathFromShellDocument_HandlesNullAndExceptionsSafely()
        {
            // Null document
            string? pathNull = ExplorerFocusResolver.ExtractPathFromShellDocument(null);
            Assert.IsNull(pathNull, "Null document must return null without exceptions.");

            // Document throwing exception on FocusedItem
            var docThrowing = new ThrowingShellDocument();
            string? pathThrow = ExplorerFocusResolver.ExtractPathFromShellDocument(docThrowing);
            Assert.IsNull(pathThrow, "Throwing document must return null safely without terminating the caller.");
        }

        [TestMethod]
        public void ExtractPathFromShellDocument_PrefersFocusedItemOverSelectedItems()
        {
            string thisDll = typeof(ExplorerFocusResolverTests).Assembly.Location;
            string tempDir = Path.GetTempPath();

            var doc = new MockShellDocument
            {
                FocusedItem = new MockShellItem { Path = thisDll },
                SelectedItemsList = new[] { new MockShellItem { Path = tempDir } }
            };

            string? resolved = ExplorerFocusResolver.ExtractPathFromShellDocument(doc);
            Assert.AreEqual(thisDll, resolved, "FocusedItem must be preferred when valid.");
        }

        [TestMethod]
        public void ExtractPathFromShellDocument_FallsBackToSelectedItemsWhenFocusedItemInvalid()
        {
            string thisDll = typeof(ExplorerFocusResolverTests).Assembly.Location;

            var doc = new MockShellDocument
            {
                FocusedItem = new MockShellItem { Path = "::{FakeGuid}" },
                SelectedItemsList = new[] { new MockShellItem { Path = thisDll } }
            };

            string? resolved = ExplorerFocusResolver.ExtractPathFromShellDocument(doc);
            Assert.AreEqual(thisDll, resolved, "SelectedItems must be used as fallback when FocusedItem is invalid.");
        }

        [TestMethod]
        public void ExtractPathFromShellDocument_ReturnsNullWhenNoItemsSelected()
        {
            string thisDll = typeof(ExplorerFocusResolverTests).Assembly.Location;

            var doc = new MockShellDocument
            {
                FocusedItem = new MockShellItem { Path = thisDll },
                SelectedItemsList = Array.Empty<MockShellItem>()
            };

            string? resolved = ExplorerFocusResolver.ExtractPathFromShellDocument(doc);
            Assert.IsNull(resolved, "Must return null when SelectedItems is empty, even if FocusedItem is populated.");
        }

        [TestMethod]
        public void LiveShell_QueryNeverThrowsUnhandledException()
        {
            // Test query on IntPtr.Zero
            bool ok = ExplorerFocusResolver.TryGetFocusedPath(IntPtr.Zero, out string? path);
            Assert.IsFalse(ok);
            Assert.IsNull(path);

            // Desktop query live
            string? desktopPath = ExplorerFocusResolver.GetFocusedPathForDesktop();
            // desktopPath may be null if no item is selected on Desktop, which is valid and expected
            TestContext?.WriteLine($"Desktop resolved path: {desktopPath ?? "(no selection)"}");
        }

        public TestContext? TestContext { get; set; }

        public class MockShellItem
        {
            public string? Path { get; set; }
        }

        public class MockShellSelectedCollection
        {
            private readonly MockShellItem[] _items;
            public MockShellSelectedCollection(MockShellItem[] items) => _items = items;
            public int Count => _items.Length;
            public MockShellItem Item(int index) => _items[index];
        }

        public class MockShellDocument
        {
            public MockShellItem? FocusedItem { get; set; }
            public MockShellItem[] SelectedItemsList { get; set; } = Array.Empty<MockShellItem>();
            public MockShellSelectedCollection SelectedItems() => new MockShellSelectedCollection(SelectedItemsList);
        }

        public class ThrowingShellDocument
        {
            public object FocusedItem => throw new InvalidOperationException("Simulated COM disconnect or RPC failure.");
            public object SelectedItems() => throw new InvalidOperationException("Simulated COM disconnect or RPC failure.");
        }
    }
}
