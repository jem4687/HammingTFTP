/*
 *
 * Data Comm Project III
 * Hamming TFTP
 * Jesse martinez (jem4687)
 *
*/

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// This class contains all the methods neede to read a file from a TFTP server
/// </summary>
public class HammingTFTP
{
        //////////////////////////////////////////////////////
	//	Class Variables
	//////////////////////////////////////////////////////

	private const int TFTP_Port = 7000;
	private const int fullBlock = 420;
	private const string octet = "octet";

	private bool error;
	private int dots;
	
	private string host;
	private string requestFile;

	private HashSet<int> parityBits;
	
	private IPAddress hostIP;

	private UdpClient client;

	private IPEndPoint receiveLoc;

	private Dictionary<string, byte> opCodes;

	/// <summary>
	/// Non-Default Constructor
	///
	/// Constructor intitializes all variables with given parameters
	/// </summary>
	///
	/// <param name = "mode"> netascii or octet mode </param>
	/// <param name = "host"> the host name of the TFTP server </param>
	/// <param name = "fileName"> file to retrieve from server </param>
	public HammingTFTP(string mode, string host, string fileName)
	{
		error = mode.Equals("error");
		this.host = host;
		this.requestFile = fileName;
		hostIP = null;
		dots = 0;
		client = new UdpClient();
		client.Client.ReceiveTimeout = 4500;
		receiveLoc = null;
		testHostName();
		fillDictionary();
		fillParityBits();
	}

	/// <summary>
	/// testHostName - attempts to find the IP Address of TFTP server
	/// </summary>
	///
	/// <return> true if IP Adress was found false otherwise </return>
	public void testHostName()
	{
		try
		{
			IPAddress[] addresses = Dns.GetHostEntry(host).AddressList;
			hostIP = addresses[0];
		}
		catch(Exception e)
		{
			e.ToString();
			Console.WriteLine("Host: " + host + " not found");
			hostIP = null;
		}
	}

	/// <summary>
	/// fillDictionary - initializes the opCodes dictionary with the 
	///                  standard TFTP opCodes
	/// </summary>
	public void fillDictionary()
	{
		opCodes = new Dictionary<string, byte>()
		{
			{"readNoError", 1},
			{"readError", 2},
			{"data", 3},
			{"ack", 4},
			{"error", 5},
			{"nack", 6}
		};
	}

	/// <summary>
	/// fillParityBits - fills a hashset of parity indices
	/// </summary>
	public void fillParityBits()
	{
		parityBits = new HashSet<int>();
		parityBits.Add(0);
		parityBits.Add(1);
		parityBits.Add(3);
		parityBits.Add(7);
		parityBits.Add(15);
		parityBits.Add(31);
	}

	/// <summary>
	/// progressBar - prints out a cool progress bar for your current 
	///               active download
	/// </summary>
	private void progressBar()
	{
		int saveCursorSize  = Console.CursorSize;
		Console.CursorSize = 0;
		int left = Console.CursorLeft;
		int bufferWidth = Console.BufferWidth - 5;
		string progressBar = "Your file is downloading(very slowly)";
		dots++;
		if( (dots + progressBar.Length) >= bufferWidth)
			dots = 0;
		int spaces = bufferWidth - (dots + progressBar.Length);
		for(int i = 0; i < dots; i++)
			progressBar += ".";
		for(int i = 0; i < spaces; i++)
			progressBar += " ";
		Console.Write(progressBar);
		Console.CursorLeft = left;
	}


	/// <summary>
	/// retrieveFile - retrieves a file from the TFTP server sending ACK 
	///                after each block is received and resending ACKs
	///		   if necessary, if error occurs stop retreiving blocks
	/// </summary>
	///
	/// <param name = "block"> first block of data retrieved from the 
	///			   server </param>
	public void retrieveFile(byte[] block)
	{
		FileStream fileStream = new FileStream(Directory.GetCurrentDirectory() + "/" + requestFile, FileMode.Create, FileAccess.ReadWrite);
		
		int blockNum = 1;

		byte[] prevBlock = null;

		while(block != null && block.Length >= fullBlock)
		{
			int blockReceived = (256 * (int)block[2]) + (int)block[3];
			if(blockReceived == blockNum && block[1] == opCodes["data"])
			{
				if(prevBlock != null)
					fileStream.Write(prevBlock, 4, prevBlock.Length - 4);
				prevBlock = block;
				block = sendAck(blockNum, false);
				if((int)block[2] == 255 && (int)block[3] == 255)
					blockNum = 0;
				blockNum += 1;
			}
			else
			{
				block = sendAck(blockNum - 1, false);
			}
		};
		
		if(block != null)
		{
			if(prevBlock == null)
				prevBlock = block;
			sendAck(blockNum, true);
			int zeroCount = 0;
			for(int i = prevBlock.Length - 1; i >= 0 ; i--)
				if( prevBlock[i] == 0 )
					zeroCount++;
			int end = prevBlock.Length - 4 - zeroCount + 1;
			if( end < 0 )
				end = prevBlock.Length - 4;
			fileStream.Write(prevBlock, 4, end);
			Console.WriteLine("");
			Console.WriteLine("File: " + requestFile + " successfully downloaded");
		}
		
		fileStream.Close();
	}

