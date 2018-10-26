using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace MathMLToCSharpLib.Entities
{
    /// <summary>
    /// Root element in  Not to be confused with <c>System.Math</c>.
    /// </summary>
    [Serializable]
    public class Math : WithBuildableContents, IXmlSerializable
    {
        #region Private Stringparsing Methods
        private static string MathMLOperatorsToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string s in MathMLOperators())
            {
                sb.Append("|" + Regex.Escape(s));
            }
            return sb.ToString();
        }

        private static string[] SupportedMathFunctions()
        {
            return new[] { "sqrt" };
        }

        private static string[] MathMLOperators()
        {
            return new[] { "+", "-", "*", "/", "^", "(", ")" };
        }

        private static string[] StackableMathMLOperators()
        {
            return new[] { "+", "-", "(", ")" };
        }

        private static bool IsOperator(string s)
        {
            return MathMLOperators().Contains(s);
        }

        private static bool IsSupportedMathFunction(string s)
        {
            return SupportedMathFunctions().Contains(s);
        }


        private static bool IsStackableOperator(string s)
        {
            return StackableMathMLOperators().Contains(s);
        }

        private static bool IsBracketSign(string s)
        {
            return s == "(" | s == ")";
        }

        /// <summary>
        /// Returns true if string contains any special character that is no operator
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private static bool ContainsInvalidSigns(string s)
        {
            foreach (Char c in s)
            {
                if (!(Char.IsNumber(c) | Char.IsLetter(c) | IsOperator(c.ToString())))
                    return true;
            }
            return false;
        }

        private static List<IBuildable> GetMathElements(string formula)
        {
            List<IBuildable> elements = new List<IBuildable>();
            List<string> elementStrings = SplitFormula(formula);

            /* Create MathML elements from string */
            foreach (string s in elementStrings)
            {
                /* Handle operator */
                if (IsOperator(s))
                    elements.Add(new Mo(s));

                /* Handle numeric numbers */
                else if (Char.IsNumber(s[0]))
                    elements.Add(new Mn(s));

                /* Default: Handle identifier */
                else
                    elements.Add(new Mi(s));
            }

            return elements;
        }

        /// <summary>
        /// Groups elements in Mrow objects
        /// </summary>
        /// <param name="elements"></param>
        private static void GroupMathElements(List<IBuildable> elements)
        {
            Stack<int> openingBracketPos = new Stack<int>();
            for (int i = 0; i < elements.Count; i++)
            {
                IBuildable element = elements[i];

                //Check for operators
                if (element is Mo)
                {
                    //Set opening bracket position
                    if (((Mo)element).IsOpeningBrace)
                        openingBracketPos.Push(i);
                    //Group elements to Mrow element
                    else if (((Mo)element).IsClosingBrace)
                    {
                        //Get row content
                        List<IBuildable> subElements = new List<IBuildable>();
                        for (int j = i; j >= openingBracketPos.Peek(); j--)
                        {
                            subElements.Add(elements[j]);
                            elements.RemoveAt(j);
                        }

                        //Set content to row and reset loop position behind row
                        Mrow row = new Mrow(subElements.Reverse<IBuildable>().ToArray());
                        elements.Insert(openingBracketPos.Peek(), row);
                        i = openingBracketPos.Pop();
                    }
                }
            }
        }

        /// <summary>
        /// Replaces operators that can be expressed by other objects
        /// </summary>
        /// <param name="elements"></param>
        private static bool ReplaceOperators(ref List<IBuildable> elements)
        {
            bool changesMade = false;
            var replacedArray = ReplaceOperators(elements.ToArray());
            if (elements.Count != replacedArray.Length)
                changesMade = true;

            elements = replacedArray.ToList();

            return changesMade;
        }


        private static IBuildable[] ReplaceOperators(IBuildable[] input)
        {
            List<IBuildable> elements = input.ToList();
            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i] is Mo)
                {
                    Mo mo = (Mo)elements[i];
                    if (mo.content == "^")
                    {
                        Msup msup = new Msup(elements[i - 1], elements[i + 1]);
                        elements[i - 1] = msup;
                        elements.RemoveAt(i + 1);
                        elements.RemoveAt(i);
                    }
                    //BUG:MathMLToCSharp bug: Mfrac nicht parsbar
                    else if (mo.content == "/")
                    {
                        elements[i - 1] = new Mfrac(elements[i - 1], elements[i + 1]);
                        elements.RemoveAt(i + 1);
                        elements.RemoveAt(i);
                    }
                }
                else if (elements[i] is WithBuildableContents)
                {
                    var container = (WithBuildableContents)elements[i];
                    container.contents = ReplaceOperators(container.contents);
                }

                else if (elements[i] is WithBinaryContent)
                {
                    var container = (WithBinaryContent)elements[i];
                    container.first = ReplaceOperators(new[] { container.first }).FirstOrDefault();
                    container.second = ReplaceOperators(new[] { container.second }).FirstOrDefault();
                }
            }

            return elements.ToArray();
        }

        /// <summary>
        /// Replaces Mi tags if they contain an existing tag (e.g. "sqrt" -> Msqrt)
        /// </summary>
        /// <param name="elements"></param>
        private static void ReplaceIdentifiers(List<IBuildable> elements)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i] is Mi)
                {
                    Mi mi = (Mi)elements[i];
                    if (mi.content == "sqrt")
                    {
                        elements[i] = new Msqrt(elements[i + 1]);
                        elements.RemoveAt(i + 1);
                    }
                }
            }
        }

        private static void RemoveUnneededBrackets(ref List<IBuildable> elements)
        {
            var input = elements.ToArray();
            bool hasChanged = true;
            int oldLength = input.Length;
            while (hasChanged)
            {
                input = RemoveUnneededBrackets(input);
                if (input.Length == oldLength)
                    hasChanged = false;
                else
                    oldLength = input.Length;
            }

            elements = input.ToList();
        }

        private static IBuildable[] RemoveUnneededBrackets(IBuildable[] input, IBuildable prevContainer = null)
        {
            List<IBuildable> elements = input.ToList();

            for (int i = 0; i < elements.Count; i++)
            {
                if (elements.Count >= 2)
                {
                    Mo first = elements.FirstOrDefault() as Mo;
                    Mo last = elements.LastOrDefault() as Mo;

                    if ((first != null && first.content == "(" && last != null && last.content == ")") &
                        (prevContainer != null && prevContainer is WithBuildableContents))

                    {
                        elements.Remove(first);
                        elements.Remove(last);
                    }

                }
                if (elements[i] is WithBuildableContents)
                {
                    var container = (WithBuildableContents)elements[i];
                    container.contents = RemoveUnneededBrackets(container.contents, container);
                }
                else if (elements[i] is WithBinaryContent)
                {
                    var container = (WithBinaryContent)elements[i];
                    container.first = RemoveUnneededBrackets(new[] { container.first }).FirstOrDefault();
                    container.second = RemoveUnneededBrackets(new[] { container.second }).FirstOrDefault();
                }
            }

            return elements.ToArray();
        }
        #endregion

        #region ToString-Conversion Methods
        /// <summary>
        /// Searches for the first occurence a substring inside of a string and returns it's first index
        /// </summary>
        /// <param name="s"></param>
        /// <param name="substring"></param>
        /// <returns></returns>
        private int GetFunctionStartingIndex(List<string> split, string function)
        {
            for (int i = 0; i < split.Count; i++)
                if (split[i] == function)
                    return i;

            return -1;
        }

        private int GetFunctionEndingIndex(List<string> split, string function, int startIndex)
        {
            int bracketCount = 0;
            for (int i = startIndex; i < split.Count; i++)
            {
                if (split[i] == "(")
                    bracketCount++;
                else if (split[i] == ")")
                    bracketCount--;

                if (bracketCount == 0 & i != startIndex)
                    return i;
            }

            return -1;
        }

        private string GlueMathParameter(List<string> split, int startIndex, int lastIndex, ref int nextIndex)
        {
            StringBuilder gluedParameter = new StringBuilder();
            int appendCount = 0;
            //Glue firstElement
            for (int i = startIndex; i <= lastIndex; i++)
            {
                if (split[i].Contains(','))
                {
                    gluedParameter.Append(split[i].Replace(",", "").Trim());
                    split[i] = "";
                    appendCount++;
                    nextIndex = i + 1;

                    //Set brackets if multiple items found
                    if (appendCount > 1)
                        gluedParameter = new StringBuilder("(" + gluedParameter.ToString() + ")");

                    break;
                }
                else
                {
                    gluedParameter.Append(split[i]);
                    split[i] = "";
                    appendCount++;
                }
            }
            return gluedParameter.ToString();
        }


        private void ReplaceMathFunction(List<string> split, string function, string op)
        {
            List<string> elements = new List<string>();
            int startOffset = 2;
            int startIndex = GetFunctionStartingIndex(split, function);
            startIndex += startOffset;
            int lastIndex = GetFunctionEndingIndex(split, function, startIndex);
            int actualIndex = startIndex;

            while (actualIndex != -1)
            {
                int nextIndex = -1;
                elements.Add(GlueMathParameter(split, actualIndex, lastIndex, ref nextIndex));
                elements.Add(op);
                actualIndex = nextIndex;
            }

            //Replace brackets
            split[lastIndex] = "";
            split[startIndex + 1] = "";

            StringBuilder gluedFormula = new StringBuilder();
            foreach (string s in elements)
                gluedFormula.Append(s);

            split[startIndex] = gluedFormula.ToString();
        }

        private string ReplaceSqrt(List<string> split)
        {
            return "";
        }

        private string ReplaceAllMathFunctions(string ret)
        {
            List<string> split = SplitFormula(ret);
            while (split.FirstOrDefault(x => x.Contains("Math.Pow")) != null)
            {
                ReplaceMathFunction(split, "Math.Pow", "^");
                //Check for Math.Pow();
            }

            StringBuilder gluedFormula = new StringBuilder();
            foreach (string s in split)
                gluedFormula.Append(s);

            return gluedFormula.ToString();
        }
        #endregion

        #region Constructors
        public Math() { }
        public Math(string content) : base(new IBuildable[] { }) { /* just in case */ }
        public Math(IBuildable content) : base(new[] { content }) { }
        public Math(IBuildable[] contents) : base(contents) { }
        #endregion

        #region IXmlSerializable Methods
        public XmlSchema GetSchema()
        {
            return null;
        }

        public void ReadXml(XmlReader reader)
        {
            List<IBuildable> tempContent = new List<IBuildable>();
            while (reader.Read())
            {
                IBuildable element = null;
                if (reader.IsStartElement() & reader.Name != "Math")
                {
                    Type type = Type.GetType(this.GetType().Namespace + "." + reader.Name);
                    element = (IBuildable)Activator.CreateInstance(type);
                    element.ReadXml(reader);
                    tempContent.Add(element);
                }
                else if (reader.NodeType == XmlNodeType.EndElement & reader.Name == this.GetType().Name)
                {
                    contents = tempContent.ToArray();
                }
            }

        }

        public void WriteXml(XmlWriter writer)
        {
            foreach (IBuildable item in contents)
                item.WriteXml(writer);
        }
        #endregion

        public override void Visit(StringBuilder sb, BuildContext context)
        {
            base.Visit(sb, context);

            // todo: this assumes that there is only one statement, which may not be the case
            if (sb.ToString().Length > 0)
                sb.Append(";");

            // sums
            int j = context.Sums.Count;
            if (j > 0)
            {
                var builder = new StringBuilder();
                foreach (var v in context.Sums)
                {
                    builder.AppendLine(v.First.Expression(context));
                }
                sb.Insert(0, builder.ToString());
            }

            // variables
            int i = context.Vars.Count;
            if (i > 0)
            {
                var builder = new StringBuilder();
                foreach (var v in context.Vars)
                {
                    builder.Append(Enum.GetName(typeof(EquationDataType), context.Options.EqnDataType).ToLower());
                    builder.Append(" ");
                    builder.Append(v);
                    builder.Append(" = 0.0"); // this is a *must*
                    if (context.Options.NumberPostfix)
                        builder.Append(Semantics.postfixForDataType(context.Options.EqnDataType));
                    builder.AppendLine(";");
                }
                foreach (IBuildable ib in context.PossibleDivisionsByZero)
                {
                    //var b = new StringBuilder();
                    //ib.Visit(b, new BuildContext(Singleton.Instance.Options));
                    //builder.AppendFormat("Debug.Assert({0} != 0, \"Expression {0} is about to cause division by zero.\");",
                    //  b);
                    //builder.AppendLine();
                }
                sb.Insert(0, builder.ToString());
            }
        }

        /// <summary>
        /// Creates a new math object from a string formula, e.g. 'a+b*2'
        /// </summary>
        /// <param name="formula">The formula that shall be parsed, e.g. 'a+b*2'</param>
        /// <returns></returns>
        public static Math FromString(string formula)
        {
            List<IBuildable> mathElements = GetMathElements(formula);
            GroupMathElements(mathElements);

            bool changesMade = true;

            while (changesMade)
                changesMade = ReplaceOperators(ref mathElements);

            ReplaceIdentifiers(mathElements);
            RemoveUnneededBrackets(ref mathElements);

            if (mathElements.Count > 1)
                return new MathMLToCSharpLib.Entities.Math(new Mrow(mathElements.ToArray()));
            else
                return new MathMLToCSharpLib.Entities.Math(mathElements.ToArray());
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                this.Visit(sb, new MathMLToCSharpLib.BuildContext());
            }
            catch (Exception ex)
            {
                return "Failed to parse MathMl! " + ex.Message;
            }

            var split = sb.ToString().Split(';').Where(x => x.Trim() != "")?.ToList();
            if (split != null && split.Count > 0)
            {
                string ret = split.LastOrDefault();
                ret = ReplaceAllMathFunctions(ret);
                return ret.Replace("\r", "").Replace("\n", "");
            }
            return "";
        }

        public static List<string> SplitFormula(string formula)
        {
            return Regex.Split(formula, @"(\s+" + @MathMLOperatorsToString() + ")").Where(x => x.Trim() != "").Select(x => x.Trim()).ToList(); // "() = term, | = delimiter, \ escape, s+ = spaces... splits for chars: ' ', (, )
        }

        /// <summary>
        /// Returns true if given formula can be parsed
        /// </summary>
        /// <param name="formula">The formula that shall be checked</param>
        /// <returns></returns>
        public static bool CheckFormulaSyntax(string formula, List<string> inputs)
        {
            var elements = SplitFormula(formula);
            double possibleNumber = 0.0;
            int bracketCount = 0;
            Stack<string> elementStack = new Stack<string>();
            bool prevWasFunction = false;

            foreach (string s in elements)
            {
                //Check if prev element forces the current element to be an opening bracket
                if (prevWasFunction && s != "(")
                    return false;

                //Check if element is valid input, operator or number
                if (!(inputs.Contains(s) | IsOperator(s) | IsSupportedMathFunction(s) | Double.TryParse(s, out possibleNumber)))
                    return false;

                //Check for multiple operators that can't be stacked
                if (elementStack.Count != 0 && IsOperator(elementStack.Peek()) && IsOperator(s))
                {
                    if (!(IsStackableOperator(elementStack.Peek()) & IsStackableOperator(s))
                        & !(IsBracketSign(elementStack.Peek()) | IsBracketSign(s)))
                    {
                        return false;
                    }
                }

                elementStack.Push(s);
                //Count bracket opening/closing
                if (s == "(")
                    bracketCount++;
                else if (s == ")")
                    bracketCount--;

                //Check for invalid signs
                if (ContainsInvalidSigns(s))
                    return false;

                //Set prev function flag if last element was a mathematical function
                if (IsSupportedMathFunction(s))
                    prevWasFunction = true;
                else
                    prevWasFunction = false;
            }

            //Check if last element was a mathematical function
            if (prevWasFunction)
                return false;
            //Check bracket mismatch
            else if (bracketCount == 0 && elementStack.Count != 0 &&
                (!IsOperator(elementStack.Peek()) | elementStack.Peek() == ")"))
                return true;
            else if (elements.Count == 0)
                return true;
            else
                return false;
        }

        public IEnumerable<T> Elements<T>()
        {
            if (this.contents == null || this.contents.Length == 0)
                return new List<T>();

            return RecursiveGetElements<T>(this.contents);
        }

        private IEnumerable<T> RecursiveGetElements<T>(IBuildable[] contents)
        {
            List<T> ret = new List<T>();

            foreach (IBuildable c in contents)
            {
                if (c is T)
                    ret.Add((T)c);

                if (c is WithBuildableContents)
                    ret.AddRange(RecursiveGetElements<T>(((WithBuildableContents)c).contents));
                else if (c is WithBinaryContent)
                    ret.AddRange(RecursiveGetElements<T>(new[] { ((WithBinaryContent)c).first, ((WithBinaryContent)c).second }));
                else if (c is WithBuildableContent)
                    ret.AddRange(RecursiveGetElements<T>(new[] { ((WithBuildableContent)c).content }));
                
            }

            return ret;
        }
    }
}