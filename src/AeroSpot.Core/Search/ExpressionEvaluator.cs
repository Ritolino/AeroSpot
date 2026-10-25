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

                            string formattedResult = FormatNumber(convertedVal);
                            string resultWithUnit = $"{formattedResult} {uTo.Symbol}";
                            string copyBadge = currentLanguage == "it" ? "invio per copiare" : "enter to copy";

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
                        if (EvaluateMathExpression(trimmed, out double mathResult))
                        {
                            string formatted = FormatNumber(mathResult);
                            string title = formatted;
                            string copyBadge = currentLanguage == "it" ? "invio per copiare" : "enter to copy";

                            return new ExpressionResult(title, string.Empty, copyBadge, formatted, isCalculator: true);
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

        private static string FormatNumber(double val)
        {
            if (double.IsNaN(val)) return "NaN";
            if (double.IsPositiveInfinity(val)) return "∞";
            if (double.IsNegativeInfinity(val)) return "-∞";

            // Se è un intero esatto
            if (Math.Abs(val % 1) < 1e-11)
            {
                return val.ToString("F0", CultureInfo.CurrentCulture);
            }

            // Altrimenti fino a 6 cifre decimali significative rimuovendo gli zeri finali
            string s = val.ToString("0.######", CultureInfo.CurrentCulture);
            return s;
        }

        // Parser matematico Shunting-Yard sicuro e leggero
        public static bool EvaluateMathExpression(string expression, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(expression)) return false;

            // Sostituisce 'x' o 'X' con '*' se circondata o usata come moltiplicazione
            string normalized = expression
                .Replace('x', '*')
                .Replace('X', '*')
                .Replace(',', '.');

            var tokens = Tokenize(normalized);
            if (tokens.Count == 0) return false;

            // Shunting Yard -> Postfix (RPN)
            var outputQueue = new Queue<string>();
            var operatorStack = new Stack<string>();

            string? lastToken = null;
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];

                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    outputQueue.Enqueue(token);
                }
                else if (token == "(")
                {
                    operatorStack.Push(token);
                }
                else if (token == ")")
                {
                    bool foundLeft = false;
                    while (operatorStack.Count > 0)
                    {
                        string op = operatorStack.Pop();
                        if (op == "(")
                        {
                            foundLeft = true;
                            break;
                        }
                        outputQueue.Enqueue(op);
                    }
                    if (!foundLeft) return false; // Parentesi sbilanciata
                }
                else if (IsOperator(token))
                {
                    // Gestione meno unario (es: -5 o 2 * -3)
                    if (token == "-" && (lastToken == null || lastToken == "(" || IsOperator(lastToken)))
                    {
                        token = "u-"; // Operatore unario
                    }

                    while (operatorStack.Count > 0 && IsOperator(operatorStack.Peek()))
                    {
                        string top = operatorStack.Peek();
                        if (GetPrecedence(top) >= GetPrecedence(token) && token != "^")
                        {
                            outputQueue.Enqueue(operatorStack.Pop());
                        }
                        else
                        {
                            break;
                        }
                    }
                    operatorStack.Push(token);
                }
                else
                {
                    return false;
                }

                lastToken = token;
            }

            while (operatorStack.Count > 0)
            {
                string op = operatorStack.Pop();
                if (op is "(" or ")") return false;
                outputQueue.Enqueue(op);
            }

            // Calcolo RPN
            var evalStack = new Stack<double>();
            while (outputQueue.Count > 0)
            {
                string token = outputQueue.Dequeue();
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    evalStack.Push(val);
                }
                else if (token == "u-")
                {
                    if (evalStack.Count < 1) return false;
                    evalStack.Push(-evalStack.Pop());
                }
                else if (IsOperator(token))
                {
                    if (evalStack.Count < 2) return false;
                    double b = evalStack.Pop();
                    double a = evalStack.Pop();

                    double res = token switch
                    {
                        "+" => a + b,
                        "-" => a - b,
                        "*" => a * b,
                        "/" => b == 0 ? double.PositiveInfinity : a / b,
                        "%" => a % b,
                        "^" => Math.Pow(a, b),
                        _ => 0
                    };
                    evalStack.Push(res);
                }
            }

            if (evalStack.Count == 1)
            {
                result = evalStack.Pop();
                return !double.IsNaN(result);
            }

            return false;
        }

        private static bool IsOperator(string s) => s is "+" or "-" or "*" or "/" or "^" or "%" or "u-";

        private static int GetPrecedence(string op) => op switch
        {
            "+" or "-" => 1,
            "*" or "/" or "%" => 2,
            "u-" => 3,
            "^" => 4,
            _ => 0
        };

        private static List<string> Tokenize(string expr)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < expr.Length)
            {
                char c = expr[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsDigit(c) || c == '.')
                {
                    int start = i;
                    bool hasDot = c == '.';
                    while (i + 1 < expr.Length && (char.IsDigit(expr[i + 1]) || (!hasDot && expr[i + 1] == '.')))
                    {
                        if (expr[i + 1] == '.') hasDot = true;
                        i++;
                    }
                    tokens.Add(expr.Substring(start, i - start + 1));
                    i++;
                }
                else if (c is '+' or '-' or '*' or '/' or '^' or '%' or '(' or ')')
                {
                    tokens.Add(c.ToString());
                    i++;
                }
                else
                {
                    i++;
                }
            }
            return tokens;
        }
    }
}
