using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;

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

    class SymbolMatchComparer : IComparer<SymbolMatch>
    {
        public int Compare(SymbolMatch x, SymbolMatch y)
        {
            if (x.list1StartIndex == y.list1StartIndex) return 0;
            else return x.list1StartIndex.CompareTo(y.list1StartIndex);
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

    struct DiffRange
    {
        public int rangeStart, rangeEnd;
        public int length;

        public DiffRange(int startIndex, int endIndex)
        {
            rangeStart = startIndex;
            rangeEnd = endIndex;
            length = endIndex - startIndex;
        }
    }

    internal class SymbolFileDiffer
    {
        public List<Symbol> symbolList1;
        public List<Symbol> symbolList2;
        public List<SymbolMatch> matches;
        public bool filesLoaded;

        public SymbolFileDiffer(string file1Path, string file2Path)
        {
            filesLoaded = false;
            matches = new List<SymbolMatch>();

            symbolList1 = ReadSymbolFile(file1Path);
            symbolList2 = ReadSymbolFile(file2Path);

            if (symbolList1 != null && symbolList2 != null)
            {
                filesLoaded = true;
            }
        }

        //Checks if a symbol matches first based on name, then on size.
        bool CheckIfSymbolsMatch(Symbol s1, Symbol s2)
        {
            return s1.name == s2.name || s1.size == s2.size;
        }

        List<Symbol> ReadSymbolFile(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("Error: could not find file \"{0}\"", path);
                return null;
            }

            StreamReader sr = new StreamReader(path);
            List<Symbol> symbols = new List<Symbol>();
            Regex lineRegex = new Regex(@"^([^\s]+) = .+:(0x[0-9A-F]{8});(?:.+size:(0x[0-9A-F]+)|).*$");

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine().Trim();

                //Ignore empty lines and comment lines
                if (line == "" || line.StartsWith("//")) continue;

                Match match = lineRegex.Match(line);

                if (match.Success)
                {
                    string name = match.Groups[1].Value;
                    uint address = Convert.ToUInt32(match.Groups[2].Value, 16);
                    int size = 0;

                    /* Size is allowed to be optional, so only parse it if the line has it. Otherwise, the size
                    is just set to 0. */
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


        public void PrintMatches(StreamWriter sw)
        {

            int numMatches = matches.Count;

            //Print the matches
            for (int i = 0; i < numMatches; i++)
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
                    mismatchEndIndex1 = symbolList1.Count;
                    mismatchEndIndex2 = symbolList2.Count;

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

                if (mismatchStartIndex2 < mismatchEndIndex2)
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

        public void DiffFiles()
        {
            byte[] list1CheckedLines = new byte[symbolList1.Count];
            byte[] list2CheckedLines = new byte[symbolList2.Count];

            DiffRange currentList1Range = new DiffRange(0, symbolList1.Count);
            DiffRange currentList2Range = new DiffRange(0, symbolList2.Count);

            //Stupid
            SymbolMatchComparer comparer = new SymbolMatchComparer();

            bool finished = false;


            while (!finished)
            {
                int list1StartIndex = currentList1Range.rangeStart;
                int list1EndIndex = currentList1Range.rangeEnd;
                int list2StartIndex = currentList2Range.rangeStart;
                int list2EndIndex = currentList2Range.rangeEnd;

                Debug.WriteLine("Current list 1 range: {0}-{1}, current list 2 range: {2}-{3}",
                list1StartIndex, list1EndIndex - 1, list2StartIndex, list2EndIndex - 1);

                //Find the longest match for the current range
                SymbolMatch match = FindLongestMatch(list1StartIndex, list2StartIndex,
                    list1EndIndex, list2EndIndex);

                bool invalidMatch = false;

                if (match.length > 0)
                {
                    /* Check if this match is inconsistent with the indices of previous matches. If so, reject it. */
                    for (int i = 0; i < matches.Count; i++)
                    {
                        SymbolMatch curMatch = matches[i];

                        bool matchPastList1 = curMatch.list1EndIndex > match.list1StartIndex;
                        bool matchPastList2 = curMatch.list2EndIndex > match.list2StartIndex;

                        //If the indices of both lists are past the ones of the new match, the match is valid.
                        if (matchPastList1 && matchPastList2) break;
                        else if (!matchPastList1 && !matchPastList2) continue; //If both still are less, keep going

                        //Otherwise, the order of the indices is inconsistent, so the match is probably invalid
                        invalidMatch = true;
                        break;

                    }
                }

                if (match.length == 0) invalidMatch = true;

                if (!invalidMatch)
                {

                    Debug.WriteLine("Found match (list 1 range: {0}-{1}, list 2 range: {2}-{3})",
                    match.list1StartIndex, match.list1EndIndex - 1, match.list2StartIndex, match.list2EndIndex - 1);

                    //Mark the matched range, and add the match to the list
                    Array.Fill<byte>(list1CheckedLines, 1, match.list1StartIndex, match.length);
                    Array.Fill<byte>(list2CheckedLines, 1, match.list2StartIndex, match.length);

                    //Put the new match into the list in order by the indices of the first list
                    int insertIndex = matches.BinarySearch(match, comparer);
                    if (insertIndex < 0) insertIndex = ~insertIndex;
                    matches.Insert(insertIndex, match);
                }
                else
                {
                    //If no match was found, rule out the range that comes earlier

                    int lastMatchPosList1 = 0;
                    int lastMatchPosList2 = 0;


                    for (int i = 0; i < matches.Count; i++)
                    {
                        SymbolMatch curMatch = matches[i];

                        if (curMatch.list1EndIndex <= list1StartIndex && curMatch.list2EndIndex <= list2StartIndex)
                        {
                            lastMatchPosList1 = curMatch.list1EndIndex;
                            lastMatchPosList2 = curMatch.list2EndIndex;
                        }
                        else
                        {
                            break;
                        }
                    }

                    int list1RelativePos = list1StartIndex - lastMatchPosList1;
                    int list2RelativePos = list2StartIndex - lastMatchPosList2;

                    if (list1RelativePos <= list2RelativePos)
                    {
                        Array.Fill<byte>(list1CheckedLines, 1, list1StartIndex, currentList1Range.length);
                    }
                    else
                    {
                        Array.Fill<byte>(list2CheckedLines, 1, list2StartIndex, currentList2Range.length);
                    }
                }

                //Update the two range variables
                FindFirstRangeFromArray(ref currentList1Range, list1CheckedLines);
                FindFirstRangeFromArray(ref currentList2Range, list2CheckedLines);

                if (currentList1Range.rangeStart == -1 || currentList2Range.rangeStart == -1)
                {
                    finished = true;
                }
            }
        }

        void FindFirstRangeFromArray(ref DiffRange range, byte[] checkedList)
        {
            int rangeStart = -1;
            int rangeEnd = -1;

            int startIndex = range.rangeStart;

            //Look for the next empty range from the start for each list
            for (int i = 0; i < checkedList.Length; i++)
            {
                if (checkedList[i] == 0)
                {
                    rangeStart = i;

                    bool foundEndIndex = false;

                    for (int j = i; j <= checkedList.Length - 1; j++)
                    {
                        //If we reach the end, set the end index to the length
                        if (j == checkedList.Length - 1)
                        {
                            rangeEnd = checkedList.Length;
                            foundEndIndex = true;
                            break;
                        }

                        if (checkedList[j + 1] == 1)
                        {
                            rangeEnd = j + 1;
                            foundEndIndex = true;
                            break;
                        }
                    }

                    if (foundEndIndex) break;
                }
            }

            range.rangeStart = rangeStart;
            range.rangeEnd = rangeEnd;
            range.length = range.rangeEnd - range.rangeStart;
        }


        SymbolMatch FindLongestMatch(int startIndex1, int startIndex2, int endIndex1, int endIndex2)
        {
            int maxMatchLength = 0;
            int bestMatchStartIndex1 = 0, bestMatchStartIndex2 = 0;


            for (int i = startIndex1; i < endIndex1; i++)
            {
                //Try from every starting point
                for (int j = startIndex2; j < endIndex2; j++)
                {
                    int matchLength = 0;
                    int offset = 0;

                    //Keep going until a mismatch is found (name and size don't match)
                    while (CheckIfSymbolsMatch(symbolList1[i + offset], symbolList2[j + offset]))
                    {
                        matchLength++;
                        offset++;

                        if (i + offset >= endIndex1 || j + offset >= endIndex2) break;
                    }

                    if (matchLength > maxMatchLength)
                    {
                        maxMatchLength = matchLength;
                        bestMatchStartIndex1 = i;
                        bestMatchStartIndex2 = j;
                    }

                    if (matchLength > 0)
                    {
                        //Skip past matched range
                        j += offset - 1;
                    }
                }
            }

            return new SymbolMatch(bestMatchStartIndex1, bestMatchStartIndex2, maxMatchLength);
        }

    }
}
