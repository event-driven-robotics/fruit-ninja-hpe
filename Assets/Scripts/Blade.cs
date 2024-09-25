using UnityEngine;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;
using Unity.Collections;


public class CircularBuffer<T>
{
    private T[] buffer;
    public int size { get; private set; }
    private int tail;

    public CircularBuffer(int capacity)
    {
        size = capacity;
        buffer = new T[capacity];
        tail = 0;
    }

    public void Enqueue(T item)
    {
        buffer[tail] = item;
        tail = (tail + 1) % size;
    }
    
    public T [] GetLastN(int n)
    {
        
        T[] output = new T[n];
        for (int i = 0; i < n; i++)
        {
            output[i] = buffer[(tail + i) % size];
        }
        return output;

    }

    public float Average()
    {
        if (typeof(T) != typeof(float))
        {
            throw new System.TypeAccessException("T must be float");
        }
        float [] floatvalues = new float[buffer.Length];
        floatvalues = buffer.Cast<float>().ToArray();
        float[] sortedData = floatvalues.OrderBy(x => x).ToArray();
        int q1Index = (int)(0.25 * (sortedData.Length - 1));
        int q3Index = (int)(0.75 * (sortedData.Length - 1));

        float q1 = sortedData[q1Index];
        float q3 = sortedData[q3Index];
        float iqr = q3 - q1;

        float lowerBound = q1 - 1.5f * iqr;
        float upperBound = q3 + 1.5f * iqr;

        var filteredData = sortedData.Where(x => x >= lowerBound && x <= upperBound).ToArray();

        return filteredData.Average();
    }
}
public class Blade : MonoBehaviour
{
    public Vector3 velocity { get; private set; }

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

    private CircularBuffer<float> last_xs;
    private CircularBuffer<float> last_ys;
    private CircularBuffer<System.DateTime> last_timestamps;
    
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
        last_xs = new CircularBuffer<float>(bufferSize);
        last_ys = new CircularBuffer<float>(bufferSize);
        last_timestamps = new CircularBuffer<System.DateTime>(bufferSize);
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

        while (Thread.CurrentThread.IsAlive)
        {
            if (killFlag) break;
            if (last_xs.size != bufferSize)
            {
                last_xs = new CircularBuffer<float>(bufferSize);
                last_ys = new CircularBuffer<float>(bufferSize);
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
                last_timestamps.Enqueue(System.DateTime.Now);
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
        float[] xs = last_xs.GetLastN(5);
        float[] ys = last_ys.GetLastN(5);
        System.DateTime[] timestamps = last_timestamps.GetLastN(5);
        
        // Using the 5-point derivative formula
        // double h1 = timestamps[1] - timestamps[0];
        double h2 = (timestamps[2] - timestamps[1]).TotalSeconds;
        double h3 = (timestamps[3] - timestamps[2]).TotalSeconds;
        // double h4 = timestamps[4] - timestamps[3];

        double Vx = (-xs[4] + 8 * xs[3] - 8 * xs[1] + xs[0]) / (6 * (h2 + h3));
        double Vy = (-ys[4] + 8 * ys[3] - 8 * ys[1] + ys[0]) / (6 * (h2 + h3));
        
        this.velocity = new Vector3((float)Vx, (float)Vy, 0);
            
        float velocity = this.velocity.magnitude;
        sliceCollider.enabled = velocity > minSliceVelocity;
        
        handPosition.z = 0f;
        transform.position = handPosition;
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
