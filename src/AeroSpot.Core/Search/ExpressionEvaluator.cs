using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AeroSpot.Core.Search
{
    public sealed class ExpressionResult
    {
        public string Title { get; }
        public string Subtitle { get; }
        public string Category { get; }
        public string CopyValue { get; }
        public bool IsCalculator { get; }

        public ExpressionResult(string title, string subtitle, string category, string copyValue, bool isCalculator)
        {
            Title = title;
            Subtitle = subtitle;
            Category = category;
            CopyValue = copyValue;
            IsCalculator = isCalculator;
        }
    }

    public static class ExpressionEvaluator
    {
        // -------------------------------------------------------------
        // 1. MOTORE CONVERSIONE UNITÀ DI MISURA OFFLINE
        // -------------------------------------------------------------
        private enum UnitType
        {
            Length,
            Mass,
            Data,
            Temperature,
            Time,
            Area,
            Volume
        }

        private sealed class UnitDef
        {
            public string CanonicalName { get; }
            public UnitType Type { get; }
            public double FactorToBase { get; } // Fattore moltiplicativo rispetto all'unità base SI
            public string Symbol { get; }

            public UnitDef(string canonicalName, UnitType type, double factorToBase, string symbol)
            {
                CanonicalName = canonicalName;
                Type = type;
                FactorToBase = factorToBase;
                Symbol = symbol;
            }
        }

        private static readonly Dictionary<string, UnitDef> Units = new(StringComparer.OrdinalIgnoreCase);

        static ExpressionEvaluator()
        {
            // Lunghezza (Base: Metro)
            RegisterUnit(UnitType.Length, 1e-3, "mm", "mm", "millimetro", "millimetri", "millimeter", "millimeters");
            RegisterUnit(UnitType.Length, 1e-2, "cm", "cm", "centimetro", "centimetri", "centimeter", "centimeters");
            RegisterUnit(UnitType.Length, 1e-1, "dm", "dm", "decimetro", "decimetri", "decimeter", "decimeters");
            RegisterUnit(UnitType.Length, 1.0, "m", "m", "metro", "metri", "meter", "meters");
            RegisterUnit(UnitType.Length, 1000.0, "km", "km", "chilometro", "chilometri", "kilometer", "kilometers");
            RegisterUnit(UnitType.Length, 0.0254, "in", "in", "inch", "inches", "pollice", "pollici");
            RegisterUnit(UnitType.Length, 0.3048, "ft", "ft", "foot", "feet", "piede", "piedi");
            RegisterUnit(UnitType.Length, 0.9144, "yd", "yd", "yard", "yards", "iarda", "iarde");
            RegisterUnit(UnitType.Length, 1609.344, "mi", "mi", "mile", "miles", "miglio", "miglia");

            // Massa / Peso (Base: Grammo)
            RegisterUnit(UnitType.Mass, 1e-3, "mg", "mg", "milligrammo", "milligrammi", "milligram", "milligrams");
            RegisterUnit(UnitType.Mass, 1e-2, "cg", "cg", "centigrammo", "centigrammi", "centigram", "centigrams");
            RegisterUnit(UnitType.Mass, 1e-1, "dg", "dg", "decigrammo", "decigrammi", "decigram", "decigrams");
            RegisterUnit(UnitType.Mass, 1.0, "g", "g", "grammo", "grammi", "gram", "grams");
            RegisterUnit(UnitType.Mass, 100.0, "hg", "hg", "etto", "etti", "ettogrammo", "ettogrammi", "hectogram", "hectograms");
            RegisterUnit(UnitType.Mass, 1000.0, "kg", "kg", "chilogrammo", "chilogrammi", "kilo", "kili", "kilogram", "kilograms");
            RegisterUnit(UnitType.Mass, 1e5, "q", "q", "quintale", "quintali");
            RegisterUnit(UnitType.Mass, 1e6, "t", "t", "tonnellata", "tonnellate", "ton", "tons");
            RegisterUnit(UnitType.Mass, 453.59237, "lb", "lb", "lbs", "libbra", "libbre", "libre", "pound", "pounds");
            RegisterUnit(UnitType.Mass, 28.349523125, "oz", "oz", "oncia", "once", "ounce", "ounces");

            // Capacità / Volume (Base: Litro)
            RegisterUnit(UnitType.Volume, 1e-3, "ml", "ml", "millilitro", "millilitri", "milliliter", "milliliters");
            RegisterUnit(UnitType.Volume, 1e-2, "cl", "cl", "centilitro", "centilitri", "centiliter", "centiliters");
            RegisterUnit(UnitType.Volume, 1e-1, "dl", "dl", "decilitro", "decilitri", "deciliter", "deciliters");
            RegisterUnit(UnitType.Volume, 1.0, "l", "l", "lt", "litro", "litri", "liter", "liters");
            RegisterUnit(UnitType.Volume, 100.0, "hl", "hl", "ettolitro", "ettolitri", "hectoliter", "hectoliters");
            RegisterUnit(UnitType.Volume, 1000.0, "m³", "m3", "m³", "metro cubo", "metri cubi", "cubic meter", "cubic meters");
            RegisterUnit(UnitType.Volume, 1e-3, "cm³", "cm3", "cm³", "cc", "centimetro cubo", "centimetri cubi");
            RegisterUnit(UnitType.Volume, 3.785411784, "gal", "gal", "gallone", "galloni", "gallon", "gallons");
            RegisterUnit(UnitType.Volume, 0.473176473, "pt", "pt", "pinta", "pinte", "pint", "pints");
            RegisterUnit(UnitType.Volume, 0.2365882365, "cup", "cup", "cups", "tazza", "tazze");
            RegisterUnit(UnitType.Volume, 0.0295735295625, "fl oz", "fl oz", "floz", "oncia liquida", "once liquide", "fluid ounce", "fluid ounces");

            // Area / Superficie (Base: Metro Quadro)
            RegisterUnit(UnitType.Area, 1e-6, "mm²", "mm2", "mm²");
            RegisterUnit(UnitType.Area, 1e-4, "cm²", "cm2", "cm²");
            RegisterUnit(UnitType.Area, 1.0, "m²", "m2", "m²", "mq", "metro quadro", "metri quadri", "metro quadrato", "metri quadrati", "square meter", "square meters");
            RegisterUnit(UnitType.Area, 1e4, "ha", "ha", "ettaro", "ettari", "hectare", "hectares");
            RegisterUnit(UnitType.Area, 1e6, "km²", "km2", "km²", "chilometro quadro", "chilometri quadri", "square kilometer", "square kilometers");
            RegisterUnit(UnitType.Area, 4046.8564224, "ac", "ac", "acro", "acri", "acre", "acres");

            // Memoria Dati (Base: Byte)
            RegisterUnit(UnitType.Data, 1.0, "B", "b", "byte", "bytes");
            RegisterUnit(UnitType.Data, 1024.0, "KB", "kb", "kilobyte", "kilobytes");
            RegisterUnit(UnitType.Data, 1024.0 * 1024.0, "MB", "mb", "megabyte", "megabytes");
            RegisterUnit(UnitType.Data, 1024.0 * 1024.0 * 1024.0, "GB", "gb", "gigabyte", "gigabytes");
            RegisterUnit(UnitType.Data, 1024.0 * 1024.0 * 1024.0 * 1024.0, "TB", "tb", "terabyte", "terabytes");

            // Tempo (Base: Secondo)
            RegisterUnit(UnitType.Time, 1e-3, "ms", "ms", "millisecondo", "millisecondi", "millisecond", "milliseconds");
            RegisterUnit(UnitType.Time, 1.0, "s", "s", "sec", "secondo", "secondi", "second", "seconds");
            RegisterUnit(UnitType.Time, 60.0, "min", "min", "minuto", "minuti", "minute", "minutes");
            RegisterUnit(UnitType.Time, 3600.0, "h", "h", "ora", "ore", "hour", "hours");
            RegisterUnit(UnitType.Time, 86400.0, "d", "d", "giorno", "giorni", "day", "days");

            // Temperatura (Formule speciali gestite ad hoc)
            RegisterUnit(UnitType.Temperature, 1.0, "°C", "c", "°c", "celsius");
            RegisterUnit(UnitType.Temperature, 1.0, "°F", "f", "°f", "fahrenheit");
            RegisterUnit(UnitType.Temperature, 1.0, "K", "k", "kelvin");
        }

        private static void RegisterUnit(UnitType type, double factor, string symbol, params string[] aliases)
        {
            var def = new UnitDef(symbol, type, factor, symbol);
            foreach (var alias in aliases)
            {
                Units[alias] = def;
            }
        }

        private static readonly Regex UnitConversionRegex = new(
            @"^\s*([+-]?\d+(?:[\.,]\d+)?)\s*([a-zA-Z°³²0-9]+(?:\s+[a-zA-Z°³²0-9]+)?)\s+(?:in|to|a|->|=)\s+([a-zA-Z°³²0-9]+(?:\s+[a-zA-Z°³²0-9]+)?)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // -------------------------------------------------------------
        // 2. MOTORE CALCOLATRICE RAPIDA
        // -------------------------------------------------------------
        private static readonly Regex PotentialMathRegex = new(
            @"^[\d\s\+\-\*\/\(\)\,\.\^\%xX]+$",
            RegexOptions.Compiled);

        public static ExpressionResult? TryEvaluate(string input, string currentLanguage = "it")
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string trimmed = input.Trim();

            // 1. Prova prima la conversione unità (es. "5cm in m", "10kg in libre", "30 c in f")
            var convMatch = UnitConversionRegex.Match(trimmed);
            if (convMatch.Success)
            {
                string numStr = convMatch.Groups[1].Value.Replace(',', '.');
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    string uFromStr = convMatch.Groups[2].Value;
                    string uToStr = convMatch.Groups[3].Value;

                    if (Units.TryGetValue(uFromStr, out var uFrom) && Units.TryGetValue(uToStr, out var uTo))
                    {
                        if (uFrom.Type == uTo.Type)
                        {
                            double convertedVal;
                            if (uFrom.Type == UnitType.Temperature)
                            {
                                convertedVal = ConvertTemperature(val, uFrom.Symbol, uTo.Symbol);
                            }
                            else
                            {
                                double inBase = val * uFrom.FactorToBase;
                                convertedVal = inBase / uTo.FactorToBase;
                            }

                            string formattedResult = FormatNumber(convertedVal, currentLanguage);
                            string resultWithUnit = $"{formattedResult} {uTo.Symbol}";
                            string copyBadge = GetLocalized("Spotlight_Calc_EnterToCopy", currentLanguage);

                            return new ExpressionResult(resultWithUnit, string.Empty, copyBadge, formattedResult, isCalculator: false);
                        }
                    }
                }
            }

            // 2. Prova Calcolatrice (es. "5 x 5", "100 / 4", "(10 + 5) * 3", "2^8")
            if (PotentialMathRegex.IsMatch(trimmed))
            {
                // Deve contenere almeno un operatore matematico per essere considerata espressione
                bool hasOp = false;
                foreach (char c in trimmed)
                {
                    if (c is '+' or '-' or '*' or '/' or 'x' or 'X' or '^' or '%')
                    {
                        hasOp = true;
                        break;
                    }
                }

                if (hasOp)
                {
                    try
                    {
                        if (EvaluateMathExpression(trimmed, out double mathResult, out string explanation, currentLanguage))
                        {
                            string formatted = FormatNumber(mathResult, currentLanguage);
                            string title = formatted;
                            string copyBadge = GetLocalized("Spotlight_Calc_EnterToCopy", currentLanguage);

                            return new ExpressionResult(title, explanation ?? string.Empty, copyBadge, formatted, isCalculator: true);
                        }
                    }
                    catch
                    {
                        // Espressione parziale o sintatticamente incompleta mentre l'utente digita
                    }
                }
            }

            return null;
        }

        private static string GetLocalized(string key, string language, params object[] args)
        {
            var dict = string.Equals(language, "it", StringComparison.OrdinalIgnoreCase)
                ? Localization.LocalizationService.Italian
                : Localization.LocalizationService.English;

            string template = dict.TryGetValue(key, out var val)
                ? val
                : Localization.LocalizationService.Get(key);

            return args.Length == 0
                ? template
                : string.Format(CultureInfo.InvariantCulture, template, args);
        }

        private static double ConvertTemperature(double val, string from, string to)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return val;

            // Convert to Celsius first
            double c;
            if (from == "°F") c = (val - 32.0) * 5.0 / 9.0;
            else if (from == "K") c = val - 273.15;
            else c = val;

            // Convert Celsius to Target
            if (to == "°F") return (c * 9.0 / 5.0) + 32.0;
            if (to == "K") return c + 273.15;
            return c;
        }

        private static string FormatNumber(double val, string currentLanguage = "it")
        {
            if (double.IsNaN(val)) return "NaN";
            if (double.IsPositiveInfinity(val)) return "∞";
            if (double.IsNegativeInfinity(val)) return "-∞";

            var culture = string.Equals(currentLanguage, "it", StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.GetCultureInfo("it-IT")
                : CultureInfo.InvariantCulture;

            // Se è un intero esatto
            if (Math.Abs(val % 1) < 1e-11)
            {
                return val.ToString("F0", culture);
            }

            // Altrimenti fino a 6 cifre decimali significative rimuovendo gli zeri finali
            string s = val.ToString("0.######", culture);
            return s;
        }

        private enum TokenType
        {
            Number,
            Plus,
            Minus,
            Multiply,
            Divide,
            Power,
            Percent,
            OpenParen,
            CloseParen,
            End
        }

        private struct Token
        {
            public TokenType Type;
            public double Value;
            public string Raw;
        }

        private struct Operand
        {
            public double Value;
            public bool IsPercent;
        }

        private sealed class MathParser
        {
            private readonly List<Token> _tokens;
            private readonly string _language;
            private int _pos;
            private string _explanation = string.Empty;

            public MathParser(List<Token> tokens, string language = "it")
            {
                _tokens = tokens;
                _language = language;
                _pos = 0;
            }

            private Token Current => _pos < _tokens.Count ? _tokens[_pos] : new Token { Type = TokenType.End };
            private Token Peek(int offset = 1) => (_pos + offset < _tokens.Count) ? _tokens[_pos + offset] : new Token { Type = TokenType.End };

            private bool Match(TokenType type)
            {
                if (Current.Type == type)
                {
                    _pos++;
                    return true;
                }
                return false;
            }

            private bool Check(TokenType type) => Current.Type == type;

            private Token Consume(TokenType type)
            {
                if (Current.Type == type)
                {
                    var t = Current;
                    _pos++;
                    return t;
                }
                throw new InvalidOperationException($"Expected {type} but found {Current.Type}");
            }

            private bool IsModulo()
            {
                // % is modulo if followed by a number, '(', or unary '-'
                var next = Peek();
                return next.Type == TokenType.Number || next.Type == TokenType.OpenParen || next.Type == TokenType.Minus;
            }

            public double Parse() => Parse(out _);

            public double Parse(out string explanation)
            {
                if (_tokens.Count == 0) throw new InvalidOperationException("Empty expression");
                Operand res = Additive();
                if (_pos < _tokens.Count)
                {
                    throw new InvalidOperationException("Unexpected token after expression: " + Current.Type);
                }
                if (res.IsPercent)
                {
                    double orig = res.Value;
                    res.Value = res.Value / 100.0;
                    if (string.IsNullOrEmpty(_explanation))
                    {
                        _explanation = $"{FormatNumber(orig, _language)}% = {FormatNumber(res.Value, _language)}";
                    }
                }
                explanation = _explanation;
                return res.Value;
            }

            private Operand Additive()
            {
                Operand left = Multiplicative();
                if (left.IsPercent && (Check(TokenType.Plus) || Check(TokenType.Minus)))
                {
                    left.Value = left.Value / 100.0;
                    left.IsPercent = false;
                }

                while (Check(TokenType.Plus) || Check(TokenType.Minus))
                {
                    TokenType op = Current.Type;
                    _pos++;
                    Operand right = Multiplicative();
                    if (right.IsPercent)
                    {
                        double delta = left.Value * right.Value / 100.0;
                        string pctStr = FormatNumber(right.Value, _language);
                        string deltaStr = FormatNumber(delta, _language);

                        if (op == TokenType.Plus)
                        {
                            left.Value = left.Value + delta;
                            _explanation = $"+{pctStr}% (+{deltaStr})";
                        }
                        else
                        {
                            left.Value = left.Value - delta;
                            _explanation = GetLocalized("Spotlight_Calc_Discount", _language, pctStr, deltaStr);
                        }
                        left.IsPercent = false;
                    }
                    else
                    {
                        if (op == TokenType.Plus) left.Value += right.Value;
                        else left.Value -= right.Value;
                    }
                }
                return left;
            }

            private Operand Multiplicative()
            {
                Operand left = Power();
                while (Check(TokenType.Multiply) || Check(TokenType.Divide) || (Check(TokenType.Percent) && IsModulo()))
                {
                    if (Check(TokenType.Percent))
                    {
                        _pos++; // consume modulo
                        Operand modRight = Power();
                        if (modRight.Value == 0) throw new DivideByZeroException();
                        left.Value = left.Value % modRight.Value;
                        left.IsPercent = false;
                        continue;
                    }

                    TokenType op = Current.Type;
                    _pos++;
                    Operand right = Power();

                    if (right.IsPercent)
                    {
                        double pct = right.Value / 100.0;
                        string pctStr = FormatNumber(right.Value, _language);
                        string baseStr = FormatNumber(left.Value, _language);

                        if (op == TokenType.Multiply)
                        {
                            left.Value = left.Value * pct;
                            _explanation = GetLocalized("Spotlight_Calc_PercentOf", _language, pctStr, baseStr);
                        }
                        else
                        {
                            left.Value = pct == 0 ? double.PositiveInfinity : left.Value / pct;
                            _explanation = $"{baseStr} / {FormatNumber(pct, _language)}";
                        }
                        left.IsPercent = false;
                    }
                    else if (left.IsPercent)
                    {
                        double pct = left.Value / 100.0;
                        string pctStr = FormatNumber(left.Value, _language);
                        string numStr = FormatNumber(right.Value, _language);

                        if (op == TokenType.Multiply)
                        {
                            left.Value = pct * right.Value;
                            _explanation = GetLocalized("Spotlight_Calc_PercentOf", _language, pctStr, numStr);
                        }
                        else
                        {
                            left.Value = right.Value == 0 ? double.PositiveInfinity : pct / right.Value;
                            _explanation = $"{FormatNumber(pct, _language)} / {numStr}";
                        }
                        left.IsPercent = false;
                    }
                    else
                    {
                        if (op == TokenType.Multiply)
                            left.Value = left.Value * right.Value;
                        else
                            left.Value = right.Value == 0 ? double.PositiveInfinity : left.Value / right.Value;
                    }
                }
                return left;
            }

            private Operand Power()
            {
                Operand left = Primary();
                if (Match(TokenType.Power))
                {
                    Operand right = Power();
                    left.Value = Math.Pow(left.Value, right.Value);
                    left.IsPercent = false;
                }
                return left;
            }

            private Operand Primary()
            {
                if (Match(TokenType.Minus))
                {
                    Operand val = Primary();
                    val.Value = -val.Value;
                    return val;
                }
                if (Match(TokenType.Plus))
                {
                    return Primary();
                }
                if (Match(TokenType.OpenParen))
                {
                    Operand val = Additive();
                    Consume(TokenType.CloseParen);
                    if (!IsModulo() && Match(TokenType.Percent))
                    {
                        val.IsPercent = true;
                    }
                    return val;
                }
                if (Check(TokenType.Number))
                {
                    Token num = Consume(TokenType.Number);
                    bool isPct = false;
                    if (!IsModulo() && Match(TokenType.Percent))
                    {
                        isPct = true;
                    }
                    return new Operand { Value = num.Value, IsPercent = isPct };
                }
                throw new InvalidOperationException($"Unexpected token in primary: {Current.Type}");
            }
        }

        private static List<Token>? Tokenize(string expr)
        {
            var tokens = new List<Token>();
            int i = 0;
            string s = expr.Replace('x', '*').Replace('X', '*').Replace(',', '.');

            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsDigit(c) || c == '.')
                {
                    int start = i;
                    bool hasDot = (c == '.');
                    while (i + 1 < s.Length && (char.IsDigit(s[i + 1]) || (!hasDot && s[i + 1] == '.')))
                    {
                        if (s[i + 1] == '.') hasDot = true;
                        i++;
                    }
                    string numStr = s.Substring(start, i - start + 1);
                    if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                    {
                        return null;
                    }
                    tokens.Add(new Token { Type = TokenType.Number, Value = val, Raw = numStr });
                    i++;
                }
                else
                {
                    switch (c)
                    {
                        case '+': tokens.Add(new Token { Type = TokenType.Plus, Raw = "+" }); break;
                        case '-': tokens.Add(new Token { Type = TokenType.Minus, Raw = "-" }); break;
                        case '*': tokens.Add(new Token { Type = TokenType.Multiply, Raw = "*" }); break;
                        case '/': tokens.Add(new Token { Type = TokenType.Divide, Raw = "/" }); break;
                        case '^': tokens.Add(new Token { Type = TokenType.Power, Raw = "^" }); break;
                        case '%': tokens.Add(new Token { Type = TokenType.Percent, Raw = "%" }); break;
                        case '(': tokens.Add(new Token { Type = TokenType.OpenParen, Raw = "(" }); break;
                        case ')': tokens.Add(new Token { Type = TokenType.CloseParen, Raw = ")" }); break;
                        default: return null;
                    }
                    i++;
                }
            }
            return tokens;
        }

        public static bool EvaluateMathExpression(string expression, out double result)
        {
            return EvaluateMathExpression(expression, out result, out _, "it");
        }

        public static bool EvaluateMathExpression(string expression, out double result, out string explanation, string currentLanguage = "it")
        {
            result = 0;
            explanation = string.Empty;
            if (string.IsNullOrWhiteSpace(expression)) return false;

            try
            {
                var tokens = Tokenize(expression);
                if (tokens == null || tokens.Count == 0) return false;

                var parser = new MathParser(tokens, currentLanguage);
                result = parser.Parse(out explanation);
                return !double.IsNaN(result);
            }
            catch
            {
                return false;
            }
        }
    }
}
