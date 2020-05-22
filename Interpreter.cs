using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CueRead
{
    interface IMixer
    {
        void SetChannel(ushort ch, byte val);
    }

    interface IXc2Element
    {
        void AddNumParam(int v);
        void AddStrParam(string longerData);
    }

    interface IXc2Complex : IXc2Element
    {
        bool IsClosedTag();
        void Finish();

        List<IXc2Element> GetChildren();
        void AppendChild(IXc2Element child);
    }

    class Document
    {
        IXc2Element head, body;

        public Document() => Expression.Empty();

        public Head HeadElement
        {
            get => (Head)head;
            set => head = value;
        }
        public Body BodyElement
        {
            get => (Body)body;
            set => body = value;
        }

        public abstract class Head : IXc2Element
        {
            protected string title = string.Empty;
            public SetOfValues sov = SetOfValues.Empty;
            public enum SetOfValues
            {
                Empty = 0,
                P400 = 4,
                P676 = 6
            }

            public string Title => title;
            public abstract void AddNumParam(int v);
            public abstract void AddStrParam(string longerData);
        }

        public class PredefinedSchemaHead : Head
        {
            int schemaId = 0;

            public int SchemaID => schemaId;

            public PredefinedSchemaHead() => Expression.Empty();
            public PredefinedSchemaHead(int schemaId) => this.schemaId = schemaId;
            public override void AddNumParam(int v)
            {
                if (sov == SetOfValues.Empty)
                    sov = (SetOfValues)v;
                else
                    schemaId = v;
            }
            public override void AddStrParam(string longerData) => title = longerData;
        }

        public class LinkedSchemaHead : Head
        {
            string schemaPath;
            public LinkedSchemaHead() => Expression.Empty();
            public override void AddNumParam(int v) => sov = (SetOfValues)v;

            public override void AddStrParam(string longerData)
            {
                if (title == string.Empty)
                    title = longerData;
                else
                    schemaPath = longerData;
            }
        }

        public class Body : IXc2Complex
        {
            List<IXc2Element> children = new List<IXc2Element>();
            bool isClosed = false;

            public Body()
            {
            }

            void IXc2Element.AddNumParam(int v) => Expression.Empty();

            void IXc2Element.AddStrParam(string longerData) => Expression.Empty();

            void IXc2Complex.AppendChild(IXc2Element child) => children.Add(child);

            void IXc2Complex.Finish() => isClosed = true;
            bool IXc2Complex.IsClosedTag() => isClosed;

            List<IXc2Element> IXc2Complex.GetChildren() => children;
        }
    }

    class KeyFrame : IXc2Complex
    {
        private List<IXc2Element> bp = new List<IXc2Element>();
        bool isClosed = false;

        public int Length => bp.Count;

        public int Delay;
        public KeyFrame(int ms) => Delay = ms;
        public KeyFrame(int ms, bool isEmpty) : this(ms) => isClosed = isEmpty;

        void IXc2Complex.Finish() => isClosed = true;
        bool IXc2Complex.IsClosedTag() => isClosed;

        void IXc2Complex.AppendChild(IXc2Element child) => this.bp.Add(child);

        void IXc2Element.AddNumParam(int v) => Delay = v;
        void IXc2Element.AddStrParam(string longerData) => Expression.Empty();

        List<IXc2Element> IXc2Complex.GetChildren() => bp;

        public override string ToString() => "KeyFrame lasts for " + Delay + " ms.";
    }

    class Delay : IXc2Complex
    {
        public enum Mode
        {
            BeatSignal,
            MilliSeconds
        }

        Mode mode;
        int del = 0;

        public Mode GetMode => mode;

        public Delay() => mode = Mode.BeatSignal;
        public Delay(int ms)
        {
            mode = Mode.MilliSeconds;
            del = ms;
        }

        bool IXc2Complex.IsClosedTag() => true;
        void IXc2Complex.Finish() => Expression.Empty();
        void IXc2Complex.AppendChild(IXc2Element child) => Expression.Empty();

        void IXc2Element.AddNumParam(int v)
        {
            del = v;
            mode = Mode.MilliSeconds;
        }
        void IXc2Element.AddStrParam(string longerData) => Expression.Empty();

        List<IXc2Element> IXc2Complex.GetChildren() => new List<IXc2Element>(0);

        public override string ToString() => mode == Mode.BeatSignal ? "Wait for BeatSignal." : "Delay " + del + " ms";
    }

    abstract class Iteration : IXc2Complex
    {
        protected List<IXc2Complex> children = new List<IXc2Complex>();
        protected bool isClosed = false;

        public abstract void AddNumParam(int v);

        void IXc2Element.AddStrParam(string longerData) => Expression.Empty();

        public void AppendChild(IXc2Element child) => children.Add((IXc2Complex)child);

        public void Finish() => isClosed = true;
        public bool IsClosedTag() => isClosed;

        public List<IXc2Element> GetChildren() => children.Cast<IXc2Element>().ToList();
    }

    class ForIteration : Iteration
    {
        public int Iter = 0;
        public int Target = 0;
        public Direction Direct = Direction.Increment;
        public enum Direction
        {
            Increment, Decrement
        }

        public ForIteration(int i, int target, Direction d)
        {
            Iter = i;
            Target = target;
            Direct = d;
        }
        public ForIteration() : this(0, 0, Direction.Increment) => Expression.Empty();
        public override void AddNumParam(int v) => Target += v;
    }

    class InfiniteIteration : Iteration
    {
        public override void AddNumParam(int v) => Expression.Empty();
    }

    class ChannelData : IXc2Element
    {
        public ushort channel = 0;
        public byte value;

        public ChannelData() => Expression.Empty();
        public ChannelData(ushort channel, byte value)
        {
            this.channel = channel;
            this.value = value;
        }

        void IXc2Element.AddNumParam(int v)
        {
            if (channel == 0)
                channel = (ushort)v;
            else
                value = (byte)v;
        }

        void IXc2Element.AddStrParam(string longerData) => Expression.Empty();

        public override string ToString() => "Ch " + channel + ": " + value;
    }

    class Program
    {
        enum States
        {
            Default,
            LetterC,
            LetterK,
            LetterB,
            LetterW,
            LetterF,
            LetterI,
            QuotationMark,
            Number,
            NumberHex,
            NumberDec,
            NumberBin,
            Command,
            WaitParamNum,
            WaitParamStr,
            ReceivedParamNum,
            ReceivedParamStr,
            NoNumParam,
            NoStrParam
        }

        static List<IXc2Complex> cueList = new List<IXc2Complex>();
        static List<IXc2Complex> iterations = new List<IXc2Complex>();

        static Document document;
        static IXc2Complex currentElement;

        static IXc2Complex GetLast() => cueList.FindLast(x => !x.IsClosedTag());
        static void CloseLast()
        {
            IXc2Complex last = GetLast();
            last.Finish();
            Console.WriteLine(last.ToString());
        }

        static void Main()
        {
            string raw = string.Empty;

            using (FileStream fs = new FileStream(@"C:\Users\fauga\Desktop\pelda_cuelist.x2d", FileMode.Open, FileAccess.Read))
            {
                StreamReader sr = new StreamReader(fs);
                while (!sr.EndOfStream)
                {
                    string[] arr = sr.ReadLine().Split('"');
                    for (int i = 0; i < arr.Length; i++)
                        raw += i % 2 > 0 ? '"' + arr[i] + '"' : Regex.Replace(arr[i], @"\s+", string.Empty);
                }
                sr.Close();
                fs.Close();
            }

            Console.WriteLine("Nyers tárgykód:" + Environment.NewLine + raw + Environment.NewLine);
            Console.WriteLine("Értelmezett szimbólumok:");

            States
                currentState = States.Default,
                nextState = States.Default;

            char previous = '\0';
            string longerData = string.Empty;
            int paramsLeft = 0;
            IXc2Element waits4Param = null;

            for (int i = 0; i < raw.Length; i++)
            {
                char letter = raw[i];
                char c = char.ToUpper(letter);

                switch (currentState)
                {
                    case States.Default:
                        switch (c)
                        {
                            case 'C': nextState = States.LetterC; break;
                            case 'K': nextState = States.LetterK; break;
                            case 'B': nextState = States.LetterB; break;
                            case 'W': nextState = States.LetterW; break;
                            case 'F': nextState = States.LetterF; break;
                            case 'I': nextState = States.LetterI; break;
                            case '"': nextState = States.QuotationMark; break;
                            case '0': nextState = States.Number; break;
                            case 'X': nextState = States.Command; break;
                            default:
                                nextState = States.Default;
                                Console.Write(c);
                                break;
                        }
                        break;
                    case States.LetterB:
                        if (c == 'P')
                        {
                            Console.Write("\nLoading channel data...");
                            waits4Param = new ChannelData();
                            GetLast().AppendChild(waits4Param);
                            paramsLeft = 2;
                            nextState = States.WaitParamNum;
                        }
                        else
                            nextState = States.Default;
                        break;
                    case States.LetterC:
                        if (c == 'L')
                            Console.WriteLine("CueList");
                        nextState = States.Default;
                        break;
                    case States.LetterF:
                        switch (c)
                        {
                            case 'I': { Console.Write("ForIteration"); Iteration it = new ForIteration(); break; }
                            case 'T': Console.Write("FadeTime"); break;
                        }
                        nextState = States.Default;
                        break;
                    case States.LetterI:
                        if (c == 'I')
                        {
                            Iteration it = new InfiniteIteration();
                            Console.Write("InfiniteIteration");
                        }
                        nextState = States.Default;
                        break;
                    case States.LetterK:
                        if (c == 'F')
                        {
                            if (char.IsUpper(previous))
                            {
                                Console.WriteLine("\nNEW KeyFrame!");
                                cueList.Add(new KeyFrame(0));
                                paramsLeft = 1;
                                nextState = States.WaitParamNum;
                            }
                            else if (char.IsUpper(letter))
                            {
                                Console.WriteLine("\nEmpty KeyFrame.");
                                cueList.Add(new KeyFrame(0, true));
                                paramsLeft = 1;
                                nextState = States.WaitParamNum;
                            }
                            else
                            {
                                Console.WriteLine("\nEnd of the KeyFrame.");
                                List<ChannelData> children = GetLast().GetChildren().Cast<ChannelData>().ToList();
                                foreach (ChannelData cd in children)
                                    Console.WriteLine(cd.ToString());
                                CloseLast();
                                nextState = States.Default;
                            }
                        }
                        break;
                    case States.LetterW:
                        switch (c)
                        {
                            case 'B':
                                Console.Write("<waitBeatSignal />");

                                break;
                            case 'M':
                                Console.Write("<waitMilliSeconds />");
                                break;
                        }
                        nextState = States.Default;
                        break;
                    case States.QuotationMark:
                        Console.Write(c);
                        nextState = (c == '"') ? States.Default : States.QuotationMark;
                        break;
                    case States.Number:
                        Console.Write(' ');
                        switch (c)
                        {
                            case 'H':
                            case 'X':
                                Console.Write('H');
                                nextState = States.NumberHex;
                                break;
                            case 'D':
                                Console.Write('D');
                                nextState = States.NumberDec;
                                break;
                            case 'B':
                                Console.Write('B');
                                nextState = States.NumberBin;
                                break;
                        }
                        break;
                    case States.NumberHex:
                    case States.NumberDec:
                    case States.NumberBin:
                        if (c == ';')
                            nextState = States.Default;
                        else
                            Console.Write(c);
                        break;
                    case States.Command:
                        switch (c)
                        {
                            case '!':
                                Console.WriteLine("Document");
                                document = new Document();
                                nextState = States.Default;
                                break;
                            case '@':
                            {
                                Console.WriteLine("Head");
                                nextState = States.WaitParamNum;
                                break;
                            }
                            case '+':
                                Console.WriteLine("Title");
                                nextState = States.WaitParamStr;
                                break;
                        }
                        break;
                    case States.WaitParamNum:
                        if (c == ';')
                            nextState = States.ReceivedParamNum;
                        else
                            longerData += letter;
                        if (longerData[0] != '0')
                            nextState = States.NoNumParam;
                        break;
                    case States.WaitParamStr:

                        break;
                    case States.ReceivedParamNum:
                        if (waits4Param != null)
                            waits4Param.AddNumParam(int.Parse(longerData.Substring(2), System.Globalization.NumberStyles.HexNumber));
                        else
                            GetLast().AddNumParam(int.Parse(longerData.Substring(2), System.Globalization.NumberStyles.HexNumber));
                        i--;
                        paramsLeft--;
                        longerData = string.Empty;
                        nextState = paramsLeft > 0 ? States.WaitParamNum : States.Default;
                        break;
                    case States.ReceivedParamStr:
                        GetLast().AddStrParam(longerData);
                        i--;
                        paramsLeft--;
                        longerData = string.Empty;
                        nextState = paramsLeft > 0 ? States.WaitParamStr : States.Default;
                        break;
                    case States.NoNumParam:
                        Console.WriteLine("There is no parameter.");
                        i -= 2;
                        paramsLeft = 0;
                        longerData = string.Empty;
                        nextState = States.Default;
                        break;
                }
                currentState = nextState;
                previous = letter;
            }

            Console.WriteLine(Environment.NewLine);
            Console.WriteLine("Count of KeyFrames:" + cueList.Count);
            Console.WriteLine("---------------------------------------------------------------------------");
            Console.WriteLine("Dump:");

            foreach (IXc2Complex cue in cueList)
            {
                KeyFrame kf = (KeyFrame)cue;
                Console.WriteLine(cue.ToString() + " childs: " + kf.Length);
                foreach (ChannelData cd in cue.GetChildren())
                    Console.WriteLine(cd.ToString());
            }

            Console.Write("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