	/// <summary>
	/// sendAck - send an ACK packet to the server with the given block
	///	      number
	/// </summary>
	///
	/// <param name = "blockNum"> the block number to ACK </param>
	/// <param name = "finished"> true if ACK is for last block false
	///			      otherwise </param>
	///
	/// <return> a byte array of the next received block from the server
	/// </return>
	public byte[] sendAck(int blockNum, bool finished)
	{
		byte[] packet = new byte[4];
		packet[0] = 0;
		packet[1] = opCodes["ack"];
		
		byte[] intBytes = BitConverter.GetBytes(blockNum);
		packet[2] = intBytes[1];
		packet[3] = intBytes[0];

		return sendReceivePacket(packet, receiveLoc, finished);
	}
	
	/// <summary>
	/// constructNack - constructs a nack packet for the given block number
	/// </summary>
	///
	/// <param name = "blockNum"> the block to nack </param>
	///
	/// <return> the nack packet </return>
	public byte[] constructNack(byte[] blockNum)
	{
		byte[] packet = new byte[4];
		packet[0] = 0;
		packet[1] = opCodes["nack"];

		packet[2] = blockNum[0];
		packet[3] = blockNum[1];
		
		return packet;
	}

	/// <summary>
	/// sendRequest - creates a request packet and starts retreiving a file
	///	          if no error occurs
	/// </summary>
	public void sendRequest()
	{
		if(hostIP != null)
		{
			byte[] modeBytes = Encoding.ASCII.GetBytes(octet);
			byte[] fileBytes = Encoding.ASCII.GetBytes(requestFile);
			byte[] requestPacket = new byte[4 + fileBytes.Length  + modeBytes.Length];

			requestPacket[0] = 0;
			if(error)
				requestPacket[1] = opCodes["readError"];
			else
				requestPacket[1] = opCodes["readNoError"];
			for( int i = 0; i < fileBytes.Length; i++ )
				requestPacket[i + 2] = fileBytes[i];
			requestPacket[fileBytes.Length + 2] = 0;
			for( int i = 0; i < modeBytes.Length; i++ )
				requestPacket[fileBytes.Length + 3 + i] = modeBytes[i];
			requestPacket[requestPacket.Length - 1] = 0;

			IPEndPoint destination = new IPEndPoint(hostIP, 7000);
			byte[] firstBlock = sendReceivePacket(requestPacket, destination, false);
			if(firstBlock != null)
			{
				retrieveFile(firstBlock);
			}
		}	
		closeClient();
	}

	/// <summary>
	/// sendReceivePacket - send a packet to the server and receives a 
	///			packet if finished is false
	/// </summary>
	///
	/// <param name = "packet"> packet to send </param>
	/// <param name = "desination"> IPEndPoint of where to send to </param>
	/// <param name = "finished"> true if done receiving packets false
	///			      otherwise </param>
	///
	/// <return> next packet received or null if error was received or
	///          finished is true
	/// </return>
	public byte[] sendReceivePacket(byte[] packet, IPEndPoint destination, bool finished)
	{
		byte[] receivePacket = null;
		byte[] nackPacket = null;
		byte[] blockNum = null;
		while(receivePacket == null)
		{
			try
			{
				do
				{
					progressBar();
					client.Send(packet, packet.Length, destination);
					receiveLoc = destination;
					if(finished)
						return null;
					receivePacket = client.Receive(ref receiveLoc);
					if(blockNum == null)
					{
						blockNum = new byte[2];
						blockNum[0] = receivePacket[2];
						blockNum[1] = receivePacket[3];
					}
					receivePacket = checkBits(receivePacket);
					if(receivePacket == null && nackPacket == null)
						nackPacket = constructNack(blockNum);
					if(receivePacket == null)
						packet = nackPacket;
				}
				while(receivePacket == null);

				if(!checkError(receivePacket))
					return null;
			}
			catch(SocketException e)
			{
				e.ToString();
			}
		}
		return receivePacket;
	}

	/// <summary>
	/// checkError - checks to see if packet received was an error packet
	/// </summary>
	///
	/// <param name = "packet"> last packet received </param>
	/// 
	/// <return> true if packet wasnt an error packet, false otherwise
	/// </return>
	public bool checkError(byte[] packet)
	{
		if(packet[1] == opCodes["error"])
		{
			byte[] error = new byte[packet.Length - 5];
			for(int i = 4; i < (packet.Length - 1); i++)
			{
				error[i-4] = packet[i];
			}
			Console.WriteLine("Error code " + packet[0] + packet[1] + ": " + Encoding.ASCII.GetString(error));
			return false;
		}
		return true;
	}

	/// <summary>
	/// checkBits - check parity bits, if bits are good strips out parity
	///             bits and returns packet without parities, if bad
	///             attempts to a single bit error
	/// <summary>
	///
	/// <param name = "packet"> the packet to verify </param>
	///
	/// <return> stripped packet or null if packet had 2 or more errors
	/// </return>
	public byte[] checkBits(byte[] packet)
	{
		int bytesRead = 4;
		int strippedIndex = 4;
		int carryCount = 0;
		int zeroCount = 0;

		bool[] parityVals = new bool[6];
		
		byte[] strippedPacket = new byte[420];
		byte[] moveBytes = new byte[3];
		byte[] blockBytes = new byte[4];
		
		BitArray strippedBlock = new BitArray(26);
		BitArray carryBits = new BitArray(8);
		BitArray convertBits = new BitArray(24);
		
		//Copy over opcode and block number bytes since they
		// dont seem to have any parity bits attached
		for(int i = 0; i < 4; i++)
			strippedPacket[i] = packet[i];

		while(bytesRead < packet.Length)
		{
			for(int i = 0; i < 4; i++)
			{
				blockBytes[i] = packet[bytesRead + i];
			}
			
			BitArray block = new BitArray(blockBytes);

			int onesCount = 0;
			for(int i = 0; i < parityVals.Length - 1; i++)
			{
				parityVals[i] = getParityVal(block, i);
				if(parityVals[i] == true)
					onesCount++;
			}
			parityVals[5] = overAllParity(block);
			if(parityVals[5] == true)
				onesCount++;
			
			if(onesCount != 6)
				if(!attemptBlockFix(ref parityVals, ref block))
				{

					return null;
				}
				else
				{
					onesCount = 0;
					for(int i = 0; i < parityVals.Length - 1; i++)
					{
						parityVals[i] = getParityVal(block, i);
						if(parityVals[i] == true)
							onesCount++;
					}
					if(onesCount != 5)
						return null;
				}
			
			int sIndex = 0;
			for(int i = 0; i < block.Count; i++)
			{
				if(!parityBits.Contains(i))
				{
					strippedBlock.Set(sIndex, block.Get(i));
					sIndex++;
				}
			}

			// reverse bit order to original order
			for(int i = 0; i < strippedBlock.Count / 2; i++)
			{
				bool swapBit = strippedBlock.Get(i);
				strippedBlock.Set(i, strippedBlock.Get(strippedBlock.Count - (i + 1)));
				strippedBlock.Set(strippedBlock.Count - (i + 1), swapBit);
			}
			
			for(int carry = 0; carry < carryCount; carry++)
				convertBits.Set(carry, carryBits.Get(carryCount - (carry + 1)));
			
			for(int i = carryCount; i < convertBits.Count; i++)
			{
				convertBits.Set(i, strippedBlock.Get(i - carryCount));
			}
			
			if(bytesRead != 0)
			{
				carryCount = carryCount + 2;
				for(int i = 0; i < carryCount; i++)
					carryBits.Set(i, strippedBlock.Get(strippedBlock.Count - (i + 1)));
			}

			convertBits.CopyTo(moveBytes, 0);
			
			int moveBytesIndex = 0;
			for(int i = strippedIndex; i < strippedIndex + 3; i++)
			{
				strippedPacket[i] = moveBytes[moveBytesIndex];
				moveBytesIndex++;
			}
			strippedIndex += 3;
			
			if(carryCount == 8)
			{
				byte[] carryByte = new byte[1];
				for(int i = 0; i < carryBits.Length / 2; i++)
				{
					bool swapBit = carryBits.Get(i);
					carryBits.Set(i, carryBits.Get(carryBits.Count - (i + 1)));
					carryBits.Set(carryBits.Count - (i + 1), swapBit);
				}
				carryBits.CopyTo(carryByte, 0);
				strippedPacket[strippedIndex] = carryByte[0];
				strippedIndex++;
				carryCount = 0;
			}
			bytesRead += 4;
		}
		zeroCount = 0;
		for(int i = strippedPacket.Length - 1; i >= 0; i--)
		{
			if(strippedPacket[i] == 0)
				zeroCount++;
			else
				break;
		}
		
		if(zeroCount == fullBlock - 4)
		{
			byte[] zeroesRemoved = new byte[fullBlock - zeroCount];
			for(int i = 0; i < zeroesRemoved.Length; i++)
				zeroesRemoved[i] = strippedPacket[i];
			return zeroesRemoved;
		}
		else
		{
			return strippedPacket;
		}
	}

