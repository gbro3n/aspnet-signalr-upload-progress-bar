using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace AppSoftware.SignalRFileUploader.HttpRequestProcessing
{
    /// <summary>
    /// Adapted from HttpMultipartContentTemplateParser in System.Web, provides processing
    /// for raw http request data
    /// </summary>
    internal sealed class HttpMultipartContentTemplateParser
    {
        private readonly StreamIndexer m_data;
        private readonly int m_length;
        private int m_pos;
        private readonly ArrayList m_elements = new ArrayList();
        private int m_lineStart = -1;
        private int m_lineLength = -1;
        private bool m_lastBoundaryFound;
        private readonly byte[] m_boundary;
        private string m_partName;
        private string m_partFilename;
        private string m_partContentType;
        private int m_partDataStart = -1;
        private int m_partDataLength = -1;
        private readonly Encoding m_encoding;

        private HttpMultipartContentTemplateParser(StreamIndexer data, long length, byte[] boundary, Encoding encoding)
        {
            this.m_data = data;
            this.m_length = (int) length;
            this.m_boundary = boundary;
            this.m_encoding = encoding;
        }

        private bool AtEndOfData()
        {
            return this.m_pos >= this.m_length || this.m_lastBoundaryFound;
        }

        private bool GetNextLine()
        {
            int i = this.m_pos;
            this.m_lineStart = -1;
            while (i < this.m_length)
            {
                if (this.m_data[i] == 10)
                {
                    this.m_lineStart = this.m_pos;
                    this.m_lineLength = i - this.m_pos;
                    this.m_pos = i + 1;
                    if (this.m_lineLength > 0 && this.m_data[i - 1] == 13)
                    {
                        this.m_lineLength--;
                        break;
                    }
                    break;
                }
                else
                {
                    if (++i == this.m_length)
                    {
                        this.m_lineStart = this.m_pos;
                        this.m_lineLength = i - this.m_pos;
                        this.m_pos = this.m_length;
                    }
                }
            }
            return this.m_lineStart >= 0;
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
            this.m_partName = null;
            this.m_partFilename = null;
            this.m_partContentType = null;
            while (this.GetNextLine())
            {
                if (this.m_lineLength == 0)
                {
                    return;
                }
                byte[] array = new byte[this.m_lineLength];
                this.m_data.CopyBytes(this.m_data, this.m_lineStart, array, 0, this.m_lineLength); // Edited to remove CopyBytes dependancy
                string @string = this.m_encoding.GetString(array);
                int num = @string.IndexOf(':');
                if (num >= 0)
                {
                    string s = @string.Substring(0, num);
                    if (s.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase)) // Edited to remove StringUtil dependancy
                    {
                        this.m_partName = this.ExtractValueFromContentDispositionHeader(@string, num + 1, "name");
                        this.m_partFilename = this.ExtractValueFromContentDispositionHeader(@string, num + 1, "filename");
                    }
                    else
                    {
                        if (s.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) // Edited to remove StringUtil dependancy
                        {
                            this.m_partContentType = @string.Substring(num + 1).Trim();
                        }
                    }
                }
            }
        }

        private bool AtBoundaryLine()
        {
            int num = this.m_boundary.Length;
            if (this.m_lineLength != num && this.m_lineLength != num + 2)
            {
                return false;
            }
            for (int i = 0; i < num; i++)
            {
                if (this.m_data[this.m_lineStart + i] != this.m_boundary[i])
                {
                    return false;
                }
            }
            if (this.m_lineLength == num)
            {
                return true;
            }
            if (this.m_data[this.m_lineStart + num] != 45 || this.m_data[this.m_lineStart + num + 1] != 45)
            {
                return false;
            }
            this.m_lastBoundaryFound = true;
            return true;
        }

        private void ParsePartData()
        {
            this.m_partDataStart = this.m_pos;
            this.m_partDataLength = -1;
            while (this.GetNextLine())
            {
                if (this.AtBoundaryLine())
                {
                    int num = this.m_lineStart - 1;

                    // Carriage return check

                    if (this.m_data[num] == 10)
                    {
                        num--;
                    }
                    if (this.m_data[num] == 13)
                    {
                        num--;
                    }
                    this.m_partDataLength = num - this.m_partDataStart + 1;
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
                if (this.m_partDataLength == -1)
                {
                    return;
                }
                if (this.m_partName != null)
                {
                    this.m_elements.Add(new MultipartContentElement(this.m_partName, this.m_partFilename, this.m_partContentType, this.m_data, this.m_partDataStart, this.m_partDataLength));
                }
                if (this.AtEndOfData())
                {
                    return;
                }
            }
        }

        internal static MultipartContentElement[] Parse(StreamIndexer data, long length, byte[] boundary, Encoding encoding) // Edited to remove HttpRawUploadedContent dependancy
        {
            var httpMultipartContentTemplateParser = new HttpMultipartContentTemplateParser(data, length, boundary, encoding);
            httpMultipartContentTemplateParser.ParseIntoElementList();
            return (MultipartContentElement[])httpMultipartContentTemplateParser.m_elements.ToArray(typeof(MultipartContentElement));
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