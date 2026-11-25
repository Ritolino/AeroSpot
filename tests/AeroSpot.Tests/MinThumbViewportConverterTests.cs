using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AeroSpot.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class MinThumbViewportConverterTests
    {
        [TestMethod]
        public void Convert_VerticalAndHorizontal_SatisfiesInverseTrackFormula()
        {
            var converter = new MinThumbViewportConverter { MinThumbLength = 44.0 };
            double range = 1000.0; // maximum(1000) - minimum(0)

            // Vertical orientation uses actualHeight (trackLength = 400)
            object vertObj = converter.Convert(
                new object[] { 10.0, 1000.0, 0.0, 400.0, 12.0, Orientation.Vertical },
                typeof(double),
                null!,
                CultureInfo.InvariantCulture);

            double vertV = (double)vertObj;
            double vertThumbLen = 400.0 * vertV / (range + vertV);
            Assert.AreEqual(44.0, vertThumbLen, 1e-9, "Vertical track thumb formula must equal MinThumbLength.");

            // Horizontal orientation uses actualWidth (trackLength = 600)
            object horizObj = converter.Convert(
                new object[] { 5.0, 1000.0, 0.0, 12.0, 600.0, Orientation.Horizontal },
                typeof(double),
                null!,
                CultureInfo.InvariantCulture);

            double horizV = (double)horizObj;
            double horizThumbLen = 600.0 * horizV / (range + horizV);
            Assert.AreEqual(44.0, horizThumbLen, 1e-9, "Horizontal track thumb formula must equal MinThumbLength.");
        }

        [TestMethod]
        public void Convert_PreservesLargerViewportSize()
        {
            var converter = new MinThumbViewportConverter { MinThumbLength = 44.0 };
            double largeViewport = 800.0;

            object result = converter.Convert(
                new object[] { largeViewport, 1000.0, 0.0, 400.0, 12.0, Orientation.Vertical },
                typeof(double),
                null!,
                CultureInfo.InvariantCulture);

            Assert.AreEqual(largeViewport, (double)result, 1e-9);
        }

        [TestMethod]
        public void Convert_EdgeCases_HandleUnsetNaNAndNonPositiveRange()
        {
            var converter = new MinThumbViewportConverter { MinThumbLength = 44.0 };

            // UnsetValue -> NaN
            object unsetResult = converter.Convert(
                new object[] { DependencyProperty.UnsetValue, 100.0, 0.0, 400.0, 12.0, Orientation.Vertical },
                typeof(double),
                null!,
                CultureInfo.InvariantCulture);
            Assert.IsTrue(double.IsNaN((double)unsetResult));

            // NaN viewport -> NaN
            object nanResult = converter.Convert(
                new object[] { double.NaN, 100.0, 0.0, 400.0, 12.0, Orientation.Vertical },
                typeof(double),
                null!,
                CultureInfo.InvariantCulture);
            Assert.IsTrue(double.IsNaN((double)nanResult));

            // range <= 0 -> returns original viewportSize unchanged
            object zeroRangeResult = converter.Convert(
                new object[] { 50.0, 100.0, 100.0, 400.0, 12.0, Orientation.Vertical },
                typeof(double),
                null!,
                CultureInfo.InvariantCulture);
            Assert.AreEqual(50.0, (double)zeroRangeResult, 1e-9);
        }

        [TestMethod]
        public void ConvertBack_ThrowsNotSupportedException()
        {
            var converter = new MinThumbViewportConverter();
            bool threw = false;
            try
            {
                converter.ConvertBack(44.0, new[] { typeof(double) }, null!, CultureInfo.InvariantCulture);
            }
            catch (NotSupportedException)
            {
                threw = true;
            }
            Assert.IsTrue(threw);
        }
    }
}
