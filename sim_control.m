function sim_control()
 
    try
        clear all;
        close all;
        clc;
    catch
    end
    
    disp('=== MATLAB Train Simulation===');
    disp('Initializing simulation environment...');
    
    % Connect to Unity TCP servers
    try
        commandClient = tcpclient('127.0.0.1', 5000, 'Timeout', 5);
        frameClient = tcpclient('127.0.0.1', 5001, 'Timeout', 5);
        
        % OPTIMIZATION: Configure TCP for low latency
        configureCallback(frameClient, "off");
        
        disp('Connected to Simulation servers.');
        disp('  Command port: 5000');
        disp('  Output port: 5001');
    catch e
        error('Failed to connect to Simulation Server. Ensure Unity is running.\nError: %s', e.message);
    end
    
    % Create figure
    fig = figure('Position', [100, 100, 700, 550], ...
                 'KeyPressFcn', @keyDown, ...
                 'KeyReleaseFcn', @keyUp, ...
                 'Name', 'Smart Train Simulation', ...
                 'NumberTitle', 'off', ...
                 'MenuBar', 'none', ...
                 'ToolBar', 'none', ...
                 'Resize', 'off');
    
    % Create axes
    ax = axes('Parent', fig, ...
              'Position', [0.05, 0.05, 0.9, 0.8], ...
              'XTick', [], 'YTick', [], ...
              'Box', 'on');
    
    % Status text
    statusText = uicontrol('Style', 'text', ...
                           'Parent', fig, ...
                           'Position', [10, 490, 680, 50], ...
                           'String', 'Status: Connected. Use WASD/Arrow/QE keys.', ...
                           'FontSize', 10, ...
                           'HorizontalAlignment', 'center', ...
                           'BackgroundColor', [0 0 0]);
    
    imgHandle = [];
    lastKey = '';
    isRunning = true;
    frameCount = 0;
    skippedFrames = 0;
    startTime = tic;
    lastFrameTime = tic;
    
    % Display initial message
    text(ax, 0.5, 0.5, 'Waiting for Simulation Engine...', ...
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
        
        set(statusText, 'String', sprintf('Status: Key pressed: %s', key), ...
                        'BackgroundColor', [0 0 0]);
        
        try
            switch key
                case {'w', 'uparrow'}
                    write(commandClient, uint8('W'));
                case {'a', 'leftarrow'}
                    write(commandClient, uint8('A'));
                case {'s', 'downarrow'}
                    write(commandClient, uint8('S'));
                case {'d', 'rightarrow'}
                    write(commandClient, uint8('D'));
                case 'q'
                    write(commandClient, uint8('Q'));
                case 'e'
                    write(commandClient, uint8('E'));
                otherwise
                    set(statusText, 'String', sprintf('Status: Key %s ignored', key), ...
                                    'BackgroundColor', [0 0 0]);
            end
        catch e
            disp(['Error sending command: ' e.message]);
        end
    end
    
    % Key release function
    function keyUp(~, ~)
        if ~isempty(lastKey)
            try
                write(commandClient, uint8('STOP'));
            catch e
                disp(['Error sending STOP: ' e.message]);
            end
            lastKey = '';
        end
        
        set(statusText, 'String', 'Status: Ready - Use WASD/Arrow/QE keys', ...
                        'BackgroundColor', [0 0 0]);
    end
    
    % Close callback
    function closeRequest(~, ~)
        isRunning = false;
        
        try
            write(commandClient, uint8('STOP'));
        catch
        end
        
        try
            clear commandClient frameClient;
        catch
        end
        
        delete(fig);
        
        % Display statistics
        elapsedTime = toc(startTime);
        if elapsedTime > 0 && frameCount > 0
           fprintf('\n=== Session Ended ===\n');
            fprintf('========================\n');
        end
        
        disp('Simulation stopped.');
    end
    
    set(fig, 'CloseRequestFcn', @closeRequest);
    
    % Display instructions
    fprintf('\n=== Control Instructions ===\n');
    fprintf('W / Up    : Forward\n');
    fprintf('A / Left  : Left\n');
    fprintf('S / Down  : Backward\n');
    fprintf('D / Right : Right\n');
    fprintf('Q         : Rotate Left\n');
    fprintf('E         : Rotate Right\n');
    fprintf('============================\n\n');
    
    disp('Ready. Click window and use WASD/arrows/QE.');
    
    % OPTIMIZATION: Pre-allocate buffer for frame size reading
    frameSizeBuffer = zeros(1, 4, 'uint8');
    
    % Main loop - OPTIMIZED for low latency
    while isRunning && isvalid(fig)
        try
            % OPTIMIZATION: Skip frames if we're behind
            availableBytes = frameClient.NumBytesAvailable;
            
            if availableBytes >= 4
                % Quick check: how many frames are waiting?
                numFramesWaiting = 0;
                tempAvailable = availableBytes;
                
                % Try to estimate number of complete frames in buffer
                while tempAvailable >= 4
                    break;
                end
                
                % Read frame size
                frameSizeBytes = read(frameClient, 4, 'uint8');
                frameSize = typecast(uint8(frameSizeBytes), 'int32');
                
                % Validate
                if frameSize <= 0 || frameSize > 10e6
                    disp(['Invalid frame size: ' num2str(frameSize)]);
                    if frameClient.NumBytesAvailable > 0
                        flush(frameClient);
                    end
                    continue;
                end
                
                % OPTIMIZATION: Check if there are more frames waiting
                if frameClient.NumBytesAvailable > frameSize + 4
                    if frameClient.NumBytesAvailable >= frameSize
                        discard = read(frameClient, frameSize, 'uint8'); %#ok<NASGU>
                        skippedFrames = skippedFrames + 1;
                        continue;
                    end
                end
                
                % Wait for this frame with timeout
                timeout = tic;
                while frameClient.NumBytesAvailable < frameSize && isRunning
                    if toc(timeout) > 1.0
                        error('Frame reception timeout');
                    end
                    pause(0.001);
                end
                
                if frameClient.NumBytesAvailable >= frameSize
                    % Read frame data
                    frameData = read(frameClient, frameSize, 'uint8');
                    
                    % OPTIMIZATION: Decode JPEG in memory (no temp file)
                    try
                        % Create Java ByteArrayInputStream for in-memory decoding
                        javaBytes = typecast(frameData, 'int8');
                        byteStream = java.io.ByteArrayInputStream(javaBytes);
                        img = javax.imageio.ImageIO.read(byteStream);
                        
                        if ~isempty(img)
                            % Convert Java BufferedImage to MATLAB matrix
                            h = img.getHeight();
                            w = img.getWidth();
                            
                            % Get pixel data
                            pixelData = reshape(typecast(img.getData().getDataStorage(), 'uint8'), [3, w, h]);
                            img_matlab = permute(pixelData, [3 2 1]);
                            
                            % FIX RED-BLUE SHIFT: Convert BGR to RGB
                            img_matlab = img_matlab(:, :, [3 2 1]);
                            
                            % Display
                            if isempty(imgHandle) || ~isvalid(imgHandle)
                                imgHandle = imshow(img_matlab, 'Parent', ax);
                                axis(ax, 'off');
                                title(ax, 'Smart Train Simulation', 'FontSize', 12);
                            else
                                set(imgHandle, 'CData', img_matlab);
                            end
                            
                            % Update stats
                            frameCount = frameCount + 1;
                            frameTime = toc(lastFrameTime);
                            lastFrameTime = tic;
           
                            drawnow limitrate;
                        end
                        
                    catch imgErr
                        % Fallback to file-based decoding if Java method fails
                        try
                            tempFile = [tempname '.jpg'];
                            fid = fopen(tempFile, 'wb');
                            fwrite(fid, frameData, 'uint8');
                            fclose(fid);
                            
                            img = imread(tempFile);
                            delete(tempFile);
                            
                            if isempty(imgHandle) || ~isvalid(imgHandle)
                                imgHandle = imshow(img, 'Parent', ax);
                                axis(ax, 'off');
                            else
                                set(imgHandle, 'CData', img);
                            end
                            
                            frameCount = frameCount + 1;
                            drawnow limitrate;
                        catch
                            disp(['Image decode error: ' imgErr.message]);
                        end
                    end
                end
            else
                % No data, brief pause
                pause(0.005);
            end
            
        catch e
            if isRunning
                disp(['Frame error: ' e.message]);
                pause(0.1);
            end
        end
    end
    
    % Cleanup
    try
        clear commandClient frameClient;
    catch
    end
    
    disp('Simulation session ended.');
end