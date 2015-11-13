using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace Fwr.DataTeamUploader.HttpRequestProcessing
{
    internal sealed class HttpMultipartContentTemplateStreamParser
    {
        private StreamIndexer _data;
        private int _length;
        private int _pos;
        private ArrayList _elements = new ArrayList();
        private int _lineStart = -1;
        private int _lineLength = -1;
        private bool _lastBoundaryFound;
        private byte[] _boundary;
        private string _partName;
        private string _partFilename;
        private string _partContentType;
        private int _partDataStart = -1;
        private int _partDataLength = -1;
        private Encoding _encoding;

        private HttpMultipartContentTemplateStreamParser(StreamIndexer data, long length, byte[] boundary, Encoding encoding)
        {
            this._data = data;
            this._length = (int) length;
            this._boundary = boundary;
            this._encoding = encoding;
        }

        private bool AtEndOfData()
        {
            return this._pos >= this._length || this._lastBoundaryFound;
        }

        private bool GetNextLine()
        {
            int i = this._pos;
            this._lineStart = -1;
            while (i < this._length)
            {
                if (this._data[i] == 10)
                {
                    this._lineStart = this._pos;
                    this._lineLength = i - this._pos;
                    this._pos = i + 1;
                    if (this._lineLength > 0 && this._data[i - 1] == 13)
                    {
                        this._lineLength--;
                        break;
                    }
                    break;
                }
                else
                {
                    if (++i == this._length)
                    {
                        this._lineStart = this._pos;
                        this._lineLength = i - this._pos;
                        this._pos = this._length;
                    }
                }
            }
            return this._lineStart >= 0;
        }

        private string ExtractValueFromContentDispositionHeader(string l, int pos, string name)
        {
            string text = name + "=\"";
            int num = CultureInfo.InvariantCulture.CompareInfo.IndexOf(l, text, pos, CompareOptions.IgnoreCase);
            if (num < 0)
            {
                return null;
            }
            num += text.Length;
            int num2 = l.IndexOf('"', num);
            if (num2 < 0)
            {
                return null;
            }
            if (num2 == num)
            {
                return string.Empty;
            }
            return l.Substring(num, num2 - num);
        }

        private void ParsePartHeaders()
        {
            this._partName = null;
            this._partFilename = null;
            this._partContentType = null;
            while (this.GetNextLine())
            {
                if (this._lineLength == 0)
                {
                    return;
                }
                byte[] array = new byte[this._lineLength];
                this._data.CopyBytes(this._data, this._lineStart, array, 0, this._lineLength); // Edited to remove CopyBytes dependancy
                string @string = this._encoding.GetString(array);
                int num = @string.IndexOf(':');
                if (num >= 0)
                {
                    string s = @string.Substring(0, num);
                    if (s.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase)) // Edited to remove StringUtil dependancy
                    {
                        this._partName = this.ExtractValueFromContentDispositionHeader(@string, num + 1, "name");
                        this._partFilename = this.ExtractValueFromContentDispositionHeader(@string, num + 1, "filename");
                    }
                    else
                    {
                        if (s.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) // Edited to remove StringUtil dependancy
                        {
                            this._partContentType = @string.Substring(num + 1).Trim();
                        }
                    }
                }
            }
        }

        private bool AtBoundaryLine()
        {
            int num = this._boundary.Length;
            if (this._lineLength != num && this._lineLength != num + 2)
            {
                return false;
            }
            for (int i = 0; i < num; i++)
            {
                if (this._data[this._lineStart + i] != this._boundary[i])
                {
                    return false;
                }
            }
            if (this._lineLength == num)
            {
                return true;
            }
            if (this._data[this._lineStart + num] != 45 || this._data[this._lineStart + num + 1] != 45)
            {
                return false;
            }
            this._lastBoundaryFound = true;
            return true;
        }

        private void ParsePartData()
        {
            this._partDataStart = this._pos;
            this._partDataLength = -1;
            while (this.GetNextLine())
            {
                if (this.AtBoundaryLine())
                {
                    int num = this._lineStart - 1;

                    // Carriage return check

                    if (this._data[num] == 10)
                    {
                        num--;
                    }
                    if (this._data[num] == 13)
                    {
                        num--;
                    }
                    this._partDataLength = num - this._partDataStart + 1;
                    return;
                }
            }
        }

        private void ParseIntoElementList()
        {
            while (this.GetNextLine() && !this.AtBoundaryLine())
            {
            }
            if (this.AtEndOfData())
            {
                return;
            }
            while (true)
            {
                this.ParsePartHeaders();

                if (this.AtEndOfData())
                {
                    break;
                }
                this.ParsePartData();
                if (this._partDataLength == -1)
                {
                    return;
                }
                if (this._partName != null)
                {
                    this._elements.Add(new MultipartContentElement(this._partName, this._partFilename, this._partContentType, this._data, this._partDataStart, this._partDataLength));
                }
                if (this.AtEndOfData())
                {
                    return;
                }
            }
        }

        internal static MultipartContentElement[] Parse(StreamIndexer data, long length, byte[] boundary, Encoding encoding) // Edited to remove HttpRawUploadedContent dependancy
        {
            var httpMultipartContentTemplateParser = new HttpMultipartContentTemplateStreamParser(data, length, boundary, encoding);
            httpMultipartContentTemplateParser.ParseIntoElementList();
            return (MultipartContentElement[])httpMultipartContentTemplateParser._elements.ToArray(typeof(MultipartContentElement));
        }

        internal static byte[] GetMultipartBoundary(string contentType)
        {
            string text = GetAttributeFromHeader(contentType, "boundary");
            if (text == null)
            {
                return null;
            }
            text = "--" + text;
            return Encoding.ASCII.GetBytes(text.ToCharArray());
        }

        private static string GetAttributeFromHeader(string headerValue, string attrName)
        {
            if (headerValue == null)
            {
                return null;
            }
            int length = headerValue.Length;
            int length2 = attrName.Length;
            int i;
            for (i = 1; i < length; i += length2)
            {
                i = CultureInfo.InvariantCulture.CompareInfo.IndexOf(headerValue, attrName, i, CompareOptions.IgnoreCase);
                if (i < 0 || i + length2 >= length)
                {
                    break;
                }
                char c = headerValue[i - 1];
                char c2 = headerValue[i + length2];
                if ((c == ';' || c == ',' || char.IsWhiteSpace(c)) && (c2 == '=' || char.IsWhiteSpace(c2)))
                {
                    break;
                }
            }
            if (i < 0 || i >= length)
            {
                return null;
            }
            i += length2;
            while (i < length && char.IsWhiteSpace(headerValue[i]))
            {
                i++;
            }
            if (i >= length || headerValue[i] != '=')
            {
                return null;
            }
            i++;
            while (i < length && char.IsWhiteSpace(headerValue[i]))
            {
                i++;
            }
            if (i >= length)
            {
                return null;
            }
            string result;
            if (i < length && headerValue[i] == '"')
            {
                if (i == length - 1)
                {
                    return null;
                }
                int num = headerValue.IndexOf('"', i + 1);
                if (num < 0 || num == i + 1)
                {
                    return null;
                }
                result = headerValue.Substring(i + 1, num - i - 1).Trim();
            }
            else
            {
                int num = i;
                while (num < length && headerValue[num] != ' ' && headerValue[num] != ',')
                {
                    num++;
                }
                if (num == i)
                {
                    return null;
                }
                result = headerValue.Substring(i, num - i).Trim();
            }
            return result;
        }
    }
}