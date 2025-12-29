function unity_control()
    % UNITY_CONTROL MATLAB client for controlling Unity and receiving video frames
    %
    % This function connects to Unity TCP servers for command sending (port 5000)
    % and frame receiving (port 5001). Use WASD or arrow keys to control the
    % Unity character and view the camera feed in real-time.
    
    % Clear any existing connections
    try
        clear all;
        close all;
        clc;
    catch
    end
    
    disp('=== MATLAB Unity Controller ===');
    disp('Initializing connection to Unity...');
    
    % Connect to Unity TCP servers
    try
        commandClient = tcpclient('127.0.0.1', 5000, 'Timeout', 5);
        frameClient = tcpclient('127.0.0.1', 5001, 'Timeout', 5);
        disp('Connected to Unity servers.');
        disp('  Command port: 5000');
        disp('  Frame port: 5001');
    catch e
        error('Failed to connect to Unity. Ensure Unity is running and TCPReceiver is active.\nError: %s', e.message);
    end
    
    % Create figure for display and key presses
    fig = figure('Position', [100, 100, 700, 550], ...
                 'KeyPressFcn', @keyDown, ...
                 'KeyReleaseFcn', @keyUp, ...
                 'Name', 'Unity Control - MATLAB Client', ...
                 'NumberTitle', 'off', ...
                 'MenuBar', 'none', ...
                 'ToolBar', 'none', ...
                 'Resize', 'off');
    
    % Create axes for video display
    ax = axes('Parent', fig, ...
              'Position', [0.05, 0.05, 0.9, 0.8], ...
              'XTick', [], 'YTick', [], ...
              'Box', 'on', ...
              'XColor', [0.3 0.3 0.3], ...
              'YColor', [0.3 0.3 0.3]);
    xlabel(ax, 'Unity Camera Feed', 'FontSize', 12, 'FontWeight', 'bold');
    
    % Status text
    statusText = uicontrol('Style', 'text', ...
                           'Parent', fig, ...
                           'Position', [10, 490, 680, 50], ...
                           'String', 'Status: Connected to Unity. Click here, then use WASD/Arrow keys.', ...
                           'FontSize', 10, ...
                           'HorizontalAlignment', 'center', ...
                           'BackgroundColor', [0.9 0.9 0.9]);
    
    imgHandle = [];
    lastKey = '';
    isRunning = true;
    frameCount = 0;
    startTime = tic;
    
    % Display initial message
    text(ax, 0.5, 0.5, 'Waiting for Unity frames...', ...
         'HorizontalAlignment', 'center', ...
         'VerticalAlignment', 'middle', ...
         'FontSize', 14, ...
         'Color', [0.5 0.5 0.5], ...
         'Units', 'normalized');
    axis(ax, 'off');
    
    % Key press function
    function keyDown(~, event)
        key = event.Key;
        lastKey = key;
        
        % Update status
        set(statusText, 'String', sprintf('Status: Key pressed: %s', key), ...
                        'BackgroundColor', [0.8 0.9 0.8]);
        
        try
            switch key
                case {'w', 'uparrow'}
                    write(commandClient, uint8('W'));
                    disp('Command: Forward (W)');
                case {'a', 'leftarrow'}
                    write(commandClient, uint8('A'));
                    disp('Command: Left (A)');
                case {'s', 'downarrow'}
                    write(commandClient, uint8('S'));
                    disp('Command: Back (S)');
                case {'d', 'rightarrow'}
                    write(commandClient, uint8('D'));
                    disp('Command: Right (D)');
                otherwise
                    set(statusText, 'String', sprintf('Status: Key %s ignored (use WASD/arrows)', key), ...
                                    'BackgroundColor', [0.95 0.95 0.7]);
            end
        catch e
            disp(['Error sending command: ' e.message]);
            set(statusText, 'String', 'Status: Error sending command', ...
                            'BackgroundColor', [1 0.8 0.8]);
        end
    end
    
    % Key release function
    function keyUp(~, ~)
        if ~isempty(lastKey)
            try
                write(commandClient, uint8('STOP'));
                disp('Command: STOP');
            catch e
                disp(['Error sending STOP: ' e.message]);
            end
            lastKey = '';
        end
        
        % Update status
        set(statusText, 'String', 'Status: Ready - Use WASD/Arrow keys to control', ...
                        'BackgroundColor', [0.9 0.9 0.9]);
    end
    
    % Close callback
    function closeRequest(~, ~)
        isRunning = false;
        set(statusText, 'String', 'Status: Shutting down...', ...
                        'BackgroundColor', [0.95 0.8 0.8]);
        
        % Send final STOP command
        try
            write(commandClient, uint8('STOP'));
        catch
        end
        
        % Close connections
        try
            clear commandClient frameClient;
        catch
        end
        
        % Calculate statistics
        elapsedTime = toc(startTime);
        if elapsedTime > 0
            fps = frameCount / elapsedTime;
            %fprintf('\n=== Session Statistics ===\n');
            %fprintf('Total frames received: %d\n', frameCount);
            %fprintf('Total time: %.2f seconds\n', elapsedTime);
            %fprintf('Average FPS: %.2f\n', fps);
        end
        
        delete(fig);
        disp('Unity control stopped.');
    end
    
    set(fig, 'CloseRequestFcn', @closeRequest);
    
    % Display control instructions
    fprintf('\n=== Control Instructions ===\n');
    fprintf('W / Up Arrow    : Move forward (camera view)\n');
    fprintf('A / Left Arrow  : Move left\n');
    fprintf('S / Down Arrow  : Move backward\n');
    fprintf('D / Right Arrow : Move right\n');
    fprintf('Release any key : Stop movement\n');
    fprintf('Close window    : Exit program\n');
    fprintf('============================\n\n');
    
    disp('MATLAB Unity Controller Ready.');
    disp('Click the figure window, then use WASD or arrow keys to control.');
    
    % Main loop - receive and display frames
    lastFrameTime = tic;
    connectionRetries = 0;
    maxRetries = 5;
    
    while isRunning && isvalid(fig)
        try
            % Check if data is available
            if frameClient.NumBytesAvailable >= 4
                % Read frame size (4 bytes)
                frameSizeBytes = read(frameClient, 4, 'uint8');
                frameSize = typecast(uint8(frameSizeBytes), 'int32');
                
                % Validate frame size
                if frameSize <= 0 || frameSize > 10e6 % 10 MB max
                    disp(['Invalid frame size: ' num2str(frameSize) ' bytes']);
                    continue;
                end
                
                % Wait for full frame to arrive with timeout
                timeout = tic;
                while frameClient.NumBytesAvailable < frameSize && isRunning
                    if toc(timeout) > 2.0 % 2 second timeout
                        error('Frame reception timeout');
                    end
                    pause(0.001);
                end
                
                if frameClient.NumBytesAvailable >= frameSize
                    % Read frame data
                    frameData = read(frameClient, frameSize, 'uint8');
                    
                    % Decode JPEG
                    try
                        % Decode JPEG from bytes
                        tempFile = tempname;
                        fid = fopen(tempFile, 'wb');
                        fwrite(fid, frameData, 'uint8');
                        fclose(fid);
                        
                        img = imread(tempFile);
                        delete(tempFile);
                        
                        % Resize to standard dimensions
                        img = imresize(img, [480, 640]);
                        
                        % Display image
                        if isempty(imgHandle) || ~isvalid(imgHandle)
                            imgHandle = imshow(img, 'Parent', ax);
                            axis(ax, 'off');
                            title(ax, 'Unity Camera Feed', 'FontSize', 12);
                        else
                            set(imgHandle, 'CData', img);
                        end
                        
                        % Update frame count and status
                        frameCount = frameCount + 1;
                        frameTime = toc(lastFrameTime);
                        lastFrameTime = tic;
                        
                        if frameCount > 1
                            currentFPS = 1 / frameTime;
                            set(statusText, 'String', sprintf('Status: %d frames received | FPS: %.1f | Use WASD/Arrows', ...
                                frameCount, currentFPS), ...
                                'BackgroundColor', [0.9 0.95 0.95]);
                        end
                        
                        drawnow limitrate;
                        
                    catch imgErr
                        disp(['Image decode error: ' imgErr.message]);
                        
                        % Show placeholder image on error
                        if isempty(imgHandle) || ~isvalid(imgHandle)
                            placeholder = zeros(480, 640, 3, 'uint8');
                            imgHandle = imshow(placeholder, 'Parent', ax);
                            axis(ax, 'off');
                        end
                    end
                end
            else
                % No data available, brief pause
                pause(0.01);
            end
            
            connectionRetries = 0; % Reset retry counter on successful iteration
            
        catch e
            if isRunning
                disp(['Frame error: ' e.message]);
                
                % Try to reconnect if connection was lost
                connectionRetries = connectionRetries + 1;
                
                if connectionRetries <= maxRetries
                    set(statusText, 'String', sprintf('Status: Reconnecting... (Attempt %d/%d)', ...
                        connectionRetries, maxRetries), ...
                        'BackgroundColor', [1 0.9 0.7]);
                    
                    try
                        % Close and recreate frame client
                        clear frameClient;
                        pause(1); % Wait before reconnecting
                        frameClient = tcpclient('127.0.0.1', 5001, 'Timeout', 5);
                        disp('Reconnected to Unity frame server');
                        set(statusText, 'String', 'Status: Reconnected - Ready', ...
                            'BackgroundColor', [0.9 0.9 0.9]);
                    catch reconnectErr
                        disp(['Reconnect failed: ' reconnectErr.message]);
                    end
                else
                    set(statusText, 'String', 'Status: Max reconnection attempts reached', ...
                        'BackgroundColor', [1 0.8 0.8]);
                    pause(0.5);
                end
                pause(0.1);
            end
        end
    end
    
    % Cleanup
    try
        clear commandClient frameClient;
    catch
    end
    
    % Clean up any temporary files
    tempFiles = dir([tempdir '*.jpg']);
    for i = 1:min(10, length(tempFiles)) % Clean up at most 10 temp files
        try
            delete(fullfile(tempdir, tempFiles(i).name));
        catch
        end
    end
    
    disp('Unity control session ended.');
    
    % Display final statistics if we have data
    elapsedTime = toc(startTime);
    if elapsedTime > 0 && frameCount > 0
        fprintf('\n=== Final Statistics ===\n');
        fprintf('Total frames received: %d\n', frameCount);
        fprintf('Session duration: %.2f seconds\n', elapsedTime);
        fprintf('Average FPS: %.2f\n', frameCount / elapsedTime);
        fprintf('========================\n');
    end
end