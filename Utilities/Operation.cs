using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;

namespace AeonHacs.Utilities
{
    //TODO look at DataTables as an alternative to this.
    [JsonObject(MemberSerialization.OptIn)]
    public class Operation : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        [JsonProperty(Order = -99)]
        public string Name { get; set; }

        public virtual double Execute(double input)
        {
            return input;
        }

        protected void NotifyPropertyChanged(string propertyName = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Name) ? "Operation" : Name;
    }

    public class OperationSet : Operation
    {
        [JsonProperty]
        public List<Operation> Operations { get; set; }

        public OperationSet() { Operations = new List<Operation>(); }

        public OperationSet(params Operation[] operations)
        {
            Operations = new List<Operation>();
            Operations.AddRange(operations);
        }

        public override double Execute(double input)
        {
            double value = input;

            foreach (Operation o in Operations)
            {
                value = o.Execute(value);
            }

            return value;
        }

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Name) ? "Operation Set" : Name;
    }

    public class Lookup : Operation
    {
        [JsonProperty]
        public string Argument { get { return getArgument(); } set { setArgument(value); } }

        LookupTable table;

        public Lookup() { }

        public override double Execute(double input)
        {
            try { return table.Interpolate(input); }
            catch { return input; }
        }

        string getArgument()
        {
            return table?.filename;
        }

        void setArgument(string value)
        {
            table = new LookupTable(value);
        }

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Name) ? "Lookup Table" : Name;
    }

    public class Arithmetic : Operation
    {
        public enum Operators { None, Add, Subtract, Multiply, Divide, Power, Ln, Log }

        private static Dictionary<string, Operators> SymbolOperatorDictionary = new Dictionary<string, Operators>
        {
            { "+", Operators.Add },
            { "-", Operators.Subtract },
            { "*", Operators.Multiply },
            { "/", Operators.Divide },
            { "^", Operators.Power },
            { "ln", Operators.Ln },
            { "log", Operators.Log }
        };

        private static Dictionary<Operators, string> OperatorSymbolDictionary = new Dictionary<Operators, string>
        {
            { Operators.Add, "+" },
            { Operators.Subtract, "-" },
            { Operators.Multiply, "*" },
            { Operators.Divide, "/" },
            { Operators.Power, "^" },
            { Operators.Ln, "Ln" },
            { Operators.Log, "Log" }
        };

        public static Operators ParseOperator(string operatorSymbol)
        {
            try { return SymbolOperatorDictionary[operatorSymbol.ToLower()]; }
            catch { return Operators.None; }
        }

        public static string OperatorSymbol(Operators operatorType)
        {
            try { return OperatorSymbolDictionary[operatorType]; }
            catch { return ""; }
        }

        private string inputSymbol = "x";

        string formula = "";

        [JsonProperty]
        public string Argument { get { return getArgument(); } set { setArgument(value); } }

        bool inputFirst = true;

        public bool InputFirst
        {
            get { return inputFirst; }
            set { inputFirst = value; updateFormula(); compile(); }
        }

        Operators _operator = Operators.None;

        public Operators Operator
        {
            get { return _operator; }
            set { _operator = value; updateFormula(); compile(); }
        }

        double operand;

        public double Operand
        {
            get { return operand; }
            set { operand = value; updateFormula(); }
        }

        Func<double, double, double> exec;

        public Arithmetic() { }

        public Arithmetic(string argument)
        {
            Argument = argument;
        }

        public override double Execute(double input)
        {
            try { return exec(input, operand); }
            catch { return input; }
        }

        string getArgument()
        {
            return formula;
        }

        void setArgument(string value)
        {
            formula = value;
            parseProperties();
            compile();
            updateFormula();
        }

        void updateFormula()
        {
            if (_operator == Operators.Ln)
                formula = Arithmetic.OperatorSymbol(_operator) + "(" + inputSymbol + ")";
            else if (_operator == Operators.Log)
            {
                formula = Arithmetic.OperatorSymbol(_operator) + (inputFirst ? operand.ToString() : inputSymbol) + "(" + (inputFirst ? inputSymbol : operand.ToString()) + ")";
            }
            else if (_operator == Operators.Add && operand < 0)
            {
                formula = inputSymbol + OperatorSymbol(Operators.Subtract) + Math.Abs(operand);
            }
            else
            {
                formula = (inputFirst ? inputSymbol : operand.ToString()) + OperatorSymbol(_operator) + (inputFirst ? operand.ToString() : inputSymbol);
            }

            NotifyPropertyChanged(nameof(Argument));
            NotifyPropertyChanged(nameof(InputFirst));
            NotifyPropertyChanged(nameof(Operator));
            NotifyPropertyChanged(nameof(Operand));
        }

        void parseProperties()
        {
            try
            {
                string toParse = formula.ToLower().Replace("(", "").Replace(")", "");

                if (toParse.StartsWith("ln"))
                {
                    _operator = Operators.Ln;
                }
                else if (toParse.StartsWith("log"))
                {
                    _operator = Operators.Log;
                    toParse = toParse.Replace("log", "");

                    if (toParse.StartsWith(inputSymbol))
                        inputFirst = false;
                    toParse = toParse.Replace(inputSymbol, "");
                }
                else
                {
                    toParse = toParse.Replace(inputSymbol, "");

                    char testChar = toParse.First();
                    if (!Char.IsNumber(testChar))
                    {
                        _operator = ParseOperator(testChar.ToString());
                        toParse = toParse.Remove(0, 1);
                    }
                    else
                    {
                        inputFirst = false;
                        _operator = ParseOperator(toParse.Last().ToString());
                        toParse = toParse.Remove(toParse.Length - 1);
                    }
                }

                try
                {
                    operand = double.Parse(toParse);
                    if (_operator == Operators.Subtract && inputFirst)
                    {
                        _operator = Operators.Add;
                        operand = -operand;
                    }
                }
                catch { }
            }
            catch { }
        }

        void compile()
        {
            if (_operator == Operators.None)
                return;

            ParameterExpression input = Expression.Parameter(typeof(double), "input");
            ParameterExpression operand = Expression.Parameter(typeof(double), "operand");
            ParameterExpression left = inputFirst ? input : operand;
            ParameterExpression right = inputFirst ? operand : input;

            Expression execute = null;

            switch (_operator)
            {
                case Operators.None:
                    return;
                case Operators.Add:
                    execute = Expression.Add(left, right);
                    break;
                case Operators.Subtract:
                    execute = Expression.Subtract(left, right);
                    break;
                case Operators.Multiply:
                    execute = Expression.Multiply(left, right);
                    break;
                case Operators.Divide:
                    execute = Expression.Divide(left, right);
                    break;
                case Operators.Power:
                    execute = Expression.Power(left, right);
                    break;
                case Operators.Ln:
                    execute = Expression.Call(typeof(Math).GetMethod("Log", new[] { typeof(double) }), input);
                    break;
                case Operators.Log:
                    execute = Expression.Call(typeof(Math).GetMethod("Log", new[] { typeof(double), typeof(double) }), left, right);
                    break;
                default:
                    break;
            }

            exec = Expression.Lambda<Func<double, double, double>>
            (
                execute,
                input,
                operand
            ).Compile();
        }

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Name) ? Argument : $"{Name}: {Argument}";
    }
}
