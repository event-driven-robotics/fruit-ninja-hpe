using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
        int bytesRead = 0;
        try
        {
            bytesRead = stream.Read(bytesToRead, 0, bufSize);
        } catch (System.IO.IOException e) {
            Debug.Log("AAAAAA");
        }
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

        TcpSend("CONNECT unity\n", stream);
        string reply = TcpReceive(stream);
        if (!reply.StartsWith("Welcome unity"))
        {
            throw new System.Exception("Could not connect to port " + port + "on address " + ip);
        }

        stream.ReadTimeout = 1000;
        stream.WriteTimeout = 1000;

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
            throw new System.Exception("Could not find port with name" + portName + 
                ". Available ports are" + string.Join(" ", availablePorts));
        }

        NetworkStream portStream = getPortStream(serverIp, portPort);
        TcpSend("r\n", portStream);
        return portStream;

    }
}

public class Blade : MonoBehaviour
{
    public Vector3 direction { get; private set; }

    private Collider sliceCollider;
    private TrailRenderer sliceTrail;

    public float sliceForce = 5f;
    public float minSliceVelocity = 0.01f;
    public bool isRightHand = true;
    public Vector2 imageResoultion = new Vector2(640, 480);

    private bool slicing;

    Vector3 handPosition;
    YarpNetwork yarp;
    NetworkStream skeletonPortStream;
    Thread readingThread;

    private void Awake()
    {
        yarp = new YarpNetwork();
        skeletonPortStream = yarp.connectToPort("/file/ch2GT50Hzskeleton:o");
        sliceCollider = GetComponent<Collider>();
        sliceTrail = GetComponentInChildren<TrailRenderer>();
        readingThread = new Thread(ReadPort);
        readingThread.Start();
        StartSlice();
    }

    private void OnEnable()
    {
        StopSlice();
    }

    private void OnDisable()
    {
        StopSlice();
    }

    private void ReadPort()
    {
        int index;
        if (isRightHand)
        {
            index = 14;
        } else
        {
            index = 16;
        }
        while (Thread.CurrentThread.IsAlive)
        {
            string msg = yarp.TcpReceive(skeletonPortStream);
            if (msg.Length == 0) continue;
            MatchCollection matches = Regex.Matches(msg, @"SKLT \((.*)\)");

            string[] skeleton = matches[0].Groups[1].ToString().Split(' ');
            handPosition.x = ((float.Parse(skeleton[index]) / imageResoultion.x) - 0.5f) * 40;
            handPosition.y = ((float.Parse(skeleton[index + 1]) / imageResoultion.y) - 0.5f) * -20;
        }
    }

    private void Update()
    {
        if (!slicing) {
            StartSlice();
        } else {
            ContinueSlice();
        }
    }

    private void StartSlice()
    {
        handPosition.z = 0f;

        transform.position = handPosition;

        slicing = true;
        sliceCollider.enabled = true;
        sliceTrail.enabled = true;
        sliceTrail.Clear();
    }

    private void StopSlice()
    {
        slicing = false;
        sliceCollider.enabled = false;
        sliceTrail.enabled = false;
    }

    private void ContinueSlice()
    {
        Vector3 newPosition = handPosition;
        newPosition.z = 0f;

        direction = newPosition - transform.position;

        float velocity = direction.magnitude / Time.deltaTime;
        sliceCollider.enabled = velocity > minSliceVelocity;

        transform.position = newPosition;
    }

    private void OnApplicationQuit()
    {
        readingThread.Abort();
        readingThread.Join();
    }

}
