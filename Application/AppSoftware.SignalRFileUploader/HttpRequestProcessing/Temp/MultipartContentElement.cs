using System;
using System.IO;
using System.Text;

namespace Fwr.DataTeamUploader.HttpRequestProcessing
{
    internal sealed class MultipartContentElement
    {
        private string _name;
        private string _filename;
        private string _contentType;
        private byte[] _data; // Edited to remove HttpRawUploadedContent dependancy
        private int _offset;
        private int _length;

        private StreamIndexer _streamIndexer;

        internal MultipartContentElement(string name, string filename, string contentType, StreamIndexer data, int offset, int length)
        {
            this._name = name;
            this._filename = filename;
            this._contentType = contentType;
            this._offset = offset;
            this._length = length;

            _streamIndexer = data;

            // Idea here is that we don't read the stream in to memory yet, one by 1, that would take ages, instead
            // we take a reference to our stream object, and then wait until we can save from the underlying stream
        }

        internal bool IsFile
        {
            get
            {
                return this._filename != null;
            }
        }

        internal string FileName
        {
            get
            {
                return this._filename;
            }
        }

        internal bool IsFormItem
        {
            get
            {
                return this._filename == null;
            }
        }

        internal string Name
        {
            get
            {
                return this._name;
            }
        }

        internal string ContentType
        {
            get
            {
                return this._contentType;
            }
        }
        
        internal byte[] GetAsBytes(Encoding encoding)
        {
            this._data = new byte[_length];

            _streamIndexer.CopyBytes(_streamIndexer, _offset, this._data, 0, _length);

            return this._data;
        }

        internal void SaveAsFile(string filePath)
        {
            // Stream a new file using positional data and underlying stream

            using (var fs = new FileStream(filePath, FileMode.CreateNew))
            {
                byte[] buffer = new byte[8 * 1024];

                int length;

                int bytesRemaining = _length;

                int readSize = buffer.Length;

                // Start the stream copy to start of the file content

                _streamIndexer.Stream.Position = _offset;

                // Only read as far as we need

                while (bytesRemaining > 0)
                {
                    length = _streamIndexer.Stream.Read(buffer, 0, readSize);

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
            if (this._length > 0)
            {
                return encoding.GetString(this.GetAsBytes(encoding)); // Edited to remove HttpRawUploadedContent dependancy, use plain byte[]
            }
            return string.Empty;
        }
    }
}