using System;
using AeroSpot.Core.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class ExpressionEvaluatorTests
    {
        [TestMethod]
        public void EvaluateMath_BasicOperations()
        {
            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("5 * 5", out double r1));
            Assert.AreEqual(25.0, r1, 1e-9);

            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("5 x 5", out double r2));
            Assert.AreEqual(25.0, r2, 1e-9);

            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("10 + 20 * 2", out double r3));
            Assert.AreEqual(50.0, r3, 1e-9);

            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("(10 + 20) * 2", out double r4));
            Assert.AreEqual(60.0, r4, 1e-9);

            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("100 / 4", out double r5));
            Assert.AreEqual(25.0, r5, 1e-9);

            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("2^8", out double r6));
            Assert.AreEqual(256.0, r6, 1e-9);

            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("10 % 3", out double r7));
            Assert.AreEqual(1.0, r7, 1e-9);
        }

        [TestMethod]
        public void EvaluateMath_UnaryAndDecimals()
        {
            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("-5 + 10", out double r1));
            Assert.AreEqual(5.0, r1, 1e-9);

            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("2.5 * 4", out double r2));
            Assert.AreEqual(10.0, r2, 1e-9);

            Assert.IsTrue(ExpressionEvaluator.EvaluateMathExpression("2,5 * 4", out double r3));
            Assert.AreEqual(10.0, r3, 1e-9);
        }

        [TestMethod]
        public void UnitConversion_Length()
        {
            var res = ExpressionEvaluator.TryEvaluate("5cm in m", "it");
            Assert.IsNotNull(res);
            Assert.AreEqual("0,05 m", res.Title);
            Assert.IsFalse(res.IsCalculator);
            Assert.AreEqual("invio per copiare", res.Category);

            var res2 = ExpressionEvaluator.TryEvaluate("1000m to km", "it");
            Assert.IsNotNull(res2);
            Assert.AreEqual("1 km", res2.Title);
        }

        [TestMethod]
        public void UnitConversion_Volume()
        {
            var res = ExpressionEvaluator.TryEvaluate("1000ml in l", "it");
            Assert.IsNotNull(res);
            Assert.AreEqual("1 l", res.Title);

            var res2 = ExpressionEvaluator.TryEvaluate("50cl in dl", "it");
            Assert.IsNotNull(res2);
            Assert.AreEqual("5 dl", res2.Title);

            var res3 = ExpressionEvaluator.TryEvaluate("2l in cl", "it");
            Assert.IsNotNull(res3);
            Assert.AreEqual("200 cl", res3.Title);
        }

        [TestMethod]
        public void UnitConversion_Mass()
        {
            var res = ExpressionEvaluator.TryEvaluate("10kg in libre", "it");
            Assert.IsNotNull(res);
            StringAssert.Contains(res.Title, "lb");
            Assert.IsFalse(res.IsCalculator);

            var res2 = ExpressionEvaluator.TryEvaluate("1000g in kg", "it");
            Assert.IsNotNull(res2);
            Assert.AreEqual("1 kg", res2.Title);

            var res3 = ExpressionEvaluator.TryEvaluate("5hg in g", "it");
            Assert.IsNotNull(res3);
            Assert.AreEqual("500 g", res3.Title);
        }

        [TestMethod]
        public void UnitConversion_DigitalData()
        {
            var res = ExpressionEvaluator.TryEvaluate("1024MB in GB", "it");
            Assert.IsNotNull(res);
            Assert.AreEqual("1 GB", res.Title);
        }

        [TestMethod]
        public void UnitConversion_Temperature()
        {
            var res = ExpressionEvaluator.TryEvaluate("0 c in f", "it");
            Assert.IsNotNull(res);
            Assert.AreEqual("32 °F", res.Title);

            var res2 = ExpressionEvaluator.TryEvaluate("100 c in f", "it");
            Assert.IsNotNull(res2);
            Assert.AreEqual("212 °F", res2.Title);
        }

        [TestMethod]
        public void TryEvaluate_MathIntegration()
        {
            var res = ExpressionEvaluator.TryEvaluate("5 x 5", "it");
            Assert.IsNotNull(res);
            Assert.AreEqual("25", res.Title);
            Assert.AreEqual("25", res.CopyValue);
            Assert.IsTrue(res.IsCalculator);
            Assert.AreEqual("invio per copiare", res.Category);
        }
    }
}
