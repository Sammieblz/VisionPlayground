import cv2
import time
import numpy as np

def test_original_detection():
    """Test the original detection method"""
    print("Testing original detection method...")
    
    # Load cascade
    eye_cascade = cv2.CascadeClassifier('haarcascade_eye.xml')
    cap = cv2.VideoCapture(0)
    
    if not cap.isOpened():
        print("Camera not available for testing")
        return
    
    # Set original parameters
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    
    frame_times = []
    detection_times = []
    
    for i in range(30):  # Test 30 frames
        start_time = time.time()
        
        ret, frame = cap.read()
        if not ret:
            break
            
        frame_start = time.time()
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        eyes = eye_cascade.detectMultiScale(gray, 1.1, 4)
        detection_time = time.time() - frame_start
        
        frame_time = time.time() - start_time
        
        frame_times.append(frame_time)
        detection_times.append(detection_time)
    
    cap.release()
    
    avg_frame_time = np.mean(frame_times)
    avg_detection_time = np.mean(detection_times)
    fps = 1.0 / avg_frame_time
    
    print(f"Original Method:")
    print(f"  Average frame time: {avg_frame_time:.4f}s")
    print(f"  Average detection time: {avg_detection_time:.4f}s")
    print(f"  Estimated FPS: {fps:.2f}")
    
    return avg_frame_time, avg_detection_time, fps

def test_optimized_detection():
    """Test the optimized detection method"""
    print("\nTesting optimized detection method...")
    
    # Load cascade
    eye_cascade = cv2.CascadeClassifier('haarcascade_eye.xml')
    cap = cv2.VideoCapture(0)
    
    if not cap.isOpened():
        print("Camera not available for testing")
        return
    
    # Set optimized parameters
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    cap.set(cv2.CAP_PROP_FPS, 30)
    
    frame_times = []
    detection_times = []
    frame_skip = 2
    frame_count = 0
    
    for i in range(30):  # Test 30 frames
        start_time = time.time()
        
        ret, frame = cap.read()
        if not ret:
            break
        
        frame_count += 1
        
        # Skip frames for optimization
        if frame_count % frame_skip != 0:
            frame_time = time.time() - start_time
            frame_times.append(frame_time)
            continue
        
        frame_start = time.time()
        
        # Optimized processing
        small_frame = cv2.resize(frame, (320, 240))
        gray = cv2.cvtColor(small_frame, cv2.COLOR_BGR2GRAY)
        
        eyes = eye_cascade.detectMultiScale(
            gray, 
            scaleFactor=1.1, 
            minNeighbors=3,
            minSize=(20, 20),
            maxSize=(80, 80),
            flags=cv2.CASCADE_SCALE_IMAGE
        )
        
        detection_time = time.time() - frame_start
        frame_time = time.time() - start_time
        
        frame_times.append(frame_time)
        detection_times.append(detection_time)
    
    cap.release()
    
    avg_frame_time = np.mean(frame_times)
    avg_detection_time = np.mean(detection_times)
    fps = 1.0 / avg_frame_time
    
    print(f"Optimized Method:")
    print(f"  Average frame time: {avg_frame_time:.4f}s")
    print(f"  Average detection time: {avg_detection_time:.4f}s")
    print(f"  Estimated FPS: {fps:.2f}")
    
    return avg_frame_time, avg_detection_time, fps

if __name__ == "__main__":
    print("Performance Comparison Test")
    print("=" * 40)
    
    # Test original method
    orig_frame, orig_det, orig_fps = test_original_detection()
    
    # Test optimized method
    opt_frame, opt_det, opt_fps = test_optimized_detection()
    
    # Calculate improvements
    if orig_frame and opt_frame:
        print("\n" + "=" * 40)
        print("PERFORMANCE IMPROVEMENTS:")
        print(f"Frame time improvement: {((orig_frame - opt_frame) / orig_frame * 100):.1f}%")
        print(f"Detection time improvement: {((orig_det - opt_det) / orig_det * 100):.1f}%")
        print(f"FPS improvement: {((opt_fps - orig_fps) / orig_fps * 100):.1f}%")
        print(f"Speed multiplier: {orig_frame / opt_frame:.2f}x faster")
