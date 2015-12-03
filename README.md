
# ASP.NET File Uploader with SignalR Progress Bar and Extended Input Stream Processing

*Note: This is a copy of an old and fairly popular blog post that I have had requests to reinstate the links and source code for. The .sln file has been updated for VS 2015. If you run into trouble with 404 error on upload of a file, check the HTTP handler config in web.config - HTTP handler configuration is different for IIS 10 vs older versions.*

![alternate text](https://cdn.iocontent.com/v1.0/assets/p3rzfr3dgzy3eq6fcgbg2ejxqc/20151113-160132588/7v29/signalr-upload-progress-git-hub.png)

This post describes how to build robust file uploading functionality, with progress bar using ASP.NET MVC and SignalR.

Full source code can be downloaded at the end of this post.
I needed to build a file uploader, and have the progress reporting managed by SignalR, as the project that this demo is part of involves long processing on the uploaded file, and requires a persistent, reliable connection to the client.

The challenges that I needed to solve following the initial SignalR integration were:

**Use of HttpContext.Request in MVC action requires full request stream to have been uploaded**

File upload via an ASP.NET MVC action using HttpPostedFileBase was not suitable. Accessing some properties on the HttpContext.Request object, such as HttpContext.Request.Length, HttpContext.Request.Files, HttpContext.Request.InputStream causes ASP.NET to wait until the full HTTP request stream has been read in its entirety. In this case, that meant that I could not start pushing updates to the client from the server until the file had been uploaded, which defeated the point of having an upload progress bar.

I was able to work around this by using `HttpContext.Request.GetBufferlessInputStream()` inside an HTTP handler. This allows me to begin reading from the http request body pretty much as soon as it starts uploading. This however left me with my next problem to solve.

**Because none of the form data can be accessed during upload (this would cause ASP.NET to wait until all uploaded), no additional variables can be used at an early stage in the request**

One such variable that was required was the SignalR client connection id, so that I could call back to the client with upload progress. To solve this problem, the HttpHandler is bound in Web.Config to all requests to .upl extention. Since the the extension of the request path was all that mattered, I was able to send the SignalR connection id as the request path file name e.g. /connection_id_here.upl. I could then parse the connection id out of Request.Url, which does not cause ASP.NET to hang.

I had considered using MVC routing to circumvent the issue, however controller actions require the full request to have been uploaded before running the action method code.

**There is no built in way to process raw HTTP request data**

I was unable to find an existing means of processing the raw multi part content, that `HttpContext.Request.GetBufferlessInputStream()` provides access to. To solve this problem, I decompiled System.Web using [ILSpy](http://ilspy.net) on my local machine to see how ASP.NET splits the request data when populating properties such as the Files HttpFileCollection array on the request object. Here I was able to find the relevant code, most of which is in an internal class called **HttpMultipartContentTemplateParser**. Once I was able to replicate this functionality, and refactor out some of the deeper dependencies, I was able to split the multipart content form data.

Initially, processing of the multi part form data was in memory, so large uploads triggered exceptions.

What was required here was a mechanism for writing the raw input stream data to disk, and then processing the data from a file in a chunked fashion, streaming to individual files without ever holding a full files worth of bytes in memory at any one time.

## HttpMultipartContentTemplateParser

The code that parses the raw multipart content data, boundaries, content disposition headers is as below. This has been adapted from an internal class of the same name in .NET's System.Web.

```
using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace AppSoftware.SignalRFileUploader.HttpRequestProcessing
{
    /// &lt;summary&gt;
    /// Adapted from HttpMultipartContentTemplateParser in System.Web, provides processing
    /// for raw http request data
    /// &lt;/summary&gt;
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
            return this.m_pos &gt;= this.m_length || this.m_lastBoundaryFound;
        }

        private bool GetNextLine()
        {
            int i = this.m_pos;
            this.m_lineStart = -1;
            while (i &lt; this.m_length)
            {
                if (this.m_data[i] == 10)
                {
                    this.m_lineStart = this.m_pos;
                    this.m_lineLength = i - this.m_pos;
                    this.m_pos = i + 1;
                    if (this.m_lineLength &gt; 0 &amp;&amp; this.m_data[i - 1] == 13)
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
            return this.m_lineStart &gt;= 0;
        }

        private string ExtractValueFromContentDispositionHeader(string l, int pos, string name)
        {
            string text = name + "=\"";
            int num = CultureInfo.InvariantCulture.CompareInfo.IndexOf(l, text, pos, CompareOptions.IgnoreCase);
            if (num &lt; 0)
            {
                return null;
            }
            num += text.Length;
            int num2 = l.IndexOf('"', num);
            if (num2 &lt; 0)
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
                if (num &gt;= 0)
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
            if (this.m_lineLength != num &amp;&amp; this.m_lineLength != num + 2)
            {
                return false;
            }
            for (int i = 0; i &lt; num; i++)
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
            while (this.GetNextLine() &amp;&amp; !this.AtBoundaryLine())
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
            for (i = 1; i &lt; length; i += length2)
            {
                i = CultureInfo.InvariantCulture.CompareInfo.IndexOf(headerValue, attrName, i, CompareOptions.IgnoreCase);
                if (i &lt; 0 || i + length2 &gt;= length)
                {
                    break;
                }
                char c = headerValue[i - 1];
                char c2 = headerValue[i + length2];
                if ((c == ';' || c == ',' || char.IsWhiteSpace(c)) &amp;&amp; (c2 == '=' || char.IsWhiteSpace(c2)))
                {
                    break;
                }
            }
            if (i &lt; 0 || i &gt;= length)
            {
                return null;
            }
            i += length2;
            while (i &lt; length &amp;&amp; char.IsWhiteSpace(headerValue[i]))
            {
                i++;
            }
            if (i &gt;= length || headerValue[i] != '=')
            {
                return null;
            }
            i++;
            while (i &lt; length &amp;&amp; char.IsWhiteSpace(headerValue[i]))
            {
                i++;
            }
            if (i &gt;= length)
            {
                return null;
            }
            string result;
            if (i &lt; length &amp;&amp; headerValue[i] == '"')
            {
                if (i == length - 1)
                {
                    return null;
                }
                int num = headerValue.IndexOf('"', i + 1);
                if (num &lt; 0 || num == i + 1)
                {
                    return null;
                }
                result = headerValue.Substring(i + 1, num - i - 1).Trim();
            }
            else
            {
                int num = i;
                while (num &lt; length &amp;&amp; headerValue[num] != ' ' &amp;&amp; headerValue[num] != ',')
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
```

## StreamIndexer

I wrote the StreamIndexer to retain compatibility with the lifted code, providing access by indexer, while reading from the file stream which contains the raw data. The stream indexer provides a chunking capability so that the indexer does not need to refer directly back to the file stream for every read. Blocks of 8KB are read into an in memory array when determined that the Stream position being accessed is not present in the current chunk. Where access is largely sequential, this has proven to be quite efficient.

```
namespace AppSoftware.SignalRFileUploader.HttpRequestProcessing
{
    /// <summary>
    /// StreamIndexer provides chunked access to underlying stream for compatibility with
    /// Chunked access provides efficient access to stream bytes allowing us to read via indexer at 
    /// speed without having to load data into byte[] e.g. by using a FileStream
    /// </summary>
    public class StreamIndexer
    {
        private readonly Stream m_stream;

        private byte[] m_chunk = null;

        private long m_chunkStartPos = 0;
        private long m_chunkEndPos = 0;

        public StreamIndexer(Stream stream)
        {
            m_stream = stream;
        }

        public Stream Stream
        {
            get { return m_stream; }
        }

        public byte this[int index]
        {
            get
            {
                // If the byte at index is already loaded into current chunk, return
                // directly from byte[], else load the chunk from stream first

                if(!(index &gt;= m_chunkStartPos &amp;&amp; index &lt;= m_chunkEndPos) || m_chunk == null)
                {
                    int chunkSize = 8 * 1024;

                    long distanceToStreamEnd = (m_stream.Length - index);

                    if (chunkSize &gt; distanceToStreamEnd)
                    {
                        chunkSize = (int) distanceToStreamEnd;
                    }

                    m_chunkStartPos = index;
                    m_chunkEndPos = (index + chunkSize) - 1;

                    m_chunk = new byte[chunkSize];

                    int readSize = m_chunk.Length;

                    m_stream.Position = index;

                    m_stream.Read(m_chunk, 0, readSize);
                }

                return m_chunk[index - m_chunkStartPos];
            }
        }

        public void CopyBytes(StreamIndexer streamIndexer, int streamIndexerOffset, byte[] destArray, int destArrayOffset, int count)
        {
            m_stream.Position = streamIndexerOffset;

            m_stream.Read(destArray, destArrayOffset, count);
        }
    }
}
```
## MultipartContentElement

This class is also derived from functionality as found in System.Web. Adapted here, it retains a reference to StreamIndexer so that it does not have to hold file bytes in memory, and can access them from StreamIndexer as required.


```
using System.IO;
using System.Text;

namespace AppSoftware.SignalRFileUploader.HttpRequestProcessing
{
    internal sealed class MultipartContentElement
    {
        private readonly string m_name;
        private readonly string m_filename;
        private readonly string m_contentType;
        private byte[] m_data; 
        private readonly int m_offset;
        private readonly int m_length;

        private readonly StreamIndexer m_streamIndexer;

        internal MultipartContentElement(string name, string filename, string contentType, StreamIndexer streamIndexer, int offset, int length)
        {
            this.m_name = name;
            this.m_filename = filename;
            this.m_contentType = contentType;
            this.m_offset = offset;
            this.m_length = length;

            this.m_streamIndexer = streamIndexer;
        }

        internal bool IsFile
        {
            get
            {
                return this.m_filename != null;
            }
        }

        internal string FileName
        {
            get
            {
                return this.m_filename;
            }
        }

        internal bool IsFormItem
        {
            get
            {
                return this.m_filename == null;
            }
        }

        internal string Name
        {
            get
            {
                return this.m_name;
            }
        }

        internal string ContentType
        {
            get
            {
                return this.m_contentType;
            }
        }
        
        internal byte[] GetAsBytes(Encoding encoding)
        {
            this.m_data = new byte[m_length];

            m_streamIndexer.CopyBytes(m_streamIndexer, m_offset, this.m_data, 0, m_length);

            return this.m_data;
        }

        /// <summary>
        /// Reads directly from stream provided by StreamIndexer
        /// so that file can be saved without loading into memory
        /// where an appropriate stream type is used.
        /// </summary>
        /// <param name="filePath"></param>
        internal void SaveAsFile(string filePath)
        {
            // Stream a new file using positional data and underlying stream

            using (var fs = new FileStream(filePath, FileMode.CreateNew))
            {
                byte[] buffer = new byte[8 * 1024];

                int length;

                int bytesRemaining = m_length;

                int readSize = buffer.Length;

                // Start the stream copy to start of the file content

                m_streamIndexer.Stream.Position = m_offset;

                // Only read as far as we need

                while (bytesRemaining &gt; 0)
                {
                    length = m_streamIndexer.Stream.Read(buffer, 0, readSize);

                    fs.Write(buffer, 0, length);

                    bytesRemaining -= length;

                     if (readSize &gt; bytesRemaining)
                     {
                         readSize = bytesRemaining;
                     }
                }
            }
        }

        internal string GetAsString(Encoding encoding)
        {
            if (this.m_length &gt; 0)
            {
                return encoding.GetString(this.GetAsBytes(encoding)); // Edited to remove HttpRawUploadedContent dependancy, use plain byte[]
            }

            return string.Empty;
        }
    }
}
```
