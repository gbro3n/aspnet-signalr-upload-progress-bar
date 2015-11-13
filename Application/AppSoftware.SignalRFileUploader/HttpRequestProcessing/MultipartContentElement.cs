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

                while (bytesRemaining > 0)
                {
                    length = m_streamIndexer.Stream.Read(buffer, 0, readSize);

                    fs.Write(buffer, 0, length);

                    bytesRemaining -= length;

                     if (readSize > bytesRemaining)
                     {
                         readSize = bytesRemaining;
                     }
                }
            }
        }

        internal string GetAsString(Encoding encoding)
        {
            if (this.m_length > 0)
            {
                return encoding.GetString(this.GetAsBytes(encoding)); // Edited to remove HttpRawUploadedContent dependancy, use plain byte[]
            }
            return string.Empty;
        }
    }
}