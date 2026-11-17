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

        [TestMethod]
        public void Percentage_SpecificationTable_AndEdgeCases()
        {
            // Tabella specifica Q4:
            // 1190-22% -> 928,2 (it) / 928.2 (en)
            var res1 = ExpressionEvaluator.TryEvaluate("1190-22%", "it");
            Assert.IsNotNull(res1);
            Assert.AreEqual("928,2", res1.Title);
            Assert.AreEqual("Sconto 22% (-261,8)", res1.Subtitle);
            Assert.IsTrue(res1.IsCalculator);

            var res1En = ExpressionEvaluator.TryEvaluate("1190-22%", "en");
            Assert.IsNotNull(res1En);
            Assert.AreEqual("928.2", res1En.Title);
            Assert.AreEqual("Discount 22% (-261.8)", res1En.Subtitle);

            // 600-60% -> 240 (con sottotitolo esplicito sconto)
            var res600 = ExpressionEvaluator.TryEvaluate("600-60%", "it");
            Assert.IsNotNull(res600);
            Assert.AreEqual("240", res600.Title);
            Assert.AreEqual("Sconto 60% (-360)", res600.Subtitle);

            var res600En = ExpressionEvaluator.TryEvaluate("600-60%", "en");
            Assert.IsNotNull(res600En);
            Assert.AreEqual("240", res600En.Title);
            Assert.AreEqual("Discount 60% (-360)", res600En.Subtitle);

            // 1190+22% -> 1451,8 (it)
            var res2 = ExpressionEvaluator.TryEvaluate("1190+22%", "it");
            Assert.IsNotNull(res2);
            Assert.AreEqual("1451,8", res2.Title);
            Assert.AreEqual("+22% (+261,8)", res2.Subtitle);

            // 1190*22% -> 261,8 (it)
            var res3 = ExpressionEvaluator.TryEvaluate("1190*22%", "it");
            Assert.IsNotNull(res3);
            Assert.AreEqual("261,8", res3.Title);
            Assert.AreEqual("22% di 1190", res3.Subtitle);

            // 600*60% -> 360
            var res600Mul = ExpressionEvaluator.TryEvaluate("600*60%", "it");
            Assert.IsNotNull(res600Mul);
            Assert.AreEqual("360", res600Mul.Title);
            Assert.AreEqual("60% di 600", res600Mul.Subtitle);

            // 60%*600 -> 360
            var res600MulRev = ExpressionEvaluator.TryEvaluate("60%*600", "it");
            Assert.IsNotNull(res600MulRev);
            Assert.AreEqual("360", res600MulRev.Title);
            Assert.AreEqual("60% di 600", res600MulRev.Subtitle);

            // 1190/22% -> 5409,09... (it)
            var res4 = ExpressionEvaluator.TryEvaluate("1190/22%", "it");
            Assert.IsNotNull(res4);
            Assert.IsTrue(res4.Title.StartsWith("5409,09", StringComparison.Ordinal));

            // 22% -> 0,22 (it) / 0.22 (en)
            var res5 = ExpressionEvaluator.TryEvaluate("22%", "it");
            Assert.IsNotNull(res5);
            Assert.AreEqual("0,22", res5.Title);

            var res5En = ExpressionEvaluator.TryEvaluate("22%", "en");
            Assert.IsNotNull(res5En);
            Assert.AreEqual("0.22", res5En.Title);

            // 200+10%+10% -> 242 (it)
            var res6 = ExpressionEvaluator.TryEvaluate("200+10%+10%", "it");
            Assert.IsNotNull(res6);
            Assert.AreEqual("242", res6.Title);

            // (100+20)-10% -> 108 (it)
            var res7 = ExpressionEvaluator.TryEvaluate("(100+20)-10%", "it");
            Assert.IsNotNull(res7);
            Assert.AreEqual("108", res7.Title);

            // Spazi
            var resSpaces = ExpressionEvaluator.TryEvaluate("1190 - 22 %", "it");
            Assert.IsNotNull(resSpaces);
            Assert.AreEqual("928,2", resSpaces.Title);

            var resSpacesChain = ExpressionEvaluator.TryEvaluate("200 + 10 % + 10 %", "it");
            Assert.IsNotNull(resSpacesChain);
            Assert.AreEqual("242", resSpacesChain.Title);

            // Decimali con virgola e con punto
            var resDec1 = ExpressionEvaluator.TryEvaluate("1190,5 - 20%", "it");
            Assert.IsNotNull(resDec1);
            Assert.AreEqual("952,4", resDec1.Title);

            var resDec2 = ExpressionEvaluator.TryEvaluate("1190.5 - 20%", "en");
            Assert.IsNotNull(resDec2);
            Assert.AreEqual("952.4", resDec2.Title);

            // 0%
            var resZero = ExpressionEvaluator.TryEvaluate("0%", "it");
            Assert.IsNotNull(resZero);
            Assert.AreEqual("0", resZero.Title);

            var resAddZero = ExpressionEvaluator.TryEvaluate("1190 + 0%", "it");
            Assert.IsNotNull(resAddZero);
            Assert.AreEqual("1190", resAddZero.Title);

            // Numeri negativi
            var resNeg = ExpressionEvaluator.TryEvaluate("-22%", "it");
            Assert.IsNotNull(resNeg);
            Assert.AreEqual("-0,22", resNeg.Title);

            // Modulo invariato
            var resMod = ExpressionEvaluator.TryEvaluate("10 % 3", "it");
            Assert.IsNotNull(resMod);
            Assert.AreEqual("1", resMod.Title);

            // Casi anomali che non devono crashare ma restituire null o false
            Assert.IsNull(ExpressionEvaluator.TryEvaluate("%", "it"));
            Assert.IsNull(ExpressionEvaluator.TryEvaluate("%%", "it"));
            Assert.IsNull(ExpressionEvaluator.TryEvaluate("22%%", "it"));
            Assert.IsNull(ExpressionEvaluator.TryEvaluate("+ %", "it"));
            Assert.IsNull(ExpressionEvaluator.TryEvaluate("1190 - %", "it"));
        }
    }
}
