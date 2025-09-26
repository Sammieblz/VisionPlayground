import cv2
import pyautogui
import time
import numpy as np

# Load the cascade for the eye
eye_cascade = cv2.CascadeClassifier('haarcascade_eye.xml')

# Check if cascade file loaded successfully
if eye_cascade.empty():
    print("Error: Could not load haarcascade_eye.xml file")
    print("Please ensure the file is in the same directory as this script")
    exit(1)

# Load the input video
cap = cv2.VideoCapture(0)

# Check if camera opened successfully
if not cap.isOpened():
    print("Error: Could not open camera. Please check:")
    print("1. Camera is connected and not being used by another application")
    print("2. Camera permissions are granted")
    print("3. Try changing the camera index (0, 1, 2, etc.)")
    exit(1)

# OPTIMIZATION 1: Set camera properties for better performance
cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)   # Reduce resolution
cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)  # Reduce resolution
cap.set(cv2.CAP_PROP_FPS, 30)            # Set FPS

# OPTIMIZATION 2: Define variables
counter = 0
threshold = 15  # Reduced threshold for faster response
last_eye_state = "open"
frame_skip = 2  # Process every 2nd frame for better performance
frame_count = 0

# OPTIMIZATION 3: Pre-allocate arrays and variables
prev_eyes = []
roi_cache = {}

# OPTIMIZATION 4: Disable PyAutoGUI failsafe for better performance
pyautogui.FAILSAFE = False

# Start the media
print("Starting optimized media playback...")
pyautogui.press('play')
print("Media playback started. Press 'q' to quit.")

# OPTIMIZATION 5: Main loop with performance improvements
while True:
    # Capture a frame from the video
    ret, frame = cap.read()
    if not ret:
        break
    
    frame_count += 1
    
    # OPTIMIZATION 6: Skip frames for better performance
    if frame_count % frame_skip != 0:
        cv2.imshow('Eye Detection', frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break
        continue
    
    # OPTIMIZATION 7: Resize frame for faster processing
    small_frame = cv2.resize(frame, (320, 240))
    gray = cv2.cvtColor(small_frame, cv2.COLOR_BGR2GRAY)
    
    # OPTIMIZATION 8: Use optimized detection parameters
    eyes = eye_cascade.detectMultiScale(
        gray, 
        scaleFactor=1.1, 
        minNeighbors=3,  # Reduced from 4
        minSize=(20, 20),  # Minimum eye size
        maxSize=(80, 80),  # Maximum eye size
        flags=cv2.CASCADE_SCALE_IMAGE
    )
    
    # OPTIMIZATION 9: Scale back to original frame size
    scale_x = frame.shape[1] / 320
    scale_y = frame.shape[0] / 240
    eyes = [(int(x * scale_x), int(y * scale_y), int(w * scale_x), int(h * scale_y)) for (x, y, w, h) in eyes]
    
    # OPTIMIZATION 10: Process only the first 2 eyes (most likely to be actual eyes)
    eyes = eyes[:2]
    
    # OPTIMIZATION 11: Improved eye state detection
    current_eye_state = "open"
    closed_eyes = 0
    
    for (x, y, w, h) in eyes:
        # Draw a rectangle around the eye
        cv2.rectangle(frame, (x, y), (x+w, y+h), (255, 0, 0), 2)
        
        # OPTIMIZATION 12: Use cached ROI if possible
        roi_key = f"{x}_{y}_{w}_{h}"
        if roi_key not in roi_cache:
            roi_gray = gray[int(y/scale_y):int((y+h)/scale_y), int(x/scale_x):int((x+w)/scale_x)]
            if roi_gray.size > 0:
                roi_cache[roi_key] = roi_gray
            else:
                continue
        else:
            roi_gray = roi_cache[roi_key]
        
        # OPTIMIZATION 13: Use more efficient eye closure detection
        if roi_gray.size > 0:
            # Use median instead of mean for better noise resistance
            eye_brightness = np.median(roi_gray)
            if eye_brightness < 100:  # Adjusted threshold
                closed_eyes += 1
    
    # OPTIMIZATION 14: Determine overall eye state
    if len(eyes) > 0 and closed_eyes >= len(eyes) * 0.5:  # 50% of eyes closed
        current_eye_state = "closed"
        counter += 1
    else:
        current_eye_state = "open"
        counter = 0
    
    # OPTIMIZATION 15: Only print when state changes
    if current_eye_state != last_eye_state:
        print(f"{current_eye_state.title()} Eye")
        last_eye_state = current_eye_state
    
    # OPTIMIZATION 16: Media control with debouncing
    if counter >= threshold:
        pyautogui.press('pause')
        print("Media paused due to closed eyes")
        time.sleep(0.5)  # Reduced sleep time
        counter = 0  # Reset counter after action
    
    # OPTIMIZATION 17: Display frame with status
    status_text = f"Eyes: {current_eye_state.title()} | Counter: {counter}/{threshold}"
    cv2.putText(frame, status_text, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
    
    # Display the frame with the detected eyes
    cv2.imshow('Eye Detection - Optimized', frame)
    
    # Break the loop if the 'q' key is pressed
    if cv2.waitKey(1) & 0xFF == ord('q'):
        break

# Release the video capture and close all windows
cap.release()
cv2.destroyAllWindows()
print("Application closed.")
