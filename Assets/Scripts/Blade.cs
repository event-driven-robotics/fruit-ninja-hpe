using UnityEngine;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;


public class CircularBuffer
{
    private float[] buffer;
    public int size { get; private set; }
    private int head;
    private int tail;

    public CircularBuffer(int capacity)
    {
        size = capacity;
        buffer = new float[capacity];
        head = 0;
        tail = 0;
    }

    public void Enqueue(float item)
    {
        buffer[tail] = item;
        tail = (tail + 1) % size;
        if (tail == head)
        {
            head = (head + 1) % size;
        }
    }

    public float Dequeue()
    {
        if (IsEmpty())
        {
            throw new System.Exception("Buffer is empty");
        }
        float item = buffer[head];
        head = (head + 1) % size;
        return item;
    }

    public bool IsEmpty()
    {
        return head == tail;
    }

    public bool IsFull()
    {
        return (tail + 1) % size == head;
    }

    public float Average()
    {
        float avg = 0;
        foreach (var item in buffer) avg += item;
        return avg / size;
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
        skeletonPortStream = yarp.connectToPort("/edpr_april/sklt:o");
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
        }
        else
        {
            index = 16;
        }
        CircularBuffer last_xs = new CircularBuffer(5);
        CircularBuffer last_ys = new CircularBuffer(5);
        while (Thread.CurrentThread.IsAlive)
        {
            string msg = yarp.TcpReceive(skeletonPortStream);
            if (msg.Length == 0) continue;
            MatchCollection matches = Regex.Matches(msg, @"SKLT \((.*)\)");
            if (matches.Count == 0) continue;
            string[] skeleton = matches[0].Groups[1].ToString().Split(' ');
            var x = ((float.Parse(skeleton[index]) / imageResoultion.x) - 0.5f) * -40;
            var y = ((float.Parse(skeleton[index + 1]) / imageResoultion.y) - 0.5f) * -20;
            //Debug.Log(Vector2.Distance(new Vector2(x, y), new Vector2(handPosition.x, handPosition.y)));
            last_xs.Enqueue(x);
            last_ys.Enqueue(y);
            handPosition.x = last_xs.Average();
            handPosition.y = last_ys.Average();
        }
    }

    private void Update()
    {
        if (!slicing)
        {
            StartSlice();
        }
        else
        {
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
