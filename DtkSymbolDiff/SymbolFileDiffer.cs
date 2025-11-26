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

    struct SectionDiffResults
    {
        public Section section1;
        public Section section2;
        public string sectionName;
        public List<SymbolMatch> matches;
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
        List<SectionDiffResults> diffResultsList = new List<SectionDiffResults>();
        public bool filesLoaded = false;
        public StreamWriter sw;
        Options options;

        //Threshold for symbol sizes
        const float sizeThreshold = 0.95f;
        /* Minimum match length needed to allow threshold check to be done. This
        helps prevent random unrelated symbols from matching, while also helping 
        in the case where symbols with different sizes are most likely to be the same. */
        const int matchLengthThreshold = 10;
        

        public SymbolFileDiffer(string file1Path, string file2Path, Options options)
        {
            this.options = options;

            file1 = new SymbolFile(file1Path);
            file2 = new SymbolFile(file2Path);

            if (file1.loaded && file2.loaded)
            {
                filesLoaded = true;
            }
        }

        public void PrintDiff()
        {
            sw.WriteLine("List 1 file: {0}", file1.name);
            sw.WriteLine("List 2 file: {0}", file2.name);
            sw.WriteLine();

            //Print the missing sections for each file.
            PrintMissingSections();

            foreach (SectionDiffResults results in diffResultsList)
            {
                PrintSectionMatches(results);
            }

            sw.Flush();
        }

        void PrintMissingSections()
        {
            foreach (Section section in file2.sections)
            {
                if (!file1.ContainsSection(section.name))
                {
                    sw.WriteLine("File 1 is missing section {0}", section.name);
                }
            }

            sw.WriteLine();

            foreach (Section section in file1.sections)
            {
                if (!file2.ContainsSection(section.name))
                {
                    sw.WriteLine("File 2 is missing section {0}", section.name);
                }
            }

            sw.WriteLine();
        }

        void PrintSectionMatches(SectionDiffResults results)
        {
            List<Symbol> symbols1 = results.section1.symbols;
            List<Symbol> symbols2 = results.section2.symbols;
            List<SymbolMatch> matches = results.matches;

            int numMatches = matches.Count;

            sw.WriteLine("Section: {0}", results.sectionName);

            //Print the matches
            for (int i = 0; i < numMatches; i++)
            {
                SymbolMatch match = matches[i];


                /* If this is the first match, print the mismatched symbols before the match. The bounds checks
                in the function prevent anything from being printed if there are no mismatches before (both indices are 0), so
                there's no need to check here. */
                if (i == 0)
                {
                    PrintMismatches(symbols1, symbols2, 0, match.list1StartIndex, 0, match.list2StartIndex);
                }

                Symbol list1FirstSymbol = symbols1[match.list1StartIndex];
                Symbol list1LastSymbol = symbols1[match.list1EndIndex - 1];
                Symbol list2FirstSymbol = symbols2[match.list2StartIndex];
                Symbol list2LastSymbol = symbols2[match.list2EndIndex - 1];

                sw.WriteLine(string.Format("Symbols {0}/{1} match ({2} total symbol(s))",
                CreateRangeString(match.list1StartIndex, match.list1EndIndex), CreateRangeString(match.list2StartIndex, match.list2EndIndex),
                match.length));

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

                //Print nonmatched symbols after

                int mismatchStartIndex1 = match.list1EndIndex;
                int mismatchStartIndex2 = match.list2EndIndex;
                int mismatchEndIndex1 = i == matches.Count - 1 ? symbols1.Count : matches[i + 1].list1StartIndex;
                int mismatchEndIndex2 = i == matches.Count - 1 ? symbols2.Count : matches[i + 1].list2StartIndex;

                PrintMismatches(symbols1, symbols2, mismatchStartIndex1, mismatchEndIndex1, mismatchStartIndex2, mismatchEndIndex2);
            }

            if (options.printDifferentSizeSymbols)
            {
                sw.WriteLine("Matched symbols with different sizes:");
                for (int i = 0; i < numMatches; i++)
                {
                    SymbolMatch match = matches[i];

                    for (int j = 0; j < match.length; j++)
                    {
                        Symbol symbol1 = symbols1[j + match.list1StartIndex];
                        Symbol symbol2 = symbols2[j + match.list2StartIndex];

                        if (symbol1.size != symbol2.size)
                        {
                            sw.WriteLine("{0} (0x{1:X}/0x{2:X})", symbol1.name, symbol1.size, symbol2.size);
                        }
                    }
                }
            }
        }

        void PrintMismatches(List<Symbol> symbols1, List<Symbol> symbols2, int startIndex1, int endIndex1, int startIndex2, int endIndex2)
        {

            bool list1Mismatch = startIndex1 < endIndex1;
            bool list2Mismatch = startIndex2 < endIndex2;

            if (list1Mismatch || list2Mismatch)
            {

                string rangeString1 = CreateRangeString(startIndex1, endIndex1 - 1);
                string rangeString2 = CreateRangeString(startIndex2, endIndex2 - 1);

                //Change the print message depending on if both or only one list has symbols left
                if (list1Mismatch && list2Mismatch)
                {
                    sw.WriteLine("Mismatch at symbols {0}/{1}", rangeString1, rangeString2);
                }
                else
                {
                    int fileNumber = list1Mismatch ? 1 : 2;
                    sw.WriteLine("Mismatch at symbols {0} (file {1})", list1Mismatch ? rangeString1 : rangeString2, fileNumber);
                }

                if (list1Mismatch)
                {
                    for (int j = startIndex1; j < endIndex1; j++)
                    {
                        sw.WriteLine("-" + symbols1[j].ToString());
                    }
                    sw.WriteLine();
                }

                if (list2Mismatch)
                {
                    for (int j = startIndex2; j < endIndex2; j++)
                    {
                        sw.WriteLine("+" + symbols2[j].ToString());
                    }
                    sw.WriteLine();
                }
            }
        }

        string CreateRangeString(int start, int end)
        {
            int length = end - start;
            return length > 1 ? string.Format("{0}-{1}", start, end) : string.Format("{0}", start);
        }

        public void DiffFiles()
        {
            for (int i = 0; i < file1.sections.Count; i++)
            {
                Section section1 = file1.sections[i];
                //Find the matching section in the other file. If it doesn't exist, skip diffing the section.
                Section section2 = file2.GetSection(section1.name);

                if (section2 != null)
                {
                    //If this section is a data section, and the include data symbols option is off, skip it
                    if (!options.includeDataSymbols && !section1.IsCodeSection())
                    {
                        continue;
                    }

                    Console.WriteLine("Diffing section " + section1.name + ":");
                    List<SymbolMatch> matches = DiffSections(section1, section2);
                    SectionDiffResults results = new SectionDiffResults();
                    results.matches = matches;
                    results.section1 = section1;
                    results.section2 = section2;
                    results.sectionName = section1.name;
                    diffResultsList.Add(results);
                }
                else
                {
                    Console.WriteLine("File 2 doesn't have section {0}, skipping", section1.name);
                }
            }
        }

        public List<SymbolMatch> DiffSections(Section section1, Section section2)
        {
            int section1Length = section1.symbols.Count;
            int section2Length = section2.symbols.Count;
            byte[] list1CheckedLines = new byte[section1Length];
            byte[] list2CheckedLines = new byte[section2Length];
            DiffRange currentList1Range = new DiffRange(0, section1Length);
            DiffRange currentList2Range = new DiffRange(0, section2Length);

            //Stupid
            SymbolMatchComparer comparer = new SymbolMatchComparer();

            List<SymbolMatch> matches = new List<SymbolMatch>();

            bool finished = false;

            while (!finished)
            {
                int list1StartIndex = currentList1Range.rangeStart;
                int list1EndIndex = currentList1Range.rangeEnd;
                int list2StartIndex = currentList2Range.rangeStart;
                int list2EndIndex = currentList2Range.rangeEnd;

                //Debug.WriteLine("Current list 1 range: {0}-{1}, current list 2 range: {2}-{3}",
                //list1StartIndex, list1EndIndex - 1, list2StartIndex, list2EndIndex - 1);

                //Find the longest match for the current range
                SymbolMatch match = FindLongestMatch(section1.symbols, section2.symbols, list1StartIndex, list2StartIndex,
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

                    //Debug.WriteLine("Found match (list 1 range: {0}-{1}, list 2 range: {2}-{3})",
                    //match.list1StartIndex, match.list1EndIndex - 1, match.list2StartIndex, match.list2EndIndex - 1);

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

            return matches;
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
            /* Only count the names matching if the name is not an auto name, and the sizes
            if at least of the symbols is auto. */
            if (s1.name == s2.name && !s1.isAuto) return true;
            else if (s1.size == s2.size && (s1.isAuto || s2.isAuto)) return true;

            //Otherwise, the symbols don't match
            return false;
        }

        SymbolMatch FindLongestMatch(List<Symbol> symbols1, List<Symbol> symbols2, int startIndex1, int startIndex2, int endIndex1, int endIndex2)
        {
            int maxMatchLength = 0;
            int bestMatchStartIndex1 = 0, bestMatchStartIndex2 = 0;
            int minSymbolCount = Math.Min(symbols1.Count, symbols2.Count);
            bool foundMaxLengthMatch = false;


            for (int i = startIndex1; i < endIndex1; i++)
            {
                //Try from every starting point
                for (int j = startIndex2; j < endIndex2; j++)
                {
                    int matchLength = 0;
                    int offset = 0;

                    //Keep going until a mismatch is found
                    while (true)
                    {
                        Symbol s1 = symbols1[i + offset];
                        Symbol s2 = symbols2[j + offset];

                        bool matching = CheckIfSymbolsMatch(s1, s2);

                        /* If the symbols don't match, the threshold option is enabled, and the current match sequence is
                        long enough, run the size threshold check */
                        if (!matching && options.useSymbolSizeThreshold && matchLength > matchLengthThreshold)
                        {
                            //Only run the check if at least one symbol is auto
                            if (s1.isAuto || s2.isAuto)
                            {
                                float similarity = s1.CalculateSizeSimilarity(s2);
                                if (similarity > sizeThreshold)
                                {
                                    //The symbol is about the threshold, so count as a match
                                    matching = true;
                                }
                            }
                        }

                        if (!matching) break;

                        matchLength++;
                        offset++;

                        if (i + offset >= endIndex1 || j + offset >= endIndex2) break;
                    }

                    if (matchLength > 0)
                    {
                        //Determine if the current match is better than the current best

                        bool isBetterMatch = false;

                        if (matchLength > maxMatchLength) isBetterMatch = true;
                        else if (matchLength == maxMatchLength)
                        {
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

                            //If the match length is the same as the length of the shorter list, exit early
                            if (maxMatchLength == minSymbolCount)
                            {
                                foundMaxLengthMatch = true;
                                break;
                            }
                        }

                        //Skip past matched range
                        //j += offset - 1;
                    }
                }

                if (foundMaxLengthMatch) break;
            }

            return new SymbolMatch(bestMatchStartIndex1, bestMatchStartIndex2, maxMatchLength);
        }

    }
}
