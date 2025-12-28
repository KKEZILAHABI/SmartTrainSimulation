using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;

public class TCPReceiver : MonoBehaviour
{
    Thread receiveThread;
    Thread sendThread;
    TcpListener commandListener;
    TcpListener frameListener;
    
    public int commandPort = 5000;
    public int framePort = 5001;
    public Camera renderCamera;
    
    string receivedData = "";
    bool newData = false;
    
    Vector3 moveInput = Vector3.zero;
    public float speed = 5f;
    CharacterController controller;
    
    // Frame streaming
    RenderTexture renderTexture;
    Texture2D screenCapture;
    public int frameWidth = 640;
    public int frameHeight = 480;
    public int targetFPS = 30;
    float frameInterval;
    float lastFrameTime;
    
    TcpClient frameClient;
    NetworkStream frameStream;
    bool isFrameClientConnected = false;
    Queue<byte[]> frameQueue = new Queue<byte[]>();
    object queueLock = new object();
    
    // For temporary rendering
    bool isRenderingToTexture = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        
        // Setup camera - IMPORTANT: Don't assign targetTexture yet
        if (renderCamera == null)
            renderCamera = Camera.main;
        
        // Check if camera exists
        if (renderCamera == null)
        {
            Debug.LogError("No camera found! Ensure there's an active camera with MainCamera tag.");
            return;
        }
        
        Debug.Log("Using camera: " + renderCamera.name + " for frame capture");
        
        // Create render texture and texture for capture
        renderTexture = new RenderTexture(frameWidth, frameHeight, 24);
        renderTexture.Create();
        screenCapture = new Texture2D(frameWidth, frameHeight, TextureFormat.RGB24, false);
        
        frameInterval = 1f / targetFPS;
        lastFrameTime = Time.time;
        
        StartServers();
    }

    void StartServers()
    {
        // Start command receiver thread
        receiveThread = new Thread(new ThreadStart(ListenForCommands));
        receiveThread.IsBackground = true;
        receiveThread.Start();
        
        // Start frame sender thread
        sendThread = new Thread(new ThreadStart(SendFrames));
        sendThread.IsBackground = true;
        sendThread.Start();
    }

    void ListenForCommands()
    {
        try
        {
            commandListener = new TcpListener(IPAddress.Any, commandPort);
            commandListener.Start();
            Debug.Log("Command listener started on port " + commandPort);
            byte[] bytes = new byte[1024];
            
            while (true)
            {
                using (TcpClient client = commandListener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    int length;
                    while ((length = stream.Read(bytes, 0, bytes.Length)) != 0)
                    {
                        receivedData = Encoding.ASCII.GetString(bytes, 0, length);
                        newData = true;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.Log("Command listener error: " + e.ToString());
        }
    }

    void SendFrames()
    {
        try
        {
            frameListener = new TcpListener(IPAddress.Any, framePort);
            frameListener.Start();
            Debug.Log("Frame listener started on port " + framePort);
            
            while (true)
            {
                frameClient = frameListener.AcceptTcpClient();
                frameStream = frameClient.GetStream();
                isFrameClientConnected = true;
                Debug.Log("MATLAB connected for frame streaming");
                
                try
                {
                    while (frameClient.Connected && isFrameClientConnected)
                    {
                        byte[] frameData = null;
                        lock (queueLock)
                        {
                            if (frameQueue.Count > 0)
                            {
                                frameData = frameQueue.Dequeue();
                            }
                        }
                        
                        if (frameData != null)
                        {
                            // Send frame size first (4 bytes)
                            byte[] sizeBytes = System.BitConverter.GetBytes(frameData.Length);
                            frameStream.Write(sizeBytes, 0, 4);
                            
                            // Send frame data
                            frameStream.Write(frameData, 0, frameData.Length);
                            frameStream.Flush();
                        }
                        else
                        {
                            Thread.Sleep(10);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.Log("Frame streaming error: " + e.ToString());
                }
                finally
                {
                    isFrameClientConnected = false;
                    if (frameStream != null) frameStream.Close();
                    if (frameClient != null) frameClient.Close();
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.Log("Frame listener error: " + e.ToString());
        }
    }

    void Update()
    {
        // Handle commands
        if (newData)
        {
            ParseCommand(receivedData);
            newData = false;
        }
        
        // Apply movement
        controller.Move(moveInput * speed * Time.deltaTime);
        
        // Capture and queue frames
        if (Time.time - lastFrameTime >= frameInterval)
        {
            CaptureFrame();
            lastFrameTime = Time.time;
        }
    }

    void CaptureFrame()
    {
        if (!isFrameClientConnected || renderCamera == null) return;
        
        try
        {
            // Method 2: Render camera to temporary texture without affecting display
            RenderTexture currentTarget = renderCamera.targetTexture;
            RenderTexture currentActive = RenderTexture.active;
            
            // Temporarily set target texture
            renderCamera.targetTexture = renderTexture;
            renderCamera.Render(); // Force render to texture
            
            // Read from the render texture
            RenderTexture.active = renderTexture;
            screenCapture.ReadPixels(new Rect(0, 0, frameWidth, frameHeight), 0, 0);
            screenCapture.Apply();
            
            // Restore camera settings
            renderCamera.targetTexture = currentTarget;
            RenderTexture.active = currentActive;
            
            // Encode as JPEG
            byte[] jpegData = screenCapture.EncodeToJPG(75);
            
            lock (queueLock)
            {
                frameQueue.Enqueue(jpegData);
                // Keep queue size manageable
                while (frameQueue.Count > 5)
                {
                    frameQueue.Dequeue();
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Frame capture error: " + e.Message);
        }
    }

    void ParseCommand(string command)
    {
        // Expected format: "W", "A", "S", "D", "STOP"
        // Camera View is rotated 90 degrees clockwise(-90) in relation to the character orientation
        // (Camera is facing character's right side), hence WASD control are -90 degrees rotated
        switch (command.Trim())
        {
            case "W":
                moveInput = Vector3.right; // moves character right, moving camera forward
                break;
            case "A":
                moveInput = Vector3.forward; // moves character forward, camera moves left
                break;
            case "S":
                moveInput = Vector3.left; // moves character left, moves camera back
                break;
            case "D":
                moveInput = Vector3.back; // moves character back, moving camera right
                break;
            case "STOP":
                moveInput = Vector3.zero;
                break;
        }
    }

    void OnApplicationQuit()
    {
        isFrameClientConnected = false;
        
        // Restore camera target texture if needed
        if (renderCamera != null)
        {
            renderCamera.targetTexture = null;
        }
        
        if (receiveThread != null && receiveThread.IsAlive)
            receiveThread.Abort();
        if (sendThread != null && sendThread.IsAlive)
            sendThread.Abort();
            
        if (commandListener != null)
            commandListener.Stop();
        if (frameListener != null)
            frameListener.Stop();
        if (frameStream != null)
            frameStream.Close();
        if (frameClient != null)
            frameClient.Close();
            
        if (renderTexture != null)
            renderTexture.Release();
    }
}