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
    
    // Frame streaming - OPTIMIZED
    RenderTexture renderTexture;
    Texture2D screenCapture;
    public int frameWidth = 640;
    public int frameHeight = 480;
    public int targetFPS = 60;
    public int jpegQuality = 100;  // REDUCED quality for faster encoding
    float frameInterval;
    float lastFrameTime;
    
    TcpClient frameClient;
    NetworkStream frameStream;
    bool isFrameClientConnected = false;
    
    // OPTIMIZATION: Only keep the latest frame
    byte[] latestFrame = null;
    object frameLock = new object();
    bool hasNewFrame = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        
        if (renderCamera == null)
            renderCamera = Camera.main;
        
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
        receiveThread = new Thread(new ThreadStart(ListenForCommands));
        receiveThread.IsBackground = true;
        receiveThread.Start();
        
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
                {
                    // OPTIMIZATION: Disable Nagle's algorithm for lower latency
                    client.NoDelay = true;
                    
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
                
                // OPTIMIZATION: Configure TCP for low latency
                frameClient.NoDelay = true;  // Disable Nagle's algorithm
                frameClient.SendBufferSize = 65536;  // Smaller send buffer
                
                frameStream = frameClient.GetStream();
                isFrameClientConnected = true;
                Debug.Log("MATLAB connected for frame streaming");
                
                try
                {
                    while (frameClient.Connected && isFrameClientConnected)
                    {
                        byte[] frameData = null;
                        
                        // OPTIMIZATION: Only get the latest frame, skip old ones
                        lock (frameLock)
                        {
                            if (hasNewFrame)
                            {
                                frameData = latestFrame;
                                hasNewFrame = false;
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
                            Thread.Sleep(5);  // Shorter sleep for more responsive updates
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
        if (newData)
        {
            ParseCommand(receivedData);
            newData = false;
        }
        
        controller.Move(moveInput * speed * Time.deltaTime);
        
        // Capture frames at target FPS
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
            RenderTexture currentTarget = renderCamera.targetTexture;
            RenderTexture currentActive = RenderTexture.active;
            
            renderCamera.targetTexture = renderTexture;
            renderCamera.Render();
            
            RenderTexture.active = renderTexture;
            screenCapture.ReadPixels(new Rect(0, 0, frameWidth, frameHeight), 0, 0);
            screenCapture.Apply();
            
            renderCamera.targetTexture = currentTarget;
            RenderTexture.active = currentActive;
            
            // OPTIMIZATION: Lower JPEG quality for faster encoding
            byte[] jpegData = screenCapture.EncodeToJPG(jpegQuality);
            
            // OPTIMIZATION: Always replace with latest frame (drop old frames)
            lock (frameLock)
            {
                latestFrame = jpegData;
                hasNewFrame = true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Frame capture error: " + e.Message);
        }
    }

    void ParseCommand(string command)
    {
        switch (command.Trim())
        {
            case "W":
                moveInput = Vector3.right;
                break;
            case "A":
                moveInput = Vector3.forward;
                break;
            case "S":
                moveInput = Vector3.left;
                break;
            case "D":
                moveInput = Vector3.back;
                break;
            case "STOP":
                moveInput = Vector3.zero;
                break;
        }
    }

    void OnApplicationQuit()
    {
        isFrameClientConnected = false;
        
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