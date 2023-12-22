using System.IO;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;
using System;

public class SharedMemoryStream
{
    private MemoryStream ms = new MemoryStream();
    private object lockObject = new object();

    public void AppendData(byte[] data)
    {
        lock (lockObject)
        {
            ms.Write(data, 0, data.Length);
        }
    }

    public byte[] ReadAndClear()
    {
        lock (lockObject)
        {
            byte[] result = ms.ToArray();
            ms.Position = 0;
            ms.SetLength(0);
            return result;
        }
    }
}

public class eventsOverlay : MonoBehaviour
{
    private SharedMemoryStream sharedMemoryStream;
    private byte[] imageData;
    private bool killFlag = false;
    private bool newImageIsAvailable = false;
    private bool useDefaultImage = true;
    private Texture2D receivedTexture;
    private Texture2D defaultTexture;
    private Thread udpReceivingThread;
    private TcpListener tcpImageListener;

    void Start()
    {
        defaultTexture = Resources.Load<Texture2D>("Textures/Wood");
        sharedMemoryStream = new SharedMemoryStream();
        receivedTexture = new Texture2D(640, 480);
        udpReceivingThread = new Thread(ReceiveBackgroundImage);
        udpReceivingThread.Start();
    }

    void ReceiveBackgroundImage()
    {
        byte[] dataBuffer = new byte[500000];
        
        Socket client = new Socket(SocketType.Stream, ProtocolType.Tcp);
        bool socketNeedsAccept = true;
        int receivedBufSize = 0;
        while (!killFlag)
        {
            if (socketNeedsAccept)
            {
                try
                {
                    Debug.Log("Waiting for incoming connection");
                    tcpImageListener = new TcpListener(IPAddress.Any, 10099);
                    tcpImageListener.Start();
                    client = tcpImageListener.AcceptSocket();
                    Debug.Log("Connected");
                    socketNeedsAccept = false;
                    client.ReceiveTimeout = 100000;
                }
                catch (SocketException e)
                {
                    break;
                }
            }
            try
            {
                receivedBufSize = client.Receive(dataBuffer);
                useDefaultImage = false;
                client.ReceiveTimeout = 1000;
                byte[] subset = new byte[receivedBufSize];
                Array.Copy(dataBuffer, 0, subset, 0, receivedBufSize);
                sharedMemoryStream.AppendData(subset);

                if (dataBuffer[receivedBufSize - 2] == 255 && dataBuffer[receivedBufSize - 1] == 217)
                {
                    imageData = sharedMemoryStream.ReadAndClear();
                    newImageIsAvailable = true;
                }
            }
            catch (SocketException e)
            {
                Debug.Log("TCP connection interrupted.");
                tcpImageListener.Stop();
                socketNeedsAccept = true;
                useDefaultImage = true;
                newImageIsAvailable = false;
                continue;
            }
            catch (IndexOutOfRangeException e)
            {
                Debug.Log(String.Format("TODO investigate further this catch branch." +
                    " This exception happens either if the received packet size is less or equal than 1" +
                    " or on connection interruptions if not handled in the SocketException branch. " +
                    "In this case the packet size is {0}", receivedBufSize));
                continue;
            }

        }
        client.Close();
        tcpImageListener.Stop();

    }

    void Update()
    {
        GetComponent<Image>().SetMaterialDirty(); // Necessary to switch from default to received texture. Don't know why
        if (useDefaultImage)
        {
            GetComponent<Image>().material.SetTexture("_MainTex", defaultTexture);

        }
        if (newImageIsAvailable)
        {   
            receivedTexture.LoadImage(imageData);
            newImageIsAvailable = false;
            GetComponent<Image>().material.SetTexture("_MainTex", receivedTexture);
        }
    }

    void OnApplicationQuit()
    {
        tcpImageListener.Stop();
        killFlag = true;
        udpReceivingThread.Join();
    }
}