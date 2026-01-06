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
    Thread statusThread;
    TcpListener commandListener;
    TcpListener frameListener;
    TcpListener statusListener;
    
    public int commandPort = 5000;
    public int framePort = 5001;
    public int statusPort = 5002;
    public Camera renderCamera;
    
    string receivedData = "";
    bool newData = false;
    
    Vector3 moveInput = Vector3.zero;
    float rotationInput = 0f;
    float pitchInput = 0f;
    public float speed = 5f;
    public float rotationSpeed = 90f;
    public float pitchSpeed = 60f;
    public float fixedHeight = 1.7f;
    public float minPitch = -80f;
    public float maxPitch = 80f;
    CharacterController controller;
    float currentPitch = 0f;
    
    // Frame streaming - OPTIMIZED
    RenderTexture renderTexture;
    Texture2D screenCapture;
    public int frameWidth = 640;
    public int frameHeight = 480;
    public int targetFPS = 60;
    public int jpegQuality = 100;
    float frameInterval;
    float lastFrameTime;
    
    TcpClient frameClient;
    NetworkStream frameStream;
    bool isFrameClientConnected = false;
    
    // Status streaming
    TcpClient statusClient;
    NetworkStream statusStream;
    bool isStatusClientConnected = false;
    Queue<string> statusMessages = new Queue<string>();
    object statusLock = new object();
    
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
        
        statusThread = new Thread(new ThreadStart(HandleStatusConnection));
        statusThread.IsBackground = true;
        statusThread.Start();
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
                            Debug.Log("Received command: " + receivedData);
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
                frameClient.NoDelay = true;
                frameClient.SendBufferSize = 65536;
                
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
                            Thread.Sleep(5);
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

    void HandleStatusConnection()
    {
        try
        {
            statusListener = new TcpListener(IPAddress.Any, statusPort);
            statusListener.Start();
            Debug.Log("Status listener started on port " + statusPort);
            
            while (true)
            {
                statusClient = statusListener.AcceptTcpClient();
                statusClient.NoDelay = true;
                statusStream = statusClient.GetStream();
                isStatusClientConnected = true;
                Debug.Log("MATLAB connected for status updates");
                
                try
                {
                    while (statusClient.Connected && isStatusClientConnected)
                    {
                        string message = null;
                        
                        lock (statusLock)
                        {
                            if (statusMessages.Count > 0)
                            {
                                message = statusMessages.Dequeue();
                            }
                        }
                        
                        if (message != null)
                        {
                            byte[] data = Encoding.UTF8.GetBytes(message);
                            statusStream.Write(data, 0, data.Length);
                            statusStream.Flush();
                            Debug.Log("Sent status to MATLAB: " + message);
                        }
                        else
                        {
                            Thread.Sleep(50);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.Log("Status streaming error: " + e.ToString());
                }
                finally
                {
                    isStatusClientConnected = false;
                    if (statusStream != null) statusStream.Close();
                    if (statusClient != null) statusClient.Close();
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.Log("Status listener error: " + e.ToString());
        }
    }

    // PUBLIC METHOD: Send status message to MATLAB
    public void SendStatusToMatlab(string statusMessage)
    {
        if (!isStatusClientConnected)
        {
            Debug.LogWarning("Status client not connected - cannot send: " + statusMessage);
            return;
        }
        
        lock (statusLock)
        {
            statusMessages.Enqueue(statusMessage);
        }
        
        Debug.Log("Queued status message: " + statusMessage);
    }

    // PUBLIC METHOD: Notify simulation end
    public void NotifySimulationEnd(bool success)
    {
        string message = success ? "SIMULATION_ENDED:SUCCESS" : "SIMULATION_ENDED:FAILURE";
        SendStatusToMatlab(message);
    }

    void Update()
    {
        if (newData)
        {
            ParseCommand(receivedData);
            newData = false;
        }
        
        // Apply movement
        Vector3 movement = moveInput * speed * Time.deltaTime;
        controller.Move(movement);
        
        // Apply rotation around Y axis only
        if (rotationInput != 0)
        {
            float rotationAmount = rotationInput * rotationSpeed * Time.deltaTime;
            transform.Rotate(0, rotationAmount, 0, Space.World);
            Debug.Log("Rotating: " + rotationAmount + " degrees");
        }
        
        // Apply pitch (look up/down) - only rotate the camera, not the character
        if (pitchInput != 0)
        {
            float pitchAmount = pitchInput * pitchSpeed * Time.deltaTime;
            currentPitch = Mathf.Clamp(currentPitch + pitchAmount, minPitch, maxPitch);
            Debug.Log("Pitch: " + currentPitch + " degrees");
        }
        
        // Always apply camera rotation: preserve parent's Y rotation, apply pitch on X
        if (renderCamera != null)
        {
            // Get the parent's (character's) Y rotation
            float yRotation = transform.eulerAngles.y;
            // Apply pitch to camera while maintaining character's yaw
            renderCamera.transform.rotation = Quaternion.Euler(currentPitch, yRotation, 0);
        }
        
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
        string cmd = command.Trim();
        Debug.Log("Parsing command: [" + cmd + "]");
        
        switch (cmd)
        {
            case "W":
                moveInput = transform.forward;
                rotationInput = 0f;
                pitchInput = 0f;
                Debug.Log("Command: Forward");
                break;
            case "A":
                moveInput = -transform.right;
                rotationInput = 0f;
                pitchInput = 0f;
                Debug.Log("Command: Left");
                break;
            case "S":
                moveInput = -transform.forward;
                rotationInput = 0f;
                pitchInput = 0f;
                Debug.Log("Command: Backward");
                break;
            case "D":
                moveInput = transform.right;
                rotationInput = 0f;
                pitchInput = 0f;
                Debug.Log("Command: Right");
                break;
            case "Q":
                moveInput = Vector3.zero;
                rotationInput = -1f;
                pitchInput = 0f;
                Debug.Log("Command: Rotate Left");
                break;
            case "E":
                moveInput = Vector3.zero;
                rotationInput = 1f;
                pitchInput = 0f;
                Debug.Log("Command: Rotate Right");
                break;
            case "Z":
                moveInput = Vector3.zero;
                rotationInput = 0f;
                pitchInput = 1f;
                Debug.Log("Command: Look Down");
                break;
            case "3":
                moveInput = Vector3.zero;
                rotationInput = 0f;
                pitchInput = -1f;
                Debug.Log("Command: Look Up");
                break;
            case "STOP":
                moveInput = Vector3.zero;
                rotationInput = 0f;
                pitchInput = 0f;
                Debug.Log("Command: Stop");
                break;
            default:
                Debug.Log("Unknown command: " + cmd);
                break;
        }
    }

    void OnApplicationQuit()
    {
        isFrameClientConnected = false;
        isStatusClientConnected = false;
        
        if (renderCamera != null)
        {
            renderCamera.targetTexture = null;
        }
        
        if (receiveThread != null && receiveThread.IsAlive)
            receiveThread.Abort();
        if (sendThread != null && sendThread.IsAlive)
            sendThread.Abort();
        if (statusThread != null && statusThread.IsAlive)
            statusThread.Abort();
            
        if (commandListener != null)
            commandListener.Stop();
        if (frameListener != null)
            frameListener.Stop();
        if (statusListener != null)
            statusListener.Stop();
        if (frameStream != null)
            frameStream.Close();
        if (frameClient != null)
            frameClient.Close();
        if (statusStream != null)
            statusStream.Close();
        if (statusClient != null)
            statusClient.Close();
            
        if (renderTexture != null)
            renderTexture.Release();
    }
}