using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;

namespace DtkSymbolDiff
{
    class Symbol
    {
        public string name;
        public uint address;
        public int size;

        public Symbol(string name, uint address, int size)
        {
            this.name = name;
            this.address = address;
            this.size = size;
        }

        public new string ToString()
        {
            return string.Format("{0} (0x{1:X8}, size 0x{2:X})", name, address, size);
        }
    }

    class SymbolMatch
    {
        public int list1StartIndex, list1EndIndex;
        public int list2StartIndex, list2EndIndex;
        public int length;

        public SymbolMatch(int startIndex1, int startIndex2, int length)
        {
            list1StartIndex = startIndex1;
            list1EndIndex = list1StartIndex + length;
            list2StartIndex = startIndex2;
            list2EndIndex = list2StartIndex + length;
            this.length = length;
        }
    }

    internal class Program
    {

        static StreamWriter sw;

        static void Main(string[] args)
        {
            if(args.Length < 3)
            {
                Console.WriteLine("Usage: DtkSymbolDiff <symbol file 1> <symbol file 2> <output filename>");
                return;
            }

            sw = new StreamWriter(args[2], false);

            Console.WriteLine("Reading symbol files");

            //Read the symbol files
            List<Symbol> symbolFile1 = ReadSymbolFile(args[0]);
            List<Symbol> symbolFile2 = ReadSymbolFile(args[1]);

            //Decoding failed, return
            if(symbolFile1 == null || symbolFile2 == null)
            {
                return;
            }

            Console.WriteLine("Comparing symbol files");

            DiffSymbolLists(symbolFile1, symbolFile2);

            Console.WriteLine("Finished!");

            //Write to the file
            sw.Flush();
        }

        //TODO: improve the algorithm to not need to limit the match length
        static void DiffSymbolLists(List<Symbol> symbolList1, List<Symbol> symbolList2)
        {
            int startIndex = 0;
            int length1 = symbolList1.Count;
            int length2 = symbolList2.Count;
            List<SymbolMatch> matches = new List<SymbolMatch>();
            const int matchThreshold = 3; //Threshold for what length matches to show to avoid clutter
   
            //Find all the matches between the two symbol lists
            for (int i = 0; i < length1; i++)
            {
                //Try from every starting point
                for(int j = startIndex; j < length2; j++)
                {
                    int matchLength = 0;
                    int offset = 0;

                    //Keep going until a mismatch is found (name and size don't match)
                    while (CheckIfSymbolsMatch(symbolList1[i + offset], symbolList2[j + offset]))
                    {
                        matchLength++;
                        offset++;

                        if (i + offset >= length1 || j + offset >= length2) break;
                    }

                    if (matchLength >= matchThreshold)
                    {
                        SymbolMatch match = new SymbolMatch(i, j, matchLength);
                        matches.Add(match);

                        i += offset;
                        startIndex += offset;
                        break;
                    }
                }
            }

            int numMatches = matches.Count;

            //Print the matches
            for(int i = 0; i < numMatches; i++)
            {
                SymbolMatch match = matches[i];

                sw.WriteLine(string.Format("Lines {0}-{1} in list 1 match with lines {2}-{3} in list 2 ({4} total symbols)",
                    match.list1StartIndex + 1, match.list1EndIndex, match.list2StartIndex + 1, match.list2EndIndex, match.length));
                sw.WriteLine("First symbol of list 1: " + symbolList1[match.list1StartIndex].ToString());
                sw.WriteLine("First symbol of list 2: " + symbolList2[match.list2StartIndex].ToString());
                sw.WriteLine();

                //Print nonmatched symbols inbetween

                int mismatchEndIndex1 = 0, mismatchEndIndex2 = 0;

                /* If this is the last match, use the lengths of each respective list as the end index. Otherwise, use the start
                index of the next match. */
                if (i == matches.Count - 1)
                {
                    mismatchEndIndex1 = length1;
                    mismatchEndIndex2 = length2;

                }
                else
                {
                    SymbolMatch nextMatch = matches[i + 1];
                    mismatchEndIndex1 = nextMatch.list1StartIndex;
                    mismatchEndIndex2 = nextMatch.list2StartIndex;
                }

                int mismatchStartIndex1 = match.list1EndIndex;
                int mismatchStartIndex2 = match.list2EndIndex;

                if (mismatchStartIndex1 < mismatchEndIndex1)
                {
                    int length = mismatchEndIndex1 - mismatchStartIndex1;
                    if (length > 1) sw.WriteLine("List 1 mismatches (lines {0}-{1}, {2} total symbols):",
                        mismatchStartIndex1 + 1, mismatchEndIndex1, length);
                    else sw.WriteLine("List 1 mismatches (line {0}):", mismatchStartIndex1 + 1);

                    for (int k = mismatchStartIndex1; k < mismatchEndIndex1; k++)
                    {
                        sw.WriteLine(symbolList1[k].ToString());
                    }
                    sw.WriteLine();
                }

                if (mismatchStartIndex1 < mismatchEndIndex2)
                {
                    int length = mismatchEndIndex2 - mismatchStartIndex2;
                    if (length > 1) sw.WriteLine("List 2 mismatches (lines {0}-{1}, {2} total symbols):",
                        mismatchStartIndex2 + 1, mismatchEndIndex2, length);
                    else sw.WriteLine("List 2 mismatches (line {0}):", mismatchStartIndex2 + 1);

                    for (int k = mismatchStartIndex2; k < mismatchEndIndex2; k++)
                    {
                        sw.WriteLine(symbolList2[k].ToString());
                    }
                    sw.WriteLine();
                }
            }
        }

        //Checks if a symbol matches first based on name, then on size.
        static bool CheckIfSymbolsMatch(Symbol s1, Symbol s2)
        {
            return s1.name == s2.name || s1.size == s2.size;
        }

        static List<Symbol> ReadSymbolFile(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("Error: could not find file \"{0}\"", path);
            }
            StreamReader sr = new StreamReader(path);
            List<Symbol> symbols = new List<Symbol>();
            Regex lineRegex = new Regex(@"^([^\s]+) = \.text:(0x[0-9A-F]{8});(?:.+size:(0x[0-9A-F]+)|).*$");

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine().Trim();

                if (line == "") continue;

                Match match = lineRegex.Match(line);

                if (match.Success)
                {
                    string name = match.Groups[1].Value;
                    uint address = Convert.ToUInt32(match.Groups[2].Value, 16);
                    int size = 0;

                    /* Size is allowed to be optional, so only parse it if the line has it. Otherwise, the size
                    is just set to 0. */
                ;
                if (match.Groups[3].Success) size = Convert.ToInt32(match.Groups[3].Value, 16);

                    Symbol symbol = new Symbol(name, address, size);
                    symbols.Add(symbol);
                }
                else
                {
                    Console.WriteLine("Error: invalid line in file \"{0}\"", path);
                    Console.WriteLine("Line: \"{0}\"", line);
                    return null;
                }
            }

            return symbols;
        }
    }
}
