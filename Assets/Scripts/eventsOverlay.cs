using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class eventsOverlay : MonoBehaviour
{
    private byte[] dataBuffer; // Adjust the buffer size as needed
    private Texture2D receivedTexture;
    Thread udpReceivingThread;
    Mutex mut;

    void Start()
    {
        receivedTexture = new Texture2D(640, 480);
        udpReceivingThread = new Thread(receiveUdp);
        mut = new Mutex();
        udpReceivingThread.Start();
    }

    void receiveUdp()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        var udpClient = new UdpClient(37623); 
        udpClient.Client.ReceiveTimeout = 200;
        while (Thread.CurrentThread.IsAlive)
        {
            mut.WaitOne();
            dataBuffer = udpClient.Receive(ref remoteEndPoint);
            mut.ReleaseMutex();
        }
        udpClient.Close();

    }
    void updateTexture(byte[] imgData)
    {
        // Create a new texture and load the image data
        mut.WaitOne();
        receivedTexture.LoadImage(imgData);
        mut.ReleaseMutex();
        
        // Now you can use 'receivedTexture' for visualization
        GetComponent<Image>().material.SetTexture("_MainTex", receivedTexture);
    }

    void Update()
    {
        updateTexture(dataBuffer);
    }

    void OnApplicationQuit()
    {
        udpReceivingThread.Abort();  
    }
}