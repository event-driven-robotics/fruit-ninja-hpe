using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class YarpNetwork
{
    int serverPort = 10000;
    string serverIp = "127.0.0.1";


    public void TcpSend(string msg, NetworkStream stream)
    {
        byte[] message = System.Text.Encoding.ASCII.GetBytes(msg);
        stream.Write(message, 0, message.Length);
    }

    public string TcpReceive(NetworkStream stream, int bufSize = 1024)
    {

        byte[] bytesToRead = new byte[bufSize];
        int bytesRead = stream.Read(bytesToRead, 0, bufSize);
        return Encoding.ASCII.GetString(bytesToRead, 0, bytesRead);
    }

    private NetworkStream getPortStream(string ip, int port)
    {

        TcpClient tcpClient = new TcpClient();
        if (!tcpClient.ConnectAsync(ip, port).Wait(1000))
        {
            throw new System.Exception("Could not find YARP server.");
        }
        NetworkStream stream = tcpClient.GetStream();

        stream.ReadTimeout = 1000;
        stream.WriteTimeout = 1000;
        TcpSend("CONNECT unity\n", stream);
        string reply = TcpReceive(stream);
        if (!reply.StartsWith("Welcome unity"))
        {
            throw new System.Exception("Could not connect to port " + port + "on address " + ip);
        }


        return stream;
    }

    public NetworkStream connectToPort(string portName)
    {
        NetworkStream serverStream = getPortStream(serverIp, serverPort);

        TcpSend("d\n", serverStream);
        TcpSend("list\n", serverStream);

        string list = TcpReceive(serverStream);

        MatchCollection matches = Regex.Matches(list, @"(\/.+) ip (\d+.\d+.\d+.\d+) port (\d+)");

        string portIp = string.Empty;
        int portPort=10000;
        bool found = false;
        List<string> availablePorts = new List<string>();
        foreach (Match match in matches)
        {
            GroupCollection groups = match.Groups;
            string name = groups[1].ToString();
            availablePorts.Add(name);
            portIp = groups[2].ToString();
            portPort = int.Parse(groups[3].ToString());
            if (name.Equals(portName))
            {
                found = true;
                break;
            }
        }

        if (!found){
            throw new System.Exception("Could not find port with name " + portName + 
                ". Available ports are: " + string.Join(", ", availablePorts));
        }

        NetworkStream portStream = getPortStream(serverIp, portPort);
        TcpSend("r\n", portStream);
        return portStream;

    }
}
