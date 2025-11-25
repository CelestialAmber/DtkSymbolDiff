using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

namespace DtkSymbolDiff
{

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
        public SymbolFile file1;
        public SymbolFile file2;
        public List<SymbolMatch> matches;
        public bool filesLoaded = false;
        public StreamWriter sw;

        bool allowSizeLeniency = false; //Allows symbols to still match as long at their sizes are relatively close
        const float sizeThreshold = 0.95f;
        

        public SymbolFileDiffer(string file1Path, string file2Path)
        {
            matches = new List<SymbolMatch>();

            file1 = new SymbolFile(file1Path);
            file2 = new SymbolFile(file2Path);

            if (file1.loaded && file2.loaded)
            {
                filesLoaded = true;
            }
        }


        public void PrintMatches()
        {

            sw.WriteLine("List 1 file: {0}", file1.name);
            sw.WriteLine("List 2 file: {0}", file2.name);
            sw.WriteLine();

            int numMatches = matches.Count;

            //Print the matches
            for (int i = 0; i < numMatches; i++)
            {
                SymbolMatch match = matches[i];

                sw.WriteLine(string.Format("Lines {0}/{1} match ({2} total symbol(s))",
                    CreateRangeString(match.list1StartIndex + 1, match.list1EndIndex), CreateRangeString(match.list2StartIndex + 1, match.list2EndIndex),
                    match.length));

                Symbol list1FirstSymbol = file1.symbols[match.list1StartIndex];
                Symbol list1LastSymbol = file1.symbols[match.list1EndIndex - 1];
                Symbol list2FirstSymbol = file2.symbols[match.list2StartIndex];
                Symbol list2LastSymbol = file2.symbols[match.list2EndIndex - 1];

                if (match.length > 1)
                {
                    sw.WriteLine("List 1: {0} ... {1}", list1FirstSymbol.ToString(), list1LastSymbol.ToString());
                    sw.WriteLine("List 2: {0} ... {1}", list2FirstSymbol.ToString(), list2LastSymbol.ToString());
                }
                else
                {
                    sw.WriteLine("List 1: {0}", list1FirstSymbol.ToString());
                    sw.WriteLine("List 2: {0}", list2FirstSymbol.ToString());
                }
                sw.WriteLine();

                //Print nonmatched symbols inbetween

                int mismatchEndIndex1 = 0, mismatchEndIndex2 = 0;

                /* If this is the last match, use the lengths of each respective list as the end index. Otherwise, use the start
                index of the next match. */
                if (i == matches.Count - 1)
                {
                    mismatchEndIndex1 = file1.symbols.Count;
                    mismatchEndIndex2 = file2.symbols.Count;

                }
                else
                {
                    SymbolMatch nextMatch = matches[i + 1];
                    mismatchEndIndex1 = nextMatch.list1StartIndex;
                    mismatchEndIndex2 = nextMatch.list2StartIndex;
                }

                int mismatchStartIndex1 = match.list1EndIndex;
                int mismatchStartIndex2 = match.list2EndIndex;
                int mismatchLength1 = mismatchEndIndex1 - mismatchStartIndex1;
                int mismatchLength2 = mismatchEndIndex2 - mismatchStartIndex2;

                bool list1Mismatch = mismatchStartIndex1 < mismatchEndIndex1;
                bool list2Mismatch = mismatchStartIndex2 < mismatchEndIndex2;

                if (list1Mismatch || list2Mismatch) {
                    int list1StartLine = mismatchStartIndex1 + 1;
                    int list2StartLine = mismatchStartIndex2 + 1;

                    sw.WriteLine("Mismatch at lines {0}/{1}",
                        CreateRangeString(list1StartLine, mismatchEndIndex1), CreateRangeString(list2StartLine, mismatchEndIndex2));

                    if (list1Mismatch)
                    {
                        PrintMismatchGroup(mismatchStartIndex1, mismatchEndIndex1, 0);
                    }

                    if (list2Mismatch)
                    {
                        PrintMismatchGroup(mismatchStartIndex2, mismatchEndIndex2, 1);
                    }
                }
            }

            sw.Flush();
        }

        string CreateRangeString(int start, int end)
        {
            int length = end - start;
            return length > 1 ? string.Format("{0}-{1}", start, end) : string.Format("{0}", start);
        }

        void PrintMismatchGroup(int startIndex, int endIndex, int whichList)
        {
            for (int i = startIndex; i < endIndex; i++)
            {
                List<Symbol> list = whichList == 0 ? file1.symbols : file2.symbols;
                sw.WriteLine((whichList == 0 ? "-" : "+") + list[i].ToString());
            }
            sw.WriteLine();
        }

        public void DiffFiles()
        {
            int list1Length = file1.totalSymbols;
            int list2Length = file2.totalSymbols;
            byte[] list1CheckedLines = new byte[list1Length];
            byte[] list2CheckedLines = new byte[list2Length];
            DiffRange currentList1Range = new DiffRange(0, list1Length);
            DiffRange currentList2Range = new DiffRange(0, list2Length);

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

                if (match.length > 0 && !invalidMatch)
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
                    /* If no match was found, decide which range to rule out first based on distance
                    from the last match, then on size. If both are equal, remove both. */

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
                    int length1 = currentList1Range.length;
                    int length2 = currentList2Range.length;

                    bool removeList1Range = false;
                    bool removeList2Range = false;

                    if (list1RelativePos < list2RelativePos) removeList1Range = true;
                    else if (list1RelativePos > list2RelativePos) removeList2Range = true;
                    else
                    {
                        //In the case of a tie, choose the smaller one. If both are equal, remove both
                        if (length1 < length2) removeList1Range = true;
                        else if (length1 > length2) removeList2Range = true;
                        else
                        {
                            removeList1Range = true;
                            removeList2Range = true;
                        }
                    }

                    if (removeList1Range) Array.Fill<byte>(list1CheckedLines, 1, list1StartIndex, length1);
                    if (removeList2Range) Array.Fill<byte>(list2CheckedLines, 1, list2StartIndex, length2);
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

        //Checks if a symbol matches first based on name, then on size.
        public bool CheckIfSymbolsMatch(Symbol s1, Symbol s2)
        {
            if (s1.name == s2.name || s1.size == s2.size) return true;

            //If leniency is enabled, and the sizes are close enough, count as a match
            if (allowSizeLeniency && s1.CalculateSizeSimilarity(s2) > sizeThreshold)
            {
                return true;
            }

            //Otherwise, the symbols don't match
            return false;
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
                    while (CheckIfSymbolsMatch(file1.symbols[i + offset], file2.symbols[j + offset]))
                    {
                        matchLength++;
                        offset++;

                        if (i + offset >= endIndex1 || j + offset >= endIndex2) break;
                    }

                    if (matchLength > 0)
                    {
                        //Determine if the current match is better than the current best
                        
                        bool isBetterMatch = false;

                        if (matchLength > maxMatchLength) isBetterMatch = true;
                        else if(matchLength == maxMatchLength) {
                             /* If the match length is equal to the current best, accept it over it if it comes earlier
                             than the current best match */
                             int avgIndex = (i + j) / 2;
                             int bestMatchAvgIndex = (bestMatchStartIndex1 + bestMatchStartIndex2) / 2;

                             if (avgIndex < bestMatchAvgIndex) isBetterMatch = true;
                         }

                         if (isBetterMatch)
                         {
                             maxMatchLength = matchLength;
                             bestMatchStartIndex1 = i;
                             bestMatchStartIndex2 = j;
                         }

                        //Skip past matched range
                        //j += offset - 1;
                    }
                }
            }

            return new SymbolMatch(bestMatchStartIndex1, bestMatchStartIndex2, maxMatchLength);
        }

    }
}
