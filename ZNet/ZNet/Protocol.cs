using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZNet
{
	public class Protocol
	{
		public ProtocolHeader Header = new ProtocolHeader();
		public ProtocolData Data = new ProtocolData();
		private byte[] Buffer = new byte[50 * 1024];
		private int BufferIndex = 0;
		public int Sent = 0;
		public int Dispatched = 0;
        public int IncommingAckInformDelivered = 0;
        public int OutGoingReceiveAcked = 0;

        public byte[] SerializeToBytes()
		{
			StartWritingToBuffer();
			WriteIntToBuffer((int)Header.SendType);
			WriteIntToBuffer(Header.SequenceNumber);
            WriteIntArrayToBuffer(Header.AckList);
            WriteIntArrayToBuffer(Header.DispatchedList);
			WriteStringToBuffer(Data.Data);
			byte[] sendbuffer = new byte[BufferIndex];
			Array.Copy(Buffer, 0, sendbuffer, 0, BufferIndex);

			return sendbuffer;
		}

        private void WriteIntArrayToBuffer(List<int> seqList)
        {
            WriteIntToBuffer(seqList.Count);
            for (int i = 0; i < seqList.Count; ++i)
            {
                WriteIntToBuffer((int)seqList[i]);
            }
        }

        private void StartWritingToBuffer()
		{
			BufferIndex = 0;
		}

		private void WriteIntToBuffer(int data)
		{
			Buffer[BufferIndex++] = (byte)(data);
			Buffer[BufferIndex++] = (byte)(data >> 8);
			Buffer[BufferIndex++] = (byte)(data >> 16);
			Buffer[BufferIndex++] = (byte)(data >> 24);
		}

		private void WriteStringToBuffer(string data)
		{
			int size = 0;
			if (string.IsNullOrEmpty(data))
			{
				WriteIntToBuffer(size);
			}
			else
			{
				size = data.Length;
				WriteIntToBuffer(size);
				byte[] strbytes = Encoding.UTF8.GetBytes(data);
				Array.Copy(strbytes, 0, Buffer, BufferIndex, size);
				BufferIndex += size;
			}
		}

		public void DeserializeFromBytes(byte[] data)
		{
			StartReadingFromBuffer();
			Header.SendType = (ProtocolHeader.MessageSendType)ReadIntFromBuffer(data);
			Header.SequenceNumber = ReadIntFromBuffer(data);
            Header.AckList = ReadIntArrayFromBuffer(data);
            Header.DispatchedList = ReadIntArrayFromBuffer(data);
            Data.Data = ReadStringFromBuffer(data);
		}

        private List<int> ReadIntArrayFromBuffer(byte[] data)
        {
            List<int> res = new List<int>();
            int size = ReadIntFromBuffer(data);
            for (int i = 0; i < size; ++i)
            {
                int element = ReadIntFromBuffer(data);
                res.Add(element);
            }

            return res;
        }

        private void StartReadingFromBuffer()
		{
			BufferIndex = 0;
		}

		private int ReadIntFromBuffer(byte[] data)
		{
			int res = (data[BufferIndex + 3] << 24) | (data[BufferIndex + 2] << 16) | (data[BufferIndex + 1] << 8) | (data[BufferIndex]);
			BufferIndex += 4;
			return res;
		}

		private string ReadStringFromBuffer(byte[] data)
		{
			int size = ReadIntFromBuffer(data);
			byte[] x = new byte[size];
			Array.Copy(data, BufferIndex, x, 0, size);
			string res = System.Text.Encoding.UTF8.GetString(x);
			BufferIndex += size;
			return res;
		}
	}


	public class ProtocolData
	{
		public string Data;
	}

	public class ProtocolHeader
	{
		public enum MessageSendType
		{
			Internal = 1,
			Ping = 2,
			External = 3,
		}

		public MessageSendType SendType;
		public int SequenceNumber = -1;
		public int GlobalSequenceNumber = -1;
        public List<int> AckList = new List<int>();
        public List<int> DispatchedList = new List<int>();
    }
}
