
// This was ported from Microsoft.VisualBasic.FileIO.TextFieldParser
// This was ported because installs of Mono and .NET core lack the Microsoft.VisualBasic library
// and so no TextFieldParser and throws a typeload exception

//What's a nice class like TextFieldParser doing in a namespace like Microsoft.VisualBasic
//http://geekswithblogs.net/brians/archive/2010/07/07/whats-a-nice-class-like-textfieldparser-doing-in-a-namespace.aspx

// TextFieldParser.vb
// 
// Authors:
// Rolf Bjarne Kvinge (RKvinge@novell.com>
// 
// Copyright (C) 2007 Novell (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// 
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using System.ComponentModel;

namespace NetShell
{
    public enum FieldType
    {
        Delimited = 0,
        FixedWidth = 1
    }

    public class TextFieldParser : IDisposable
    {
        private TextReader m_Reader;
        private bool m_LeaveOpen = false;
        private int[] m_FieldWidths = null;

        private Queue<string> m_PeekedLine = new Queue<string>();
        private int m_MinFieldLength;

        public TextFieldParser(Stream stream)
        {
            m_Reader = new StreamReader(stream);
        }

        public TextFieldParser(TextReader reader)
        {
            m_Reader = reader;
        }

        public TextFieldParser(string path)
        {
            m_Reader = new StreamReader(path);
        }

        public TextFieldParser(Stream stream, Encoding defaultEncoding)
        {
            m_Reader = new StreamReader(stream, defaultEncoding);
        }

        public TextFieldParser(string path, Encoding defaultEncoding)
        {
            m_Reader = new StreamReader(path, defaultEncoding);
        }

        public TextFieldParser(Stream stream, Encoding defaultEncoding, bool detectEncoding)
        {
            m_Reader = new StreamReader(stream, defaultEncoding, detectEncoding);
        }

        public TextFieldParser(string path, Encoding defaultEncoding, bool detectEncoding)
        {
            m_Reader = new StreamReader(path, defaultEncoding, detectEncoding);
        }

        public TextFieldParser(Stream stream, Encoding defaultEncoding, bool detectEncoding, bool leaveOpen)
        {
            m_Reader = new StreamReader(stream, defaultEncoding, detectEncoding);
            m_LeaveOpen = leaveOpen;
        }

        private string[] GetDelimitedFields()
        {
            if (Delimiters == null || Delimiters.Length == 0)
                throw new InvalidOperationException("Unable to read delimited fields because Delimiters is Nothing or empty.");

            List<string> result = new List<string>();
            string line;
            int currentIndex = 0, nextIndex = 0;

            line = GetNextLine();

            if (line == null)
                return null;

            while (!(nextIndex >= line.Length))
            {
                result.Add(GetNextField(line, currentIndex, ref nextIndex));
                currentIndex = nextIndex;
            }

            return result.ToArray();
        }

        private string GetNextField(string line, int startIndex, ref int nextIndex)
        {
            bool inQuote = false;
            int currentindex = startIndex;

            if (nextIndex == int.MinValue)
            {
                nextIndex = int.MaxValue;
                return string.Empty;
            }

            if (HasFieldsEnclosedInQuotes && line[currentindex] == '"')
            {
                inQuote = true;
                startIndex += 1;
            }

            bool mustMatch = false;

            for (int j = startIndex; j <= line.Length - 1; j++)
            {
                if (inQuote)
                {
                    if (line[j] == '"')
                    {
                        //if the quotes are escaped using two quotes ""
                        //skip ahead
                        if (j + 1 < line.Length && line[j + 1] == '"')
                        {
                            j += 1;
                            continue;
                        }

                        inQuote = false;
                        mustMatch = true;
                    }
                    continue;
                }

                for (int i = 0; i <= Delimiters.Length - 1; i++)
                {
                    if (string.Compare(line, j, Delimiters[i], 0, Delimiters[i].Length) == 0)
                    {
                        nextIndex = j + Delimiters[i].Length;

                        if (nextIndex == line.Length)
                            nextIndex = int.MinValue;

                        if (mustMatch)
                            return line.Substring(startIndex, j - startIndex - 1);
                        else
                            return line.Substring(startIndex, j - startIndex);
                    }
                }

                if (mustMatch)
                    RaiseDelimiterEx(line);
            }

            if (inQuote)
                RaiseDelimiterEx(line);

            nextIndex = line.Length;
            if (mustMatch)
                return line.Substring(startIndex, nextIndex - startIndex - 1);
            else
                return line.Substring(startIndex);
        }

        private void RaiseDelimiterEx(string Line)
        {
            ErrorLineNumber = LineNumber;
            ErrorLine = Line;
            throw new MalformedLineException("Line " + ErrorLineNumber.ToString() + " cannot be parsed using the current Delimiters.", ErrorLineNumber);
        }