	/// <summary>
	/// getParityVal - verifies a parity bit
	/// </summary>
	///
	/// <param name = "block"> a bitarray of the 32 bits to check </param>
	/// <param name = "parity"> the index of the parity bit to verify 
	/// </param>
	///
	/// <return> bool - true of parity bits is correct, false otherwise
	/// </return>
	public bool getParityVal(BitArray block, int parity)
	{
		int onesCount = 0;
		int startIndex = (int)Math.Pow(2, parity) - 1;
		int endIndex = block.Count - 1;
		int incrementor = (int)Math.Pow(2, parity + 1); 
		
		int count = 0;
		int index;

		for(int i = startIndex; i < endIndex; i += incrementor)
		{
			if(block.Get(i) == true)
			{
				onesCount++;
			}
			index = i + 1;
			while(index < endIndex && count < ((incrementor/2) - 1))
			{	
				if(block.Get(index) == true)
				{
					onesCount++;
				}
				index++;
				count++;
			}
			count = 0;
		}
		return ((onesCount % 2) == 0);
	}

	/// <summary>
	/// overAllParity - verifies bit 31 which is the parity for a 32
	///                 bit block
	/// </summary>
	///
	/// <param name = "block"> the 32 bit block to verify </param>
	///
	/// <return> bool - true if overall parity is correct, false otherwise
	/// </return>
	public bool overAllParity(BitArray block)
	{
		int onesCount = 0;
		for(int bit = 0; bit < block.Count; bit++)
			if(block.Get(bit) == true)
				onesCount++;

		return (onesCount % 2 == 0);
	}

	/// <summary>
	/// attemptBlockFix - attempts to find and fix a single bit error
	/// </summary>
	///
	/// <param = "parityVals"> an array of the parity values </param>
	/// <param = "block"> the 32 bit block to attempt to fix </param>
	///
	/// <return> bool - true if single bit error was found, false if
	///                 more than one error was found 
	/// </return>
	public bool attemptBlockFix(ref bool[] parityVals, ref BitArray block)
	{
		int changeIndex = 0;
		for(int i = 0; i < parityVals.Length - 1; i++)
			if(parityVals[i] == false)
				changeIndex += (int)Math.Pow(2, i);
		bool bit = block.Get(changeIndex);
		bit = !bit;
		block.Set(changeIndex, bit);
		parityVals[5] = !parityVals[5];
		
		return (parityVals[5] == true);
	}

	/// <summary>
	/// closeClient - closes UdpClient used to send and received packets
	/// </summary>
	public void closeClient()
	{
		client.Close();
	}

	/// <summary>
	/// Main - rusn a simple TFTPreader with mode, host and file given as
	///	   command line arguments
	/// </summary>
	static public void Main(string[] args)
	{
		if(args.Length == 3)
		{
			HammingTFTP tftp = new HammingTFTP(args[0], args[1], args[2]);
			tftp.sendRequest();
		}
		else
			Console.WriteLine("Usage: [mono] TFTPreader.exe [error|noerror] tftp−host file");
	}
}
