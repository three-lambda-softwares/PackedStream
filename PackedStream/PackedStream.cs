using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TLS
{
    public class PackedStream
    {
        #region Constants

        private const PackedStreamMaxSize PSMS_DEFAULT_MAX_SIZE = PackedStreamMaxSize.LengthMax_4Bytes;
        private const int INT_BUFFER_SIZE = 1024 * 512; // 512k

        #endregion

        #region Declarations

        private readonly SemaphoreSlim _outputStreamLock = new SemaphoreSlim(1, 1);

        private readonly PackedStreamMaxSize _maxSize;
        private readonly long _maxSizeValue;
        private readonly Stream _inputStream;
        private readonly Stream _outputStream;

        private readonly byte[] _dataLength;
        private int _readedDataLength = 0;

        #endregion

        #region Events

        public event EventHandler<PackedStreamDataEventArgs> DataReceived;
        public event EventHandler Disconected;

        #endregion

        #region Constructors

        public PackedStream(Stream stream)
            : this(stream, stream) { }

        public PackedStream(Stream stream, PackedStreamMaxSize maxSize)
            : this(stream, stream, maxSize) { }

        public PackedStream(Stream inputStream, Stream outputStream)
            : this(inputStream, outputStream, PSMS_DEFAULT_MAX_SIZE) { }

        public PackedStream(Stream inputStream, Stream outputStream, PackedStreamMaxSize maxSize)
        {
            _inputStream = inputStream;
            _outputStream = outputStream;
            _maxSize = maxSize;
            _maxSizeValue = 
                _maxSize == PackedStreamMaxSize.LengthMax_1Bytes ? byte.MaxValue :
                _maxSize == PackedStreamMaxSize.LengthMax_2Bytes ? ushort.MaxValue :
                _maxSize == PackedStreamMaxSize.LengthMax_4Bytes ? uint.MaxValue :
                _maxSize == PackedStreamMaxSize.LengthMax_8Bytes ? long.MaxValue :
                throw new NotImplementedException();
            _dataLength = 
                _maxSize == PackedStreamMaxSize.LengthMax_1Bytes ? new byte[1] :
                _maxSize == PackedStreamMaxSize.LengthMax_2Bytes ? new byte[2] :
                _maxSize == PackedStreamMaxSize.LengthMax_4Bytes ? new byte[4] :
                _maxSize == PackedStreamMaxSize.LengthMax_8Bytes ? new byte[8] :
                throw new NotImplementedException();

            RequestPackSize();
        }

        #endregion

        #region Public functions

        public void Write(MemoryStream data)
        {
            // Validate required parameters
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            // Validate the stream length
            if (data.Length > _maxSizeValue)
            {
                throw new OverflowException(string.Format(Texts.errMaxValExceeded, data.Length, _maxSizeValue));
            }

            byte[] dataSize;

            switch (_maxSize)
            {
                case PackedStreamMaxSize.LengthMax_1Bytes:
                    dataSize = new byte[] { (byte)data.Length };
                    break;
                case PackedStreamMaxSize.LengthMax_2Bytes:
                    dataSize = BitConverter.GetBytes((ushort)data.Length);
                    break;
                case PackedStreamMaxSize.LengthMax_4Bytes:
                    dataSize = BitConverter.GetBytes((uint)data.Length);
                    break;
                case PackedStreamMaxSize.LengthMax_8Bytes:
                    dataSize = BitConverter.GetBytes((long)data.Length);
                    break;
                default:
                    throw new NotImplementedException();
            }

            data.Position = 0;
            _outputStreamLock.Wait();
            try
            {
                _outputStream.Write(dataSize, 0, dataSize.Length);
                data.WriteTo(_outputStream);
                _outputStream.Flush();
            }
            finally
            {
                _outputStreamLock.Release();
            }
        }

        public async Task WriteAsync(MemoryStream data)
        {
            // Validate required parameters
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            // Validate the stream length
            if (data.Length > _maxSizeValue)
            {
                throw new OverflowException(string.Format(Texts.errMaxValExceeded, data.Length, _maxSizeValue));
            }

            byte[] dataSize;

            switch (_maxSize)
            {
                case PackedStreamMaxSize.LengthMax_1Bytes:
                    dataSize = new byte[] { (byte)data.Length };
                    break;
                case PackedStreamMaxSize.LengthMax_2Bytes:
                    dataSize = BitConverter.GetBytes((ushort)data.Length);
                    break;
                case PackedStreamMaxSize.LengthMax_4Bytes:
                    dataSize = BitConverter.GetBytes((uint)data.Length);
                    break;
                case PackedStreamMaxSize.LengthMax_8Bytes:
                    dataSize = BitConverter.GetBytes((long)data.Length);
                    break;
                default:
                    throw new NotImplementedException();
            }

            data.Position = 0;

            var buf = new byte[INT_BUFFER_SIZE];
            int readed;

            await _outputStreamLock.WaitAsync();
            try
            {
                await _outputStream.WriteAsync(dataSize, 0, dataSize.Length);

                while (data.Position < data.Length)
                {
                    readed = await data.ReadAsync(buf, 0, INT_BUFFER_SIZE);
                    await _outputStream.WriteAsync(buf, 0, readed);
                }

                await _outputStream.FlushAsync();
            }
            finally
            {
                _outputStreamLock.Release();
            }
        }

        #endregion

        #region Private functions

        private void RequestPackSize()
        {
            _inputStream.BeginRead(_dataLength, _readedDataLength, _dataLength.Length, InputStreamDataReceived, null);
        }

        private void InputStreamDataReceived(IAsyncResult ar)
        {

            try
            {
                var curDataLength = _inputStream.EndRead(ar);
                if (curDataLength == 0)
                {
                    // Input stream closed
                    Disconected?.BeginInvoke(this, EventArgs.Empty, (iar) => { }, null);
                    return;
                }
                _readedDataLength += curDataLength;
            }
            catch (IOException ex) when (ex.HResult == -2146232800)
            {
                // Input stream closed
                Disconected?.BeginInvoke(this, EventArgs.Empty, (iar) => { }, null);
                return;
            }

            if (_readedDataLength == _dataLength.Length)
            {
                var dataSize =
                    _maxSize == PackedStreamMaxSize.LengthMax_1Bytes ? _dataLength[0] :
                    _maxSize == PackedStreamMaxSize.LengthMax_2Bytes ? BitConverter.ToUInt16(_dataLength, 0) :
                    _maxSize == PackedStreamMaxSize.LengthMax_4Bytes ? BitConverter.ToUInt32(_dataLength, 0) :
                    _maxSize == PackedStreamMaxSize.LengthMax_8Bytes ? BitConverter.ToInt64(_dataLength, 0) :
                    throw new NotImplementedException();

                var mms = new MemoryStream();
                var buf = new byte[INT_BUFFER_SIZE];
                int readed;
                var toRead = dataSize;

                while (toRead > 0)
                {
                    readed = _inputStream.Read(buf, 0, toRead < INT_BUFFER_SIZE ? (int)toRead : INT_BUFFER_SIZE);
                    toRead -= readed;
                    mms.Write(buf, 0, readed);
                }

                // Read next pack
                _readedDataLength = 0;
                RequestPackSize();

                // Fire new pack event
                mms.Position = 0;
                DataReceived?.BeginInvoke(this, new PackedStreamDataEventArgs(mms), (iar) =>
                {
                    mms.Dispose();
                }, null);
            }
            else if(_readedDataLength < _dataLength.Length)
            {
                RequestPackSize();
            }
            else
            {
                throw new InvalidDataException();
            }
        }

        #endregion
    }
}