        private void RaiseFieldWidthEx(string Line)
        {
            ErrorLineNumber = LineNumber;
            ErrorLine = Line;
            throw new MalformedLineException("Line " + ErrorLineNumber.ToString() + " cannot be parsed using the current FieldWidths.", ErrorLineNumber);
        }

        private string[] GetWidthFields()
        {
            if (m_FieldWidths == null || m_FieldWidths.Length == 0)
                throw new InvalidOperationException("Unable to read fixed width fields because FieldWidths is Nothing or empty.");

            string[] result = new string[m_FieldWidths.Length - 1 + 1];
            int currentIndex = 0;
            string line;

            line = GetNextLine();

            if (line.Length < m_MinFieldLength)
                RaiseFieldWidthEx(line);

            for (int i = 0; i <= result.Length - 1; i++)
            {
                if (TrimWhiteSpace)
                    result[i] = line.Substring(currentIndex, m_FieldWidths[i]).Trim();
                else
                    result[i] = line.Substring(currentIndex, m_FieldWidths[i]);
                currentIndex += m_FieldWidths[i];
            }

            return result;
        }

        private bool IsCommentLine(string Line)
        {
            if (CommentTokens == null)
                return false;

            foreach (string str in CommentTokens)
            {
                if (Line.StartsWith(str))
                    return true;
            }

            return false;
        }

        private string GetNextRealLine()
        {
            string nextLine;

            do
            {
                if ((nextLine = ReadLine()) == null)
                    break;
            }
            while (IsCommentLine(nextLine));

            return nextLine;
        }

        private string GetNextLine()
        {
            if (m_PeekedLine.Count > 0)
                return m_PeekedLine.Dequeue();
            else
                return GetNextRealLine();
        }

        public void Close()
        {
            if (m_Reader != null && m_LeaveOpen == false)
                m_Reader.Close();
            m_Reader = null;
        }


        public string PeekChars(int numberOfChars)
        {
            if (numberOfChars < 1)
                throw new ArgumentException("numberOfChars has to be a positive, non-zero number", "numberOfChars");

            string[] peekedLines;
            string theLine = null;
            if (m_PeekedLine.Count > 0)
            {
                peekedLines = m_PeekedLine.ToArray();
                for (int i = 0; i <= m_PeekedLine.Count - 1; i++)
                {
                    if (IsCommentLine(peekedLines[i]) == false)
                    {
                        theLine = peekedLines[i];
                        break;
                    }
                }
            }

            if (theLine == null)
            {
                do
                {
                    theLine = m_Reader.ReadLine();
                    m_PeekedLine.Enqueue(theLine);
                }
                while (theLine == null || IsCommentLine(theLine));
            }

            if (theLine != null)
            {
                if (theLine.Length <= numberOfChars)
                    return theLine;
                else
                    return theLine.Substring(0, numberOfChars);
            }
            else
                return null;
        }

        public string[] ReadFields()
        {
            switch (TextFieldType)
            {
                case FieldType.Delimited:
                    {
                        return GetDelimitedFields();
                    }

                case FieldType.FixedWidth:
                    {
                        return GetWidthFields();
                    }

                default:
                    {
                        return GetDelimitedFields();
                    }
            }
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public string ReadLine()
        {
            if (m_PeekedLine.Count > 0)
                return m_PeekedLine.Dequeue();

            if (LineNumber == -1)
                LineNumber = 1;
            else
                LineNumber += 1;
            return m_Reader.ReadLine();
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public string ReadToEnd()
        {
            return m_Reader.ReadToEnd();
        }

        public void SetDelimiters(params string[] delimiters)
        {
            this.Delimiters = delimiters;
        }

        public void SetFieldWidths(params int[] fieldWidths)
        {
            this.FieldWidths = fieldWidths;
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public string[] CommentTokens { get; set; } = new string[] { };

        public string[] Delimiters { get; set; } = null;

        public bool EndOfData => PeekChars(1) == null;

        public string ErrorLine { get; private set; } = string.Empty;

        public long ErrorLineNumber { get; private set; } = -1;

        public int[] FieldWidths
        {
            get
            {
                return m_FieldWidths;
            }
            set
            {
                m_FieldWidths = value;
                if (m_FieldWidths != null)
                {
                    m_MinFieldLength = 0;
                    for (int i = 0; i <= m_FieldWidths.Length - 1; i++)
                        m_MinFieldLength += value[i];
                }
            }
        }

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public bool HasFieldsEnclosedInQuotes { get; set; } = true;

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public long LineNumber { get; private set; } = -1;

        public FieldType TextFieldType { get; set; } = FieldType.Delimited;

        public bool TrimWhiteSpace { get; set; } = true;

        private bool disposedValue = false;        // To detect redundant calls

        // IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
                Close();
            this.disposedValue = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~TextFieldParser()
        {
            Dispose(false);
        }
    }
}
