using UnityEngine;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;


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
        var sortedData = buffer.OrderBy(x => x).ToArray();
        int q1Index = (int)(0.25 * (sortedData.Length - 1));
        int q3Index = (int)(0.75 * (sortedData.Length - 1));

        double q1 = sortedData[q1Index];
        double q3 = sortedData[q3Index];
        double iqr = q3 - q1;

        double lowerBound = q1 - 1.5 * iqr;
        double upperBound = q3 + 1.5 * iqr;

        var filteredData = sortedData.Where(x => x >= lowerBound && x <= upperBound).ToArray();

        return sortedData.Average();
    }
}
public class Blade : MonoBehaviour
{
    public Vector3 direction { get; private set; }

    private Collider sliceCollider;
    private TrailRenderer sliceTrail;

    public float sliceForce = 5f;
    public float minSliceVelocity = 0.01f;
    public int bufferSize = 5;
    public bool isRightHand = true;
    public Vector2 imageResoultion = new Vector2(640, 480);

    private bool slicing;
    private bool isSkeletonPortConnected = false;
    private bool killFlag = false;

    Vector3 handPosition;
    YarpNetwork yarp;
    NetworkStream skeletonPortStream;
    Thread readingThread;
    Thread connectionThread;

    private void Awake()
    {
        yarp = new YarpNetwork();
        sliceCollider = GetComponent<Collider>();
        sliceTrail = GetComponentInChildren<TrailRenderer>();
        connectionThread = new Thread(ConnectionAttemptLoop);
        connectionThread.Start();
    }

    private void ConnectionAttemptLoop()
    {
        while (!isSkeletonPortConnected)
        {
            if (killFlag) return;
            try
            {
                skeletonPortStream = yarp.connectToPort("/edpr_april/sklt:o");
                skeletonPortStream.ReadTimeout = 1000;
                isSkeletonPortConnected = true;
            }
            catch (System.Exception e)
            {
                Debug.Log("Could not connect to port /edpr_april/sklt:o. Please verify that yarp is up and port exists.");
                Thread.Sleep(1000);
            }
        }
        readingThread = new Thread(ReadPort);
        readingThread.Start();

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
        CircularBuffer last_xs = new CircularBuffer(bufferSize);
        CircularBuffer last_ys = new CircularBuffer(bufferSize);
        while (Thread.CurrentThread.IsAlive)
        {
            if (killFlag) break;
            if (last_xs.size != bufferSize)
            {
                last_xs = new CircularBuffer(bufferSize);
                last_ys = new CircularBuffer(bufferSize);
            }
            try
            {
                string msg = yarp.TcpReceive(skeletonPortStream);
                if (msg.Length == 0)
                {
                    connectionThread.Join();
                    isSkeletonPortConnected = false;
                    connectionThread = new Thread(ConnectionAttemptLoop);
                    connectionThread.Start();
                    return;
                }
                MatchCollection matches = Regex.Matches(msg, @"SKLT \((.*)\)");
                if (matches.Count == 0) continue;
                string[] skeleton = matches[0].Groups[1].ToString().Split(' ');
                var x = ((float.Parse(skeleton[index]) / imageResoultion.x) - 0.5f) * -26.6f; // TODO make this relative to aspect ratio
                var y = ((float.Parse(skeleton[index + 1]) / imageResoultion.y) - 0.5f) * -20;

                //Debug.Log(Vector2.Distance(new Vector2(x, y), new Vector2(handPosition.x, handPosition.y)));
                last_xs.Enqueue(x);
                last_ys.Enqueue(y);
                handPosition.x = last_xs.Average();
                handPosition.y = last_ys.Average();
            }
            catch (System.IO.IOException e)
            {
                Thread.Sleep(1000);
                continue;
            }

        }
    }

    private void Update()
    {
        if (!isSkeletonPortConnected)
        {
            StopSlice();
            return;
        }
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
        killFlag = true;
        try
        {
            connectionThread.Join();
        }
        catch (System.NullReferenceException e) { }
        try
        {
            readingThread.Join();
        }
        catch (System.NullReferenceException e) { }
    }

}
