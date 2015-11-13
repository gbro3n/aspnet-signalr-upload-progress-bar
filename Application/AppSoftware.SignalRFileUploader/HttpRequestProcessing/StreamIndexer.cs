using System.IO;

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

                if(!(index >= m_chunkStartPos && index <= m_chunkEndPos) || m_chunk == null)
                {
                    int chunkSize = 8 * 1024;

                    long distanceToStreamEnd = (m_stream.Length - index);

                    if (chunkSize > distanceToStreamEnd)
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