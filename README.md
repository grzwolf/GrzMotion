# GrzMotion
Motion detection with RTSP or UVC camera for Windows x64 >= version 7.  

GrzMotion is a continuation of GrzMotionUVC.
While image processing and UVC handling are almost the same, image source RTSP is now available.


Notes:

1.) Accord.Video.FFMPEG: needs both VC_redist.x86.exe and VC_redist.x64.exe installed on target PC

2.) If reference to AForge.Video.DirectShow is not updated automatically. 
    
    Reference to AForge.Video.DirectShow must be set manually in Solution Explorer:
    
    - GrzMotion --> References --> Add Reference --> Browse --> ..\Video.DirectShow\bin\x64\Debug
    
    - select AForge.Video.DirectShow.dll --> Add
    
