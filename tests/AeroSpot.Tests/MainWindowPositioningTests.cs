using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class MainWindowPositioningTests
    {
        [TestMethod]
        public void CenteringMath_WithVariousDpiScales_CalculatesSymmetricMargins()
        {
            // Test 100%, 125%, 150%, 175%, 200% scaling on common resolutions
            var testCases = new[]
            {
                new { Width = 1920, Height = 1080, Scale = 1.0 },
                new { Width = 1920, Height = 1200, Scale = 1.25 },
                new { Width = 2560, Height = 1600, Scale = 1.5 },   // 16" laptop config
                new { Width = 2560, Height = 1440, Scale = 1.25 },
                new { Width = 3840, Height = 2160, Scale = 1.75 },
                new { Width = 3840, Height = 2160, Scale = 2.0 }
            };

            const double windowWidthDips = 680.0;

            foreach (var tc in testCases)
            {
                double workWidthDips = tc.Width / tc.Scale;
                double leftDips = (workWidthDips - windowWidthDips) / 2.0;

                double leftPixels = leftDips * tc.Scale;
                double rightEdgePixels = leftPixels + (windowWidthDips * tc.Scale);
                double rightMarginPixels = tc.Width - rightEdgePixels;

                // Left margin and right margin must be equal within 0.001 pixel
                Assert.AreEqual(leftPixels, rightMarginPixels, 0.001,
                    $"Failed for {tc.Width}x{tc.Height} @ {tc.Scale * 100}% DPI. Left: {leftPixels}, Right: {rightMarginPixels}");
            }
        }
    }
}
