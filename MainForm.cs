﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
// AForge download packages misses: SetVideoProperty, GetVideoProperty, GetVideoPropertyRange --> no brightness setting possible
// fix: http://www.aforgenet.com/forum/viewtopic.php?f=2&t=2939
using AForge.Video.DirectShow;
using System.Globalization;
using System.IO;
using TeleSharp.Entities;  
using TeleSharp.Entities.SendEntities;
using System.Threading;
using System.Threading.Tasks;
// Accord.Video.FFMPEG: !! needs both VC_redist.x86.exe and VC_redist.x64.exe installed on target PC !!
using Accord.Video.FFMPEG;
using static GrzMotion.AppSettings;
using GrzTools;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Linq;
using System.Drawing.Design;
using RestSharp;
using File = System.IO.File;
// !! @compile time: needs "cvextern.dll" (not recognized as VS2019 reference) manually copied + via xcopy after build to release & debug folders !!
// !! @run time    : H264 needs codec library openh264-2.3.1-win64.dll on stock Windows 10 to be copied to app folder !!
using Emgu.CV;
using Logging;
using RtspInfra;

namespace GrzMotion
{
    public partial class MainForm : Form, IMessageFilter {
                
        public class oneROI {                                                // class to define a single 'Region Of Interest' = ROI
            public Rectangle rect { get; set; }                              // monitor area  
            public int thresholdIntensity { get; set; }                      // pixel gray value threshold considered as a potential motion
            public double thresholdChanges { get; set; }                     // percentage of pixels in a ROI considered as a potential motion
            public double thresholdUpperLimit { get; set; }                  // if percentage of pixels in a ROI is exceeded, it's considered false positive
            public bool reference { get; set; }                              // reference ROI to exclude false positive motions
            public int boxScaler { get; set; }                               // consecutive pixels in a box   
        };
        public static int ROICOUNT = 10;                                     // max ROI count
        List<oneROI> _roi = new List<oneROI>();                              // list containing ROIs

        public static AppSettings Settings = new AppSettings();              // app settings

        Size SourceResolution = new Size();                                  // either UVC camera resolution or RTSP stream resolution

        private FilterInfoCollection _uvcDevices;                            // AForge collection of UVC camera devices
        private VideoCaptureDevice _uvcDevice = null;                        // AForge UVC camera device
        private int _uvcDeviceRestartCounter = 0;                            // video device restart counter per app session
        
        private RtspControl _rtspStream;                                     // RTSP stream infra 
        private Thread _rtspStreamThread = null;                             // a separate thread as a measure of last resort to abort a hanging RTSP client 
        private int _rtspDeviceExceptionCounter = 0;                         // device Exception counter

        private DateTime _appStartTime = DateTime.Now;                       // app start time is used to reset Settings.RtspRestartAppCount

        VideoCaptureDevice _uvcDeviceSnap = null;                            // AForge UVC camera device for snapshots, only if in RTSP mode
        bool _uvcDeviceSnapIsBusy = false;                                   // avoid snapshot single shot overrun

        private string _buttonConnectString;                                 // original text on camera start button
                                                                             
        static Bitmap _origFrame = null;                                     // current camera frame original
        public static Bitmap _currFrame = null;                              // current camera scaled frame (typically 800 x 600)
        static Bitmap _procFrame = null;                                     // current camera scaled processed frame
        static Bitmap _prevFrame = null;                                     // previous camera scaled frame
        static double _frameAspectRatio = 1.3333f;                           // default value until it is overridden via 'firstImageProcessing' in grabber 

        string _nowStringFile = "";
        string _nowStringPath = "";
        Font   _timestampFont = new Font("Arial", 20, FontStyle.Bold, GraphicsUnit.Pixel);

        PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available MBytes");

        public class Motion {                                                // helper to have a motion list other than the stored files on disk
            public String fileNameMotion;
            public String fileNameProc;
            public DateTime motionDateTime;
            public Bitmap imageMotion;
            public Bitmap imageProc;
            public bool motionSaved;
            public bool motionConsecutive { get; set; }
            public bool bitmapLocked { get; set; }
            public Motion(String fileNameMotion, DateTime motionDateTime) {
                this.fileNameMotion = fileNameMotion;
                this.fileNameProc = "";
                this.motionDateTime = motionDateTime;
                this.motionConsecutive = false;
                this.imageMotion = null;
                this.imageProc = null;
                this.motionSaved = true;
                this.bitmapLocked = false;
            }
            public Motion(String fileNameMotion, DateTime motionDateTime, Bitmap image, String fileNameProc, Bitmap imageProc) {
                this.fileNameMotion = fileNameMotion;
                this.fileNameProc = fileNameProc;
                this.motionDateTime = motionDateTime;
                this.motionConsecutive = false;
                this.imageMotion = (Bitmap)image.Clone();
                this.imageProc = imageProc != null ? (Bitmap)imageProc.Clone() : null;
                this.motionSaved = false;
                this.bitmapLocked = false;
            }
        }
        List<Motion> _motionsList = new List<Motion>();                      // list of Motion, which are motion sequences, if 'consecutive' is true

        static int _motionsDetected = 0;                                     // all motions detection counter
        static int _consecutivesDetected = 0;                                // consecutive motions counter
        System.Timers.Timer _timerMotionSequenceActive = null;               // timer is active for 1s, after a motion sequence was detected, used to overvote a false positive mation
        string _strOverVoteFalsePositive = "";                               // global string, it's either "" or "o" 
        static bool _justConnected = false;                                  // just connected
                                                                              
        static int TWO_FPS = 490;                                            // ensure two fps
        static double _fps = 0;                                              // current frame rate 
        static long _procMs = 0;                                             // current process time
        static long _procMsMin = long.MaxValue;                              // min process time
        static long _procMsMax = 0;                                          // max process time
        static long _proc450Ms = 0;                                          // count number of process time >450ms

        Size _sizeBeforeResize;                                              // MainForm size before a change was made by User

        double BRIGHTNESS_CHANGE_THRESHOLD = 10.0f;                          // experimental: camera exposure control thru app  
        int _brightnessNoChangeCounter = 0;                                  // experimental: camera no brightness change counter

        TimeSpan _midNight = new System.TimeSpan(0, 0, 0);                   // magic times 
        TimeSpan _videoTime = new System.TimeSpan(19, 0, 0);
        public static TimeSpan BootTimeBeg = new System.TimeSpan(0, 30, 0);
        public static TimeSpan BootTimeEnd = new System.TimeSpan(0, 31, 0);
        int _dailyVideoErrorCount = 0;                                       // make video error counter to prevent loops
        bool _dailyVideoInProgress = false;                                  // make video in progress flag
                
        TeleSharp.TeleSharp _Bot = null;                                     // Telegram bot  
        bool _alarmSequence = false;
        bool _alarmSequenceAsap = false;
        bool _alarmSequenceBusy = false;
        bool _alarmNotify = false;                                           // sends all motions (SaveMotions) or a sequence photo every 60 consecutives (SaveSequence)
        static string _notifyText = "";
        DateTime _lastSequenceSendTime = new DateTime();                     // limit video/photo sequence send cadence 
        bool _sendVideo = false;
        MessageSender _notifyReceiver = null;
        MessageSender _sequenceReceiver = null;
        DateTime _connectionLiveTick = DateTime.Now;
        int _telegramOnErrorCount = 0;
        int _telegramLiveTickErrorCount = 0;
        int _telegramRestartCounter = 0;
        bool _runPing = false;
        List<string> telegramMasterMessageCache = new List<string>();        // msg collector, sent latest when connection is established

        long ONE_GB =  1000000000;                                           // constants for file delete  
        long TWO_GB =  2000000000;                                            
        long TEN_GB = 10000000000;

        Font _pctFont = new Font("Arial", 15, FontStyle.Bold, GraphicsUnit.Pixel);

        // current image renderer
        private ImageBox PictureBox;

        public MainForm() {
            // form designer standard init
            InitializeComponent();

            // timer indicates an active motion sequence
            _timerMotionSequenceActive = new System.Timers.Timer(1000);
            _timerMotionSequenceActive.AutoReset = false;               // 'AutoReset = false' let timer expire after 1s --> single shot

            // avoid empty var
            _sizeBeforeResize = this.Size;

            // subclassed ImageBox handles the 'red cross exception' 
            // !! couldn't find a way to make this class accessible thru designer in x64 via toolbox (exception thrown when dragging to form) !! (Any CPU + x86 ok)
            this.PictureBox = new ImageBox();
            this.PictureBox.BackColor = System.Drawing.SystemColors.ActiveBorder;
            this.PictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PictureBox.Margin = new System.Windows.Forms.Padding(0);
            this.PictureBox.Size = new System.Drawing.Size(796, 492);
            this.PictureBox.TabIndex = 0;
            this.PictureBox.TabStop = false;
            this.tableLayoutPanelGraphs.Controls.Add(this.PictureBox, 0, 0);

            // prevent flickering when paint: https://stackoverflow.com/questions/24910574/how-to-prevent-flickering-when-using-paint-method-in-c-sharp-winforms  
            Control ctrl = this.tableLayoutPanelGraphs;
            ctrl.GetType()
                .GetProperty("DoubleBuffered",
                             System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(this.tableLayoutPanelGraphs, true, null);
            ctrl = this.PictureBox;
            ctrl.GetType()
                .GetProperty("DoubleBuffered",
                             System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(this.PictureBox, true, null);

            // memorize the initial camera connect button text
            _buttonConnectString = this.connectButton.Text;

            // add "about entry" to app's system menu
            SetupSystemMenu();

            // distinguish between 'forced reboot app start after ping fail' and a 'regular app start'
            AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            if ( bool.Parse(ini.IniReadValue("GrzMotion", "RebootPingFlagActive", "False")) ) {
                // app start due to a app forced reboot after ping fail
                ini.IniWriteValue("GrzMotion", "RebootPingFlagActive", "False");
            } else {
                // if app was started regular, reset the ping reboot counter
                ini.IniWriteValue("GrzMotion", "RebootPingCounter", "0");
            }

            // get settings from INI
            Settings.fillPropertyGridFromIni();

            // log start
            Settings.WriteLogfile = bool.Parse(ini.IniReadValue("GrzMotion", "WriteLogFile", "False"));
            Logger.WriteToLog = Settings.WriteLogfile;
            string path = ini.IniReadValue("GrzMotion", "StoragePath", Application.StartupPath + "\\");
            if ( !path.EndsWith("\\") ) {
                path += "\\";
            }
            Logger.FullFileNameBase = path + Path.GetFileName(Application.ExecutablePath);
            Logger.logTextU("\r\n---------------------------------------------------------------------------------------------------------------------------\r\n");
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            Logger.logTextLnU(DateTime.Now, String.Format("{0} {1}", assembly.FullName, fvi.FileVersion));
            // distinguish between regular app start and a restart after app crash
            if ( bool.Parse(ini.IniReadValue("GrzMotion", "AppCrash", "False")) ) {
                // distinguish between app crash and OS crash + store msg in telegramMasterMessageCache 
                if ( (DateTime.Now - getOsBootTime()).TotalMinutes < 15 ) {
                    Logger.logTextLnU(DateTime.Now, "App was restarted after unscheduled OS reboot");
                    telegramMasterMessageCache.Add("app restart after unscheduled OS reboot");
                } else {
                    Logger.logTextLnU(DateTime.Now, "App was restarted after crash");
                    telegramMasterMessageCache.Add("app restart after crash");
                }
            } else {
                Logger.logTextLn(DateTime.Now, "App start regular");
            }

            // before processing, images will be scaled down to a smaller image size
            Settings.ScaledImageSize = new Size(800, 600);                               

            // IMessageFilter - an encapsulated message filter
            // - also needed: class declaration "public partial class MainForm: Form, IMessageFilter"
            // - also needed: event handler "public bool PreFilterMessage( ref Message m )"
            // - also needed: Application.RemoveMessageFilter(this) when closing this form
            Application.AddMessageFilter(this);
        }

        // MainForm is loaded before it is shown
        private void MainForm_Load(object sender, EventArgs e) {
            // check for UVC and RTSP devices
            getCameraBasics();
            EnableConnectionControls(true);
        }

        // called when MainForm is finally shown
        private void MainForm_Shown(object sender, EventArgs e) {

//#if DEBUG
//            AutoMessageBox.Show("Ok to start session", "DEBUG Session", 60000);
//#endif

            AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            // from now on assume an app crash as default behavior: this flag is reset to False, if app closes the normal way
            ini.IniWriteValue("GrzMotion", "AppCrash", "True");
            // set app properties according to settings; in case ini craps out, delete it and begin from scratch with defaults
            try {
                updateAppPropertiesFromSettings();
            } catch {
                System.IO.File.Delete(System.Windows.Forms.Application.ExecutablePath + ".ini");
                Settings.fillPropertyGridFromIni();
            }
        }

        // update app from settings
        void updateAppPropertiesFromSettings() {

            // check for UVC and RTSP devices
            getCameraBasics();

            // UI app layout
            this.Size = Settings.FormSize;
            // get all display ranges (multiple monitors) and check, if desired location fits in
            Rectangle dispRange = new Rectangle(0, 0, 0, int.MaxValue);
            foreach ( Screen sc in Screen.AllScreens ) {
                dispRange.X = Math.Min(sc.Bounds.X, dispRange.X);
                dispRange.Width += sc.Bounds.Width;
                dispRange.Height = Math.Min(sc.Bounds.Height, dispRange.Height);
            }
            dispRange.X -= Settings.FormSize.Width / 2;
            dispRange.Height -= Settings.FormSize.Height / 2;
            if ( !dispRange.Contains(Settings.FormLocation) ) {
                Settings.FormLocation = new Point(100, 100);
            }
            this.Location = Settings.FormLocation;
            // UI exposure controls
            this.hScrollBarExposure.Minimum = Settings.ExposureMin;
            this.hScrollBarExposure.Maximum = Settings.ExposureMax;
            this.hScrollBarExposure.Value = Settings.ExposureVal;
            this.toolTip.SetToolTip(this.hScrollBarExposure, "camera exposure time = " + Settings.ExposureVal.ToString() + " (" + this.hScrollBarExposure.Minimum.ToString() + ".." + this.hScrollBarExposure.Maximum.ToString() + ")");
            // write to logfile
            Logger.WriteToLog = Settings.WriteLogfile;
            if ( !Settings.StoragePath.EndsWith("\\") ) {
                Settings.StoragePath += "\\";
            }
            Logger.FullFileNameBase = Settings.StoragePath + Path.GetFileName(Application.ExecutablePath);
            // get ROI motion zones
            _roi = Settings.getROIsListFromPropertyGrid();
            // handle Telegram bot usage
            System.Net.NetworkInformation.PingReply reply = execPing(Settings.PingTestAddress);
            if ( reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success ) {
                Settings.PingOk = true;
                Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: ping ok");
            } else {
                Settings.PingOk = false;
                Logger.logTextLnU(DateTime.Now, "updateAppPropertiesFromSettings: ping failed");
                if ( Settings.UseTelegramBot ) {
                    if ( _Bot == null ) {
                        Logger.logTextLnU(DateTime.Now, "updateAppPropertiesFromSettings: Telegram not activated due to ping fail");
                    } else {
                        // it might happen, that timerFlowControl jumped in earlier
                        Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: Telegram obviously activated by timerFlowControl");
                        TelegramSendMasterMessage("Telegram obviously activated by timerFlowControl");
                    }
                }
            }
            if ( Settings.PingOk ) {
                // could be, that Telegram was recently enabled in Settings, but don't activate it, if restart count is already too large
                if ( Settings.UseTelegramBot && Settings.TelegramRestartAppCount < 5 ) {
                    if ( _Bot == null ) {
                        _Bot = new TeleSharp.TeleSharp(Settings.BotAuthenticationToken);
                        _Bot.OnMessage += OnMessage;
                        _Bot.OnError += OnError;
                        _Bot.OnLiveTick += OnLiveTick;
                        this.timerCheckTelegramLiveTick.Start();
                        Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: Telegram bot activated");
                        // send cached master messages
                        while ( telegramMasterMessageCache.Count > 0 ) {
                            TelegramSendMasterMessage(telegramMasterMessageCache[0]);
                            telegramMasterMessageCache.RemoveAt(0);
                        }
                        TelegramSendMasterMessage("Telegram bot activated");
                    } else {
                        Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: Telegram is already active");
                        TelegramSendMasterMessage("Telegram bot was already active");
                    }
                    // restart alarm notify if previously enabled
                    _alarmNotify = false;
                    _notifyReceiver = new MessageSender();
                    _notifyReceiver.Id = -1;
                    _notifyText = "";
                    if ( Settings.KeepTelegramNotifyAction ) {
                        if ( Settings.KeepTelegramNotifyAction && Settings.TelegramNotifyReceiver <= 0 ) {
                            Logger.logTextLnU(DateTime.Now, "updateAppPropertiesFromSettings: 'TelegramNotifyReceiver' is not valid");
                            AutoMessageBox.Show("The 'TelegramNotifyReceiver' is not valid, alarm notification won't work unless it is changed.", "Note", 5000);
                        } else {
                            _alarmNotify = true;
                            _notifyReceiver.Id = Settings.TelegramNotifyReceiver;
                            _notifyText = " - permanent alarm notification active";
                        }
                    }
                } else {
                    if ( Settings.TelegramRestartAppCount >= 5 ) {
                        Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: Telegram not activated due to app restart limit");
                    } else {
                        Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: Telegram not activated");
                    }
                }
            }
            // could be, that Telegram was recently disabled in Settings
            if ( !Settings.UseTelegramBot ) {
                if ( _Bot != null ) {
                    _Bot.OnMessage -= OnMessage;
                    _Bot.OnError -= OnError;
                    _Bot.OnLiveTick -= OnLiveTick;
                    _Bot.Stop();
                    this.timerCheckTelegramLiveTick.Stop();
                    _Bot = null;
                    // if Telegram is actively disabled, disable permanent alarm notification too
                    Settings.KeepTelegramNotifyAction = false;
                    Settings.TelegramNotifyReceiver = -1;
                    _notifyText = "";
                    Logger.logTextLn(DateTime.Now, "updateAppPropertiesFromSettings: Telegram bot deactivated");
                }
            }
            // ping monitoring in a UI-thread separated task, which is a loop !! overrides Settings.PingOk !!
            Settings.PingTestAddressRef = Settings.PingTestAddress;
            if ( !_runPing ) {
                _runPing = true;
                Task.Run(() => { doPingLooper(ref _runPing, ref Settings.PingTestAddressRef); });
            }
            // no matter what, set operation mode resolution
            this.SourceResolution = 
                Settings.ImageSource == ImageSourceType.UVC ? Settings.CameraResolution : 
                Settings.ImageSource == ImageSourceType.RTSP ? Settings.RtspResolution : 
                new Size(200, 200);

            // if camera was already started, allow to start OR stop webserver
            if ( this.connectButton.Text != this._buttonConnectString ) {
                if ( Settings.RunWebserver ) {
                    ImageWebServer.Start();
                }
            }
            if ( !Settings.RunWebserver ) {
                ImageWebServer.Stop();
            }
            // handle auto start motion detection via button Start
            if ( Settings.DetectMotion ) {
                // click camera button to start it, if not yet running: would start webserver too if enabled 
                if ( this.connectButton.Text == this._buttonConnectString ) {
                    this.connectButton.PerformClick();
                }
            } else {
                // don't click connect button to stop processing, if running - because it's an autostart property
                //if ( this.connectButton.Text != this._buttonConnectString ) {
                //    this.connectButton.PerformClick();
                //}
            }
            // sync to motion count from today
            getTodaysMotionsCounters();
            // check whether settings were forcing a 'make video now'
            if ( Settings.MakeVideoNow ) {
                Task.Run(() => { makeMotionVideo(this.SourceResolution); });
            }
        }
        // update settings from app
        void updateSettingsFromAppProperties() {
            Settings.FormSize = this.Size;
            Settings.FormLocation = this.Location;
            Settings.ExposureVal = this.hScrollBarExposure.Value;
            Settings.ExposureMin = this.hScrollBarExposure.Minimum;
            Settings.ExposureMax = this.hScrollBarExposure.Maximum;
        }

        // send a message to master
        void TelegramSendMasterMessage(String message) {
            if ( Settings.UseTelegramBot && _Bot != null && Settings.TelegramWhitelist.Count > 0 && Settings.TelegramSendMaster ) {
                string chatid = Settings.TelegramWhitelist[0].Split(',')[1];
                _Bot.SendMessage(new SendMessageParams {
                    ChatId = chatid,
                    Text = message
                });
            }
        }

        // monitor RAM usage
        public string getAvailableRAM() {
            return ramCounter.NextValue() + " MB";
        }

        // get OS boot time
        [DllImport("Kernel32.dll")]
        static extern long GetTickCount64();
        DateTime getOsBootTime() {
            return DateTime.Now.AddMilliseconds(-(double)GetTickCount64());
        }

        // update today's motions counters; perhaps useful, if app is restarted during the day
        private static void getTodaysMotionsCounters() {
            DateTime now = DateTime.Now;
            string nowString = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if ( DateTime.Now.TimeOfDay >= new System.TimeSpan(19, 0, 0) ) {
                now = now.AddDays(1);
                nowString = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            string path = System.IO.Path.Combine(Settings.StoragePath, nowString);
            System.IO.Directory.CreateDirectory(path);
            DirectoryInfo di = new DirectoryInfo(path);
            FileInfo[] files = di.GetFiles("*.jpg");
            // save all but no sequences
            if ( Settings.SaveMotion && !Settings.SaveSequences ) {
                _motionsDetected = files.Length;
                _consecutivesDetected = 0;
            }
            // save sequences only
            if ( !Settings.SaveMotion && Settings.SaveSequences ) {
                _consecutivesDetected = files.Length != 0 ? files.Length : 0;
                // a bit of fake: _motionsDetected will always be larger than _consecutivesDetected, but _motionsDetected is not saved anywhere
                _motionsDetected = _consecutivesDetected;
                // in case path _nonc exists, adjust _motionsDetected accordingly
                string noncPath = System.IO.Path.Combine(Settings.StoragePath, nowString + "_nonc");
                di = new DirectoryInfo(noncPath);
                if ( di.Exists ) {
                    _motionsDetected += di.GetFiles("*.jpg").Length;
                }
            }
        }

        // a general timer 1x / 30s for app flow control
        private void timerFlowControl_Tick(object sender, EventArgs e) {

            // monitor RAM usage
            if ( Settings.LogRamUsage ) {
                Logger.logTextLn(DateTime.Now, String.Format("RAM usage: {0}", getAvailableRAM()));
            }

            // check once per 30s, whether to search & send an alarm video sequence; ideally it's just the rest after an already sent sequence of 6 motions started from detectMotion(..)
            if ( _alarmSequence && !_alarmSequenceBusy ) {
                // busy flag to prevent overrun
                _alarmSequenceBusy = true;
                // don't continue, if list is empty
                if ( _motionsList.Count == 0 ) {
                    _alarmSequenceBusy = false;
                    return;
                }
                // don't continue, if latest stored motion is older than 35s
                if ( (DateTime.Now - _motionsList[_motionsList.Count - 1].motionDateTime).TotalSeconds > 35 ) {
                    _alarmSequenceBusy = false;
                    return;
                }
                // pick the most recent consecutive motion
                int lastConsecutiveNdx = -1;
                Motion mo = new Motion("", new DateTime(1900, 01, 01));
                try {
                    for ( int i = _motionsList.Count - 1; i >= 0; i-- ) {
                        if ( _motionsList[i].motionConsecutive ) {
                            mo = _motionsList[i];
                            lastConsecutiveNdx = i;
                            break;
                        }
                    }
                } catch ( Exception exc ) {
                    Logger.logTextLnU(DateTime.Now, String.Format("timerFlowControl_Tick exc:{0}", exc.Message));
                    _alarmSequenceBusy = false;
                    return;
                }
                // don't continue, if latest consecutive motion doeas not exist or is older than 35s
                if ( lastConsecutiveNdx == -1 || (DateTime.Now - mo.motionDateTime).TotalSeconds > 35 ) {
                    _alarmSequenceBusy = false;
                    return;
                }
                // add dummy entry to the original motion list, it acts like a marker of what was previously sent
                try {
                    _motionsList.Add(new Motion("", new DateTime(1900, 1, 1)));
                    if ( Settings.DebugMotions ) {
                        int i = _motionsList.Count - 1;
                        Motion m = _motionsList[i];
                        Logger.logMotionListEntry("dummy", i, m.imageMotion != null, m.motionConsecutive, m.motionDateTime, m.motionSaved);
                    }
                } catch {;}
                // make a sub list containing the latest consecutive motions
                List<Motion> subList = new List<Motion>();
                for ( int i = lastConsecutiveNdx; i>=0; i-- ) {
                    if ( _motionsList[i].motionConsecutive ) {
                        subList.Insert(0, _motionsList[i]);
                    } else {
                        break;
                    }
                }
                // don't continue, if subList is too small
                if ( subList.Count < 2 ) {
                    _alarmSequenceBusy = false;
                    return;
                }
                // make latest motion video sequence, send it via Telegram and reset flag _alarmSequenceBusy when done
                Task.Run(() => { 
                    makeMotionSequence(subList, this.SourceResolution); 
                });
            }

            // once per hour
            if ( DateTime.Now.Minute % 60 == 0 && DateTime.Now.Second < 31 ) {

                // log once per hour the current app status
                bool currentWriteLogStatus = Settings.WriteLogfile;
                if ( !Settings.WriteLogfile ) {
                    Settings.WriteLogfile = true;
                }
                Logger.logTextLnU(DateTime.Now,
                    String.Format("motions [x] abs/seq: {0}/{1}\tprocess times [ms][x] curr/mín/max/>450: {2}/{3}/{4}/{5}\tbot alive={6}",
                    _motionsDetected,
                    _consecutivesDetected,
                    _procMs,
                    _procMsMin,
                    _procMsMax,
                    _proc450Ms,
                    (_Bot != null)));
                _procMsMin = long.MaxValue;
                _procMsMax = 0;
                _proc450Ms = 0;
                if ( !currentWriteLogStatus ) {
                    Settings.WriteLogfile = currentWriteLogStatus;
                }

                // check if remaining disk space is less than 2GB
                if ( (Settings.SaveMotion || Settings.SaveSequences) && driveFreeBytes(Settings.StoragePath) < TWO_GB ) {
                    Logger.logTextLnU(DateTime.Now, "timerFlowControl_Tick: free disk space <2GB");
                    // delete the 5 oldest image folders
                    for ( int i = 0; i < 5; i++ ) {
                        deleteOldestImageFolder(Settings.StoragePath);
                    }
                    // if finally the remaining disk space is less than 1GB
                    if ( driveFreeBytes(Settings.StoragePath) < ONE_GB ) {
                        // check alternative storage path and switch to it if feasible
                        if ( System.IO.Directory.Exists(Settings.StoragePathAlt) && driveFreeBytes(Settings.StoragePathAlt) > TEN_GB ) {
                            Settings.StoragePath = Settings.StoragePathAlt;
                            Logger.logTextLnU(DateTime.Now, "Now using alternative storage path.");
                            return;
                        }
                        // if finally the remaining disk space were still less than 1GB --> give up storing anything on disk
                        Logger.logTextLnU(DateTime.Now, "GrzMotion stops saving detected motions due to lack of disk space.");
                        Settings.SaveMotion = false;
                        Settings.SaveSequences = false;
                        Settings.writePropertyGridToIni();
                    }
                }
            }

            // one check every 15 minutes
            if ( DateTime.Now.Minute % 15 == 0 && DateTime.Now.Second < 31 ) {

                // retry to start RTSP after an obvious camera power off
                if ( Settings.ImageSource == ImageSourceType.RTSP && Settings.RtspRetry ) {
                    Settings.RtspRetry = false;
                    // handle auto start motion detection via button Start
                    if ( Settings.DetectMotion ) {
                        // click camera button to start it, if not yet running: would start webserver too if enabled 
                        if ( this.connectButton.Text == this._buttonConnectString ) {
                            this.connectButton.PerformClick();
                        }
                    }
                }

                // reset RTSP app restart counter; it is used to prevent restart loops
                if ( Settings.RtspRestartAppCount > 0 ) {
                    if ( (DateTime.Now - _appStartTime).TotalHours > 1 ) {
                        Settings.RtspRestartAppCount = 0;
                        _rtspDeviceExceptionCounter = 0;
                        AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                        ini.IniWriteValue("GrzMotion", "RtspRestartAppCount", "0");
                        Logger.logTextLnU(DateTime.Now, "timerFlowControl_Tick: reset RTSP app restart counter to 0");
                    }
                }

                // log cleanup
                if ( Settings.DebugMotions ) {
                    Logger.logMotionListExtra(String.Format("{0} cleanup start: {1}", DateTime.Now.ToString("HH-mm-ss_fff"), _motionsList.Count));
                }
                // clean up _motionList from leftover Bitmaps
                DateTime now = DateTime.Now;
                for ( int i=0; i < _motionsList.Count; i++ ) {
                    // ignore all entries younger than 60s: TBD ?? what if a sequence is longer than 60s ??
                    if ( (now - _motionsList[i].motionDateTime).TotalSeconds > 60 ) {
                        // release hires images
                        if ( _motionsList[i].imageMotion != null ) {
                            _motionsList[i].imageMotion.Dispose();
                            _motionsList[i].imageMotion = null;
                            // debug motion list: only log disposed images
                            if ( Settings.DebugMotions ) {
                                Motion m = _motionsList[i];
                                Logger.logMotionListEntry("flowctl", i, m.imageMotion != null, m.motionConsecutive, m.motionDateTime, m.motionSaved);
                            }
                        }
                        // lores images
                        if ( Settings.DebugNonConsecutives ) {
                            // save an release
                            if ( _motionsList[i].imageProc != null ) {
                                string pathNonC = System.IO.Path.GetDirectoryName(_motionsList[i].fileNameProc);
                                pathNonC = pathNonC.Substring(0, pathNonC.Length - 4) + "nonc";
                                string fileNonC = System.IO.Path.GetFileName(_motionsList[i].fileNameProc);
                                System.IO.Directory.CreateDirectory(pathNonC);
                                _motionsList[i].imageProc.Save(System.IO.Path.Combine(pathNonC, fileNonC), System.Drawing.Imaging.ImageFormat.Jpeg);
                                _motionsList[i].imageProc.Dispose();
                                _motionsList[i].imageProc = null;
                            }
                        } else {
                            // release only
                            if ( _motionsList[i].imageProc != null ) {
                                _motionsList[i].imageProc.Dispose();
                                _motionsList[i].imageProc = null;
                            }
                        }
                    }
                }
                // log cleanup
                if ( Settings.DebugMotions ) {
                    Logger.logMotionListExtra("cleanup finished");
                }


                // try to restart Telegram, if it should run but it doesn't due to an internal fail
                if ( Settings.UseTelegramBot && _Bot == null && Settings.TelegramRestartAppCount < 5 ) {
                    // restart app after too many failing Telegram restarts in the current app session
                    if ( _telegramRestartCounter > 5 ) {
                        // set flag, that this is not an app crash
                        AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                        ini.IniWriteValue("GrzMotion", "AppCrash", "False");
                        // memorize count of Telegram malfunctions forcing an app restart: needed to avoid restart loops
                        Settings.TelegramRestartAppCount++;
                        ini.IniWriteValue("GrzMotion", "TelegramRestartAppCount", Settings.TelegramRestartAppCount.ToString());
                        Logger.logTextLnU(DateTime.Now, String.Format("timerFlowControl_Tick: Telegram restart count > 5, now restarting GrzMotion"));
                        // restart GrzMotion: if Telegram restart count in session > 5, then restart app (usual max. 2 over months)
                        string exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
                        try {
                            System.Diagnostics.Process.Start(startInfo);
                            this.Close();
                        } catch ( Exception ) {; }
                    } else {
                        // restart Telegram
                        try {
                            _telegramRestartCounter++;
                            Logger.logTextLnU(DateTime.Now, String.Format("timerFlowControl_Tick: Telegram restart #{0} of 5", _telegramRestartCounter));
                            _telegramOnErrorCount = 0;
                            _Bot = new TeleSharp.TeleSharp(Settings.BotAuthenticationToken);
                            _Bot.OnMessage += OnMessage;
                            _Bot.OnError += OnError;
                            _Bot.OnLiveTick += OnLiveTick;
                            this.timerCheckTelegramLiveTick.Start();
                            // send so far unsent cached master messages
                            while ( telegramMasterMessageCache.Count > 0 ) {
                                TelegramSendMasterMessage(telegramMasterMessageCache[0]);
                                telegramMasterMessageCache.RemoveAt(0);
                            }
                        } catch( Exception ex ) {
                            Logger.logTextLnU(DateTime.Now, String.Format("timerFlowControl_Tick exception: {0}", ex.Message));
                        }
                    }
                }

                // EXPERIMENTAL: check for a gradual image brightness change and adjust camera exposure time accordingly
                if ( Settings.ExposureByApp ) {
                    // get brightness change over time
                    double brightnessChange = GrayAvgBuffer.GetSlope();
                    // brightness change shall exceed an empirical threshold
                    if ( _brightnessNoChangeCounter >= 4 ) {
                        _brightnessNoChangeCounter = 0;
                        Logger.logTextLn(DateTime.Now, string.Format("timerFlowControl_Tick: no brightness change detected {0:0.###} vs. {1}", brightnessChange, BRIGHTNESS_CHANGE_THRESHOLD));
                    }
                    if ( Math.Abs(brightnessChange) < BRIGHTNESS_CHANGE_THRESHOLD ) {
                        _brightnessNoChangeCounter++;
                        return;
                    }
                    // get current exposure time
                    int currValue;
                    bool success = getCameraExposureTime(out currValue);
                    if ( !success ) {
                        Logger.logTextLn(DateTime.Now, "timerFlowControl_Tick: no current exposure time returned");
                        return;
                    }
                    // distinguish between images got brighter vs. darker
                    int changeValue = currValue;
                    if ( brightnessChange < 0 ) {
                        changeValue++;
                    } else {
                        changeValue--;
                    }
                    // set new exposure time
                    int newValue;
                    success = setCameraExposureTime(changeValue, out newValue);
                    if ( !success ) {
                        Logger.logTextLn(DateTime.Now, "timerFlowControl_Tick: no new exposure time returned");
                        return;
                    }
                    // reset brightness monitor history
                    GrayAvgBuffer.ResetData();
                    // update UI
                    _brightnessNoChangeCounter = 0;
                    Logger.logTextLn(DateTime.Now, string.Format("timerFlowControl_Tick: camera exposure time old={0} new={1}", currValue, newValue));
                    updateUiCameraProperties();
                }

            }

            // only care, if making a daily video is active
            if ( Settings.MakeDailyVideo ) {
                // make video from today's images at 19:00:00: if not done yet AND if today's error count < 5 AND not in progress
                if ( DateTime.Now.TimeOfDay >= _videoTime && !Settings.DailyVideoDone && (_dailyVideoErrorCount < 5) && !_dailyVideoInProgress ) {
                    // generate daily video: a) from single motion images b) from motion sequences afterwards
                    Task.Run(() => { makeMotionVideo(this.SourceResolution); });
                    // clear _motionsList for current day
                    for ( int i = 0; i < _motionsList.Count; i++ ) {
                        // dispose hires if existing
                        if ( _motionsList[i].imageMotion != null ) {
                            _motionsList[i].imageMotion.Dispose();
                            _motionsList[i].imageMotion = null;
                        }
                        // dispose lores if existing
                        if ( _motionsList[i].imageProc != null ) {
                            _motionsList[i].imageProc.Dispose();
                            _motionsList[i].imageProc = null;
                        }
                    }
                    _motionsList.Clear();
                    // done   
                    return;
                }
                // prevent 'make video' loops in case of errors
                if ( _dailyVideoErrorCount == 5 ) {
                    _dailyVideoErrorCount = int.MaxValue;
                    Logger.logTextLnU(DateTime.Now, "timerFlowControl_Tick: too many 'make video' errors, giving up for today");
                }
                // some time after 19:00 _dailyVideoDone might become TRUE, it needs to reset after midnight
                if ( Settings.DailyVideoDone && DateTime.Now.TimeOfDay >= _midNight && DateTime.Now.TimeOfDay < _videoTime ) {
                    // reset _dailyVideoDone right after midnight BUT not after 19:00
                    Settings.DailyVideoDone = false;
                    IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                    ini.IniWriteValue("GrzMotion", "DailyVideoDoneForToday", "False");
                    _dailyVideoErrorCount = 0;
                    _dailyVideoInProgress = false;
                    Logger.logTextLnU(DateTime.Now, "timerFlowControl_Tick: reset video done flag at midnight");
                    // sync to motion count from today
                    getTodaysMotionsCounters();
                }
            }

            // actions around 00:30 --> timer tick is 30s, so within a range of 60s this condition will be met once for sure (surely 2x)
            if ( DateTime.Now.TimeOfDay >= MainForm.BootTimeBeg && DateTime.Now.TimeOfDay <= MainForm.BootTimeEnd ) {
                // reset RTSP exception restart app counter and RTSP device restart counter
                if ( Settings.RtspRestartAppCount > 0 ) {
                    Settings.RtspRestartAppCount = 0;
                    _rtspDeviceExceptionCounter = 0;
                    AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                    ini.IniWriteValue("GrzMotion", "RtspRestartAppCount", "0");
                    // if not yet running, start RTSP via app restart
                    if ( Settings.ImageSource == ImageSourceType.RTSP ) {
                        if ( _rtspStream == null || (_rtspStream != null && _rtspStream.GetStatus != RtspControl.Status.RUNNING) ) {
                            // set flag, that this is not an app crash
                            ini.IniWriteValue("GrzMotion", "AppCrash", "False");
                            // memorize count of app restarts, needed to avoid restart loops
                            Settings.RtspRestartAppCount++;
                            ini.IniWriteValue("GrzMotion", "RtspRestartAppCount", Settings.RtspRestartAppCount.ToString());
                            // restart GrzMotion
                            Logger.logTextLnU(DateTime.Now, String.Format("new day RTSP exception GrzMotion start"));
                            System.Diagnostics.Process.Start(Application.ExecutablePath);
                            Environment.Exit(0);
                            return;
                        }
                    }
                }
                // reset Telegram restart counter for the app current session
                _telegramRestartCounter = 0;
                // reset 'Telegram malfunction forced app restart' counter
                if ( Settings.TelegramRestartAppCount > 0 ) {
                    Settings.TelegramRestartAppCount = 0;
                    AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                    ini.IniWriteValue("GrzMotion", "TelegramRestartAppCount", "0");
                }
                // only care, if daily reboot of Windows-OS is active
                if ( Settings.RebootDaily ) {
                    Logger.logTextLnU(DateTime.Now, "Now: daily reboot system");
                    // INI: write to ini
                    updateSettingsFromAppProperties();
                    Settings.writePropertyGridToIni();
                    // a planned reboot is not a crash
                    AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                    ini.IniWriteValue("GrzMotion", "AppCrash", "False");
                    // reboot
                    System.Diagnostics.Process.Start("shutdown", "/r /f /y /t 1");    // REBOOT: /f == force if /t > 0; /y == yes to all questions asked 
                } else {
                    // reset counters etc
                    _motionsList.Clear();
                    getTodaysMotionsCounters();
                }
            }
        }

        // make video sequence from motion/image data stored in mol aka List<Motion> 
        public void makeMotionSequence(List<Motion> mol, Size size) {
            // folder and video file name
            DateTime now = DateTime.Now;
            Logger.logTextLnU(DateTime.Now, "makeMotionSequence: 'on demand' start");
            string nowString = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = System.IO.Path.Combine(Settings.StoragePath, nowString + "_sequ");
            System.IO.Directory.CreateDirectory(path);
            // fileName shall distinguish between full motion sequence and an 'on demand' sequence via Telegram
            string fileName = System.IO.Path.Combine(path, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".avi");
            if ( _alarmSequence && _Bot != null && _sequenceReceiver != null ) {
                fileName = System.IO.Path.Combine(path, now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".avi");
            }
            Accord.Video.FFMPEG.VideoFileWriter writer = null;
            // try to make a motion video sequence
            try {
                // video writer: !! needs both VC_redist.x86.exe and VC_redist.x64.exe installed on target PC !!
                writer = new VideoFileWriter();
                // create new video file
                writer.Open(fileName, size.Width, size.Height, 25, VideoCodec.MPEG4);
                Bitmap image;
                // loop list 
                foreach ( Motion mo in mol ) {
                    if ( mo.motionConsecutive ) {
                        try {
                            if ( mo.motionSaved ) {
                                // if motion is already saved, get bmp from disk
                                image = new Bitmap(mo.fileNameMotion);
                            } else {
                                // if motion is not yet saved, get image bmp from list
                                image = (Bitmap)mo.imageMotion.Clone();
                            }
                            writer.WriteVideoFrame(image);
                            image.Dispose();
                        } catch {
                            continue;
                        }
                    }
                }
                writer.Close();
            } catch ( Exception ex1 ) {
                // update bot status
                if ( _alarmSequence && _Bot != null && _sequenceReceiver != null ) {
                    _Bot.SendMessage(new SendMessageParams {
                        ChatId = _sequenceReceiver.Id.ToString(),
                        Text = "Make video sequence failed."
                    });
                }
                Logger.logTextLnU(DateTime.Now, String.Format("makeMotionSequence ex: {0}", ex1.Message));
                if ( writer != null ) {
                    writer.Close();
                }
                _alarmSequenceBusy = false;
                return;
            }
            // send motion alarm sequence
            if ( _alarmSequence && _Bot != null && _sequenceReceiver != null ) {
                sendVideo(_sequenceReceiver, fileName);
                Logger.logTextLnU(DateTime.Now, "makeMotionSequence: 'on demand' done");
            } else {
                if ( Settings.MakeVideoNow ) {
                    Logger.logTextLnU(DateTime.Now, "makeMotionSequence: 'now sequence' done");
                } else {
                    Logger.logTextLnU(DateTime.Now, "makeMotionSequence: 'daily sequence' done");
                }
            }
            // the busy flag was set in the calling method
            _alarmSequenceBusy = false;
        }
        // make video from today's images  
        public void makeMotionVideo(Size size, MessageSender sender = null) {
            _dailyVideoInProgress = true;
            Logger.logTextLnU(DateTime.Now, "makeMotionVideo: 'make video' start");
            // folder and video file name
            string nowString = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = System.IO.Path.Combine(Settings.StoragePath, nowString);
            System.IO.Directory.CreateDirectory(path);
            string fileName = "";
            // folder with images to process
            DirectoryInfo d = new DirectoryInfo(path);
            FileInfo[] Files = d.GetFiles("*.jpg");
            int fileCount = Files.Length;
            // if no files were found
            if ( fileCount == 0 ) {
                Logger.logTextLnU(DateTime.Now, "makeMotionVideo: no files");
                if ( !Settings.MakeVideoNow ) {
                    // set done flag for making the today's video
                    Settings.DailyVideoDone = true;
                    IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                    ini.IniWriteValue("GrzMotion", "DailyVideoDoneForToday", "True");
                }
                Settings.MakeVideoNow = false;
                _dailyVideoErrorCount = 0;
                _dailyVideoInProgress = false;
                _sendVideo = false;
                return;
            }
            // do the video generation work
            int excStep = 0;
            if ( Settings.VideoH264 ) {
                // OpenCV with H264 --> works well on Android
                try {
                    // emgu wiki: https://www.emgu.com/wiki/index.php/X264_VFW requires video file extension .mp4 and backend ID for MSMF
                    Backend[] backends = CvInvoke.WriterBackends;
                    int backend_idx = 0;
                    foreach ( Backend be in backends ) {
                        if ( be.Name.Equals("MSMF") ) {
                            backend_idx = be.ID;
                            break;
                        }
                    }
                    excStep = 1;
                    fileName = System.IO.Path.Combine(path, nowString + ".mp4");
                    excStep = 2;
                    char[] arr = "H264".ToCharArray();
                    VideoWriter writerOpenCV = new VideoWriter(fileName, backend_idx, VideoWriter.Fourcc(arr[0], arr[1], arr[2], arr[3]), 30, Settings.ScaledImageSize, true);
                    excStep = 3;
                    // record Mat frame to video file
                    if ( writerOpenCV != null ) {
                        // loop images
                        int fileError = 0;
                        foreach ( FileInfo file in Files ) {
                            try {
                                excStep = 4;
                                Bitmap tmpBmp1 = new Bitmap(file.FullName);
                                excStep = 5;
                                Bitmap tmpBmp2 = resizeBitmap(tmpBmp1, Settings.ScaledImageSize);
                                excStep = 6;
                                Mat mat = tmpBmp2.ToMat();
                                excStep = 7;
                                writerOpenCV.Write(mat);
                                excStep = 8;
                                mat.Dispose();
                                excStep = 9;
                                tmpBmp2.Dispose();
                                excStep = 10;
                                tmpBmp1.Dispose();
                            } catch (Exception ex) {
                                Logger.logTextLn(DateTime.Now, String.Format("makeMotionVideo OpenCV exc @ step{0} {1}", excStep, ex.Message));
                                fileError++;
                                continue;
                            }
                        }
                        writerOpenCV.Dispose();
                        // if image files are locked
                        if ( fileError == fileCount ) {
                            Logger.logTextLnU(DateTime.Now, "makeMotionVideo OpenCV: too many file errors");
                            if ( !Settings.MakeVideoNow ) {
                                Settings.DailyVideoDone = false;
                            }
                            Settings.MakeVideoNow = false;
                            _dailyVideoErrorCount++;
                            _dailyVideoInProgress = false;
                            _sendVideo = false;
                            return;
                        }
                    } else {
                        Logger.logTextLnU(DateTime.Now, String.Format("makeMotionVideo OpenCV: writerOpenCV == null  "));
                    }
                } catch ( Exception ex ) {
                    // update bot status
                    if ( _sendVideo && _Bot != null && sender != null ) {
                        _Bot.SendMessage(new SendMessageParams {
                            ChatId = sender.Id.ToString(),
                            Text = "Make video failed, try again later."
                        });
                    }
                    Logger.logTextLnU(DateTime.Now, String.Format("makeMotionVideo OpenCV ex at step{0}: {1}", excStep, ex.Message));
                    if ( !Settings.MakeVideoNow ) {
                        Settings.DailyVideoDone = false;
                    }
                    Settings.MakeVideoNow = false;
                    _dailyVideoErrorCount++;
                    _dailyVideoInProgress = false;
                    _sendVideo = false;
                    return;
                }

            } else {
                // FFMPEG with MPEG4 --> not suitable for Android 
                fileName = System.IO.Path.Combine(path, nowString + ".avi");
                Accord.Video.FFMPEG.VideoFileWriter writer = null;
                try {
                    // video writer: !! needs both VC_redist.x86.exe and VC_redist.x64.exe installed on target PC !!
                    writer = new VideoFileWriter();
                    excStep = 1;
                    // create new video file
                    excStep = 2;
                    writer.Open(fileName, size.Width, size.Height, 25, VideoCodec.MPEG4);
                    excStep = 3;
                    int fileError = 0;
                    Bitmap image;
                    foreach ( FileInfo file in Files ) {
                        try {
                            image = new Bitmap(file.FullName);
                            writer.WriteVideoFrame(image);
                            image.Dispose();
                        } catch {
                            fileError++;
                            continue;
                        }
                    }
                    // if image files are locked
                    if ( fileError == fileCount ) {
                        Logger.logTextLnU(DateTime.Now, "makeMotionVideo FFMPEG: too many file errors");
                        if ( !Settings.MakeVideoNow ) {
                            Settings.DailyVideoDone = false;
                        }
                        Settings.MakeVideoNow = false;
                        _dailyVideoErrorCount++;
                        _dailyVideoInProgress = false;
                        _sendVideo = false;
                        writer.Close();
                        return;
                    }
                    writer.Close();
                } catch ( Exception ex ) {
                    // update bot status
                    if ( _sendVideo && _Bot != null && sender != null ) {
                        _Bot.SendMessage(new SendMessageParams {
                            ChatId = sender.Id.ToString(),
                            Text = "Make video failed, try again later."
                        });
                    }
                    Logger.logTextLnU(DateTime.Now, String.Format("makeMotionVideo FFMPEG ex at step{0}: {1}", excStep, ex.Message));
                    if ( !Settings.MakeVideoNow ) {
                        Settings.DailyVideoDone = false;
                    }
                    Settings.MakeVideoNow = false;
                    _dailyVideoErrorCount++;
                    _dailyVideoInProgress = false;
                    _sendVideo = false;
                    if ( writer != null ) {
                        writer.Close();
                    }
                    return;
                }
            }
            // distinguish regular video (== !_sendVideo) and video on demand (== _sendVideo)
            if ( !_sendVideo ) {
                if ( !Settings.MakeVideoNow ) {
                    // set done flag for making the today's video
                    Settings.DailyVideoDone = true;
                    IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                    ini.IniWriteValue("GrzMotion", "DailyVideoDoneForToday", "True");
                }
            } else {
                // send on demand video
                if ( sender != null ) {
                    sendVideo(sender, fileName);
                }
            }
            Settings.MakeVideoNow = false;
            _dailyVideoErrorCount = 0;
            _dailyVideoInProgress = false;
            Logger.logTextLnU(DateTime.Now, "makeMotionVideo: 'make video' done");
        }
        // prepare to send a video
        void prepareToSendVideo(MessageSender sender) {
            // check today's folder for a existing video file
            string fileName = "";
            if ( Settings.DailyVideoDone ) {
                // folder and video file name
                string nowString = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string path = System.IO.Path.Combine(Settings.StoragePath, nowString);
                System.IO.Directory.CreateDirectory(path);
                fileName = System.IO.Path.Combine(path, nowString + ".avi");
                if ( !System.IO.File.Exists(fileName) ) {
                    fileName = "";
                }
            }
            if ( fileName.Length == 0 ) {
                _Bot.SendMessage(new SendMessageParams {
                    ChatId = sender.Id.ToString(),
                    Text = "Preparing video may take a while ..."
                });
                try {
                    // if no video exists, make one
                    Task.Run(() => { makeMotionVideo(this.SourceResolution, sender); });
                } catch ( Exception e ) {
                    Logger.logTextLnU(DateTime.Now, "prepareToSendVideo: " + e.Message);
                    _sendVideo = false;
                }
            } else {
                // if the video exists, send it
                sendVideo(sender, fileName);
            }
        }
        // really send video
        void sendVideo(MessageSender sender, string fileName) {
            if ( _Bot != null ) {
                _Bot.SetCurrentAction(sender, ChatAction.UploadVideo);
                byte[] buffer = System.IO.File.ReadAllBytes(fileName);
                _Bot.SendVideo(sender, buffer, "snapshot", "video");
                Logger.logTextLnU(DateTime.Now, "video sent");
                _sendVideo = false;
            }
        }

        // continuously check network availability: needed for Telegram bot
        System.Net.NetworkInformation.PingReply execPing(string strTestIP) {
            System.Net.NetworkInformation.Ping pinger = new System.Net.NetworkInformation.Ping();
            System.Net.NetworkInformation.PingReply reply = pinger.Send(strTestIP, 10);
            return reply;
        }
        public void doPingLooper(ref bool runPing, ref string strTestIP) {
            int pingFailCounter = 0;
            int stopLogCounter = 0;
            do {
                // execute ping
                System.Net.NetworkInformation.PingReply reply = execPing(strTestIP);
                // two possibilities
                if ( reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success ) {
                    // ping ok
                    Settings.PingOk = true;
                    // notify about previous fails
                    if ( pingFailCounter > 10 ) {
                        Logger.logTextLnU(DateTime.Now, String.Format("ping is ok - after {0} fails", pingFailCounter));
                    }
                    pingFailCounter = 0;
                    if ( stopLogCounter > 0 ) {
                        Logger.logTextLnU(DateTime.Now, "ping is ok - after a long time failing");
                    }
                    stopLogCounter = 0;
                } else {
                    // ping fail
                    Settings.PingOk = false;
                    pingFailCounter++;
                }
                // reboot AFTER 10x subsequent ping fails in 100s 
                if ( (pingFailCounter > 0) && (pingFailCounter % 10 == 0) ) {
                    Logger.logTextLn(DateTime.Now, "network reset after 10x ping fail");
                    bool networkUp = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                    if ( networkUp ) {
                        Logger.logTextLn(DateTime.Now, "network is up, but 10x ping failed");
                        if ( Settings.RebootPingCounter < 3 ) {
                            if ( Settings.RebootPingAllowed ) {
                                Logger.logTextLnU(DateTime.Now, "network is up, but ping fails --> next reboot System");
                                Settings.RebootPingCounter++;
                                AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                                ini.IniWriteValue("GrzMotion", "RebootPingCounter", Settings.RebootPingCounter.ToString());
                                ini.IniWriteValue("GrzMotion", "RebootPingFlagActive", "True");
                                System.Diagnostics.Process.Start("shutdown", "/r /f /y /t 1");    // REBOOT: /f == force if /t > 0; /y == yes to all questions asked 
                            } else {
                                Logger.logTextLnU(DateTime.Now, "network is up, but ping fails --> BUT reboot System is not allowed");
                            }
                        } else {
                            if ( stopLogCounter < 5 ) {
                                Logger.logTextLn(DateTime.Now, "Reboot Counter >= 3 --> no reboot, despite of local network is up");
                                stopLogCounter++;
                            }
                        }
                    } else {
                        if ( Settings.RebootPingCounter < 3 ) {
                            if ( Settings.RebootPingAllowed ) {
                                Logger.logTextLnU(DateTime.Now, "network is down --> next reboot System");
                                Settings.RebootPingCounter++;
                                AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                                ini.IniWriteValue("GrzMotion", "RebootPingCounter", Settings.RebootPingCounter.ToString());
                                ini.IniWriteValue("GrzMotion", "RebootPingFlagActive", "True");
                                System.Diagnostics.Process.Start("shutdown", "/r /f /y /t 1");    // REBOOT: /f == force if /t > 0; /y == yes to all questions asked 
                            } else {
                                Logger.logTextLnU(DateTime.Now, "network is down --> BUT reboot System is not allowed");
                            }
                        } else {
                            if ( stopLogCounter < 5 ) {
                                Logger.logTextLn(DateTime.Now, "Reboot Counter >= 3 --> no reboot, despite of network is down");
                                stopLogCounter++;
                            }
                        }
                    }
                }
                //
                System.Threading.Thread.Sleep(10000);
            } while ( runPing );
        }

        // Telegram connector provides a 'live tick info', this timer always monitors such 'live tick info' 
        private void timerCheckTelegramLiveTick_Tick(object sender, EventArgs e) {
            if ( _Bot != null ) {
                TimeSpan span = DateTime.Now - _connectionLiveTick;
                if ( span.TotalSeconds > 120 ) {
                    if ( _telegramLiveTickErrorCount > 10 ) {
                        // set flag, that this is not an app crash
                        AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                        ini.IniWriteValue("GrzMotion", "AppCrash", "False");
                        // Telegram malfunction forces an app restart
                        Settings.TelegramRestartAppCount++;
                        ini.IniWriteValue("GrzMotion", "TelegramRestartAppCount", Settings.TelegramRestartAppCount.ToString());
                        // give up after more than 10 live tick errors and log app restart
                        Logger.logTextLnU(DateTime.Now, String.Format("timerCheckTelegramLiveTick_Tick: Telegram not active for #{0} cycles, now restarting GrzMotion", _telegramLiveTickErrorCount));
                        // restart GrzMotion
                        string exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
                        try {
                            System.Diagnostics.Process.Start(startInfo);
                            this.Close();
                        } catch ( Exception ) {; }
                    } else {
                        // try to restart Telegram, it's not fully reliable - therefore a counter is introduced
                        _telegramLiveTickErrorCount++;
                        Logger.logTextLnU(DateTime.Now, String.Format("timerCheckTelegramLiveTick_Tick: Telegram not active detected, now shut it down #{0}", _telegramLiveTickErrorCount));
                        try {
                            _Bot.OnMessage -= OnMessage;
                            _Bot.OnError -= OnError;
                            _Bot.OnLiveTick -= OnLiveTick;
                            _Bot.Stop();
                            _Bot = null;
                            this.timerCheckTelegramLiveTick.Stop();
                        } catch ( Exception ex ) {
                            Logger.logTextLnU(DateTime.Now, String.Format("timerCheckTelegramLiveTick_Tick ex: {0}", ex.Message));
                        }
                    }
                }
            }
        }
        // Telegram provides a live tick info
        private void OnLiveTick(DateTime now) {
            _connectionLiveTick = now;
            if ( _telegramLiveTickErrorCount > 0 ) {
                // telegram restart after a live tick fail was successful
                Logger.logTextLnU(DateTime.Now, String.Format("OnLiveTick: Telegram now active after previous fail #{0}", _telegramLiveTickErrorCount));
                _telegramLiveTickErrorCount = 0;
            }
        }
        // Telegram connector detected a connection issue
        private void OnError(bool connectionError) {
            _telegramOnErrorCount++;
            Logger.logTextLnU(DateTime.Now, String.Format("OnError: Telegram connect error {0} {1}", _telegramOnErrorCount, connectionError));
            if ( _Bot != null ) {
                _Bot.OnMessage -= OnMessage;
                _Bot.OnError -= OnError;
                _Bot.OnLiveTick -= OnLiveTick;
                _Bot.Stop();
                _Bot = null;
                this.timerCheckTelegramLiveTick.Stop();
                Logger.logTextLnU(DateTime.Now, "OnError: Telegram connect error, now shut down");
            } else {
                Logger.logTextLnU(DateTime.Now, "OnError: _Bot == null, but OnError still active");
            }
        }
        // read received Telegram messages to the local bot
        private void OnMessage(TeleSharp.Entities.Message message) {
            // get message sender information
            MessageSender sender = (MessageSender)message.Chat ?? message.From;
            Logger.logTextLnU(DateTime.Now, "'" + message.Text + "'");
            string baseStoragePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            if ( string.IsNullOrEmpty(message.Text) || string.IsNullOrEmpty(baseStoragePath) ) {
                return;
            }
            // whitelist handling
            if ( Settings.UseTelegramWhitelist ) {
                bool callerIstAccepted = false;
                string caller = sender.Id.ToString();
                try {
                    foreach ( var client in Settings.TelegramWhitelist ) {
                        var clientId = client.Split(',')[1];
                        if ( clientId == caller ) {
                            callerIstAccepted = true;
                            break;
                        }
                    }
                } catch (Exception ex) {
                    Logger.logTextLnU(DateTime.Now, "OnMessage: whitelist format error");
                }
                if ( !callerIstAccepted ) {
                    Logger.logTextLnU(DateTime.Now, "caller rejected: " + caller);
                    return;
                }
            }
            try {
                if ( !string.IsNullOrEmpty(message.Text) )
                    switch ( message.Text.ToLower() ) {
                        case "/help": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "Valid commands, pick one:\n\n/hello  /help  /time  /location\n\n/video  /image\n\n/start_notify  /stop_notify  /keep_notify\n\n/quick_alarm  /start_alarm  /stop_alarm"
                                });
                                break;
                            }
                        case "/hello": {
                                string welcomeMessage = $"Welcome {message.From.Username} !{Environment.NewLine}My name is {_Bot.Me.Username}{Environment.NewLine}";
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = welcomeMessage
                                });
                                break;
                            }
                        case "/time": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString()
                                });
                                break;
                            }
                        case "/location": {
                                _Bot.SendLocation(sender, "50.69421", "3.17456");
                                break;
                            }
                        case "/video": {
                                if ( !_sendVideo && !_dailyVideoInProgress ) {
                                    _Bot.SendMessage(new SendMessageParams {
                                        ChatId = sender.Id.ToString(),
                                        Text = "Checking video status ..."
                                    });
                                    _sendVideo = true;
                                    prepareToSendVideo(sender);
                                } else {
                                    if ( _dailyVideoInProgress ) {
                                        _Bot.SendMessage(new SendMessageParams {
                                            ChatId = sender.Id.ToString(),
                                            Text = "The daily video is in progress, try again in a few minutes."
                                        });
                                    } else {
                                        _Bot.SendMessage(new SendMessageParams {
                                            ChatId = sender.Id.ToString(),
                                            Text = "making video is already in progress ..."
                                        });
                                    }
                                }
                                break;
                            }
                        case "/quick_alarm": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /quick_alarm - alarm will be sent ASAP"
                                });
                                _alarmSequenceAsap = true;
                                _alarmSequence = true;
                                _sequenceReceiver = sender;
                                break;
                            }
                        case "/start_alarm": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /start_alarm - alarm send delay max. 30s "
                                });
                                _alarmSequenceAsap = false;
                                _alarmSequence = true;
                                _sequenceReceiver = sender;
                                break;
                            }
                        case "/stop_alarm": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /stop_alarm"
                                });
                                _alarmSequenceAsap = false;
                                _alarmSequence = false;
                                _sequenceReceiver = null;
                                break;
                            }
                        case "/start_notify": {
                                // a fresh start quits a previous notification scenario: inform subscriber
                                if ( Settings.KeepTelegramNotifyAction ) {
                                    _Bot.SendMessage(new SendMessageParams {
                                        ChatId = Settings.TelegramNotifyReceiver.ToString(),
                                        Text = "notification is switched to a new message receiver"
                                    });
                                    Settings.KeepTelegramNotifyAction = false;
                                    Settings.TelegramNotifyReceiver = -1;
                                }
                                // inform current subscriber
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /start_notify"
                                });
                                _alarmNotify = true;
                                _notifyReceiver = sender;
                                _notifyText = " - alarm notification active";
                                break;
                            }
                        case "/stop_notify": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /stop_notify"
                                });
                                _alarmNotify = false;
                                _notifyReceiver = null;
                                // stop means stop permanent notification too
                                Settings.KeepTelegramNotifyAction = false;
                                Settings.TelegramNotifyReceiver = -1;
                                _notifyText = "";
                                break;
                            }
                        case "/keep_notify": {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = "roger /keep_notify until further notice"
                                });
                                _alarmNotify = true;
                                _notifyReceiver = sender;
                                Settings.KeepTelegramNotifyAction = true;
                                Settings.TelegramNotifyReceiver = _notifyReceiver.Id;
                                _notifyText = " - permanent alarm notification active";
                                break;
                            }
                        case "/image": {
                                if ( _currFrame == null ) {
                                    Logger.logTextLnU(DateTime.Now, "image capture not working");
                                    _Bot.SendMessage(new SendMessageParams {
                                        ChatId = sender.Id.ToString(),
                                        Text = "image capture not working",
                                    });
                                    break;
                                }
                                _Bot.SetCurrentAction(sender, ChatAction.UploadPhoto);
                                try {
                                    Bitmap tmp = (Bitmap)_currFrame.Clone();
                                    byte[] buffer = bitmapToByteArray(tmp);
                                    tmp.Dispose();
                                    _Bot.SendPhoto(sender, buffer, "snapshot", "image");
                                    Logger.logTextLnU(DateTime.Now, String.Format("image sent to: {0}", sender.Id.ToString()));
                                } catch (Exception e) {
                                    Logger.logTextLnU(DateTime.Now, "EXCEPTION /image: " + e.Message);
                                    _Bot.SendMessage(new SendMessageParams {
                                        ChatId = sender.Id.ToString(),
                                        Text = "image capture failed",
                                    });
                                }
                                break;
                            }
                        default: {
                                _Bot.SendMessage(new SendMessageParams {
                                    ChatId = sender.Id.ToString(),
                                    Text = message.Text,
                                });
                                Logger.logTextLnU(DateTime.Now, String.Format("unknown command '{0}' from {1}", message.Text, sender.Id.ToString()));
                                break;
                            }
                    }
            } catch ( Exception ex ) {
                Logger.logTextLnU(DateTime.Now, "EXCEPTION OnMessage: " + ex.Message);
            }
        }

        // get UVC devices into a combo box
        void getCameraBasics() {
            this.devicesCombo.Items.Clear();
            int indexToSelect = 0;

            // add RTSP stream source
            if ( Settings.RtspConnectUrl.Length != 0 ) {
                this.devicesCombo.Items.Add(Settings.RtspConnectUrl);
                if ( Settings.ImageSource == ImageSourceType.RTSP ) {
                    indexToSelect = 0;
                }
            }

            // enumerate UVC video devices
            _uvcDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if ( _uvcDevices.Count == 0 && this.devicesCombo.Items.Count == 0 ) {
                this.devicesCombo.Items.Add("No DirectShow devices found");
                this.devicesCombo.SelectedIndex = 0;
                uvcResolutionsCombo.Items.Clear();
                return;
            }

            // loop all UVC devices and add them to combo
            int offset = RtspDeviceExists() ? 1 : 0;
            int ndx = 0;
            foreach ( FilterInfo device in _uvcDevices ) {
                this.devicesCombo.Items.Add(device.Name);
                if ( Settings.ImageSource == ImageSourceType.UVC && device.MonikerString == Settings.CameraMoniker ) {
                    indexToSelect = ndx + offset;
                }
                ndx++;
            }

            // selecting an index automatically calls devicesCombo_SelectedIndexChanged(..)
            if ( (_uvcDevice != null) && _uvcDevice.IsRunning ) {
                this.devicesCombo.SelectedIndexChanged -= new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
                this.devicesCombo.SelectedIndex = indexToSelect;
                this.devicesCombo.SelectedIndexChanged += new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
            } else {
                this.devicesCombo.SelectedIndex = indexToSelect;
            }
        }

        // closing the main form
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e) {
            Logger.logTextLnU(DateTime.Now, "GrzMotion closed by user.");

            // IMessageFilter
            Application.RemoveMessageFilter(this);

            // shutdown webserver no matter what
            ImageWebServer.Stop();

            // stop ping looper task
            _runPing = false;

            // stop RTSP stream
            if ( (_rtspStream != null) && _rtspStream.GetStatus == RtspControl.Status.RUNNING ) {
                _rtspStream.Error -= rtspStream_Error;
                _rtspStream.NewFrame -= rtspStream_NewFrame;
                _rtspStream.StopRTSP();
                bool bruteForceStopNeeded = false;
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                do {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                } while ( sw.ElapsedMilliseconds < 500 );
                // normally CANCELLED is to expect, but EXCEPTION might be possible
                if ( _rtspStream != null && (_rtspStream.GetStatus == RtspControl.Status.CANCELLED || _rtspStream.GetStatus == RtspControl.Status.EXCEPTION) ) {
                    Logger.logTextLn(DateTime.Now, "MainForm_FormClosing: RTSP stream is stopped");
                } else {
                    bruteForceStopNeeded = true;
                }
                // brute force RTSP stream cancellation via aborting its parent thread
                if ( bruteForceStopNeeded ) {
                    if ( _rtspStreamThread != null ) {
                        Logger.logTextLn(DateTime.Now, "MainForm_FormClosing: RTSP stream stopping failed, now aborting the calling thread");
                        _rtspStreamThread.Abort();
                        if ( _rtspStreamThread.Join(500) ) {
                            Logger.logTextLn(DateTime.Now, String.Format("MainForm_FormClosing: _rtspStreamThread successfully aborted"));
                        } else {
                            Logger.logTextLn(DateTime.Now, String.Format("MainForm_FormClosing: _rtspStreamThread did not abort, giving up ..."));
                        }
                    } else {
                        Logger.logTextLn(DateTime.Now, String.Format("MainForm_FormClosing: _rtspStreamThread was already finished"));
                    }
                }
            }

            // stop UVC camera & store meaningful camera parameters
            if ( (_uvcDeviceSnap != null) && _uvcDeviceSnap.IsRunning ) {
                _uvcDeviceSnap.SignalToStop();
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                do {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                } while ( sw.ElapsedMilliseconds < 500 );
            }

            // stop UVC camera & store meaningful camera parameters
            if ( (_uvcDevice != null) && _uvcDevice.IsRunning ) {
                _uvcDevice.Stop();
                _uvcDevice.SignalToStop();
                _uvcDevice.NewFrame -= new AForge.Video.NewFrameEventHandler(uvcDevice_NewFrame);
                _uvcDevice.SetCameraProperty(CameraControlProperty.Exposure, -5, CameraControlFlags.Auto);
                _uvcDevice.SetVideoProperty(VideoProcAmpProperty.Brightness, -6, VideoProcAmpFlags.Auto);
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                do {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                } while ( sw.ElapsedMilliseconds < 500 );
                EnableConnectionControls(true);
                sw.Start();
                do {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                } while ( sw.ElapsedMilliseconds < 500 );
            }

            // INI: write to ini
            updateSettingsFromAppProperties();
            Settings.writePropertyGridToIni();
            // if app live cycle comes here, there was no app crash, write such info to ini for next startup log
            AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            ini.IniWriteValue("GrzMotion", "AppCrash", "False");
            // hard exit
            Environment.Exit(0);
        }

        // RTSP device is listed (if any ) at pos 0 in camera combobox
        private bool RtspDeviceExists() {
            bool retVal = false;
            if ( this.devicesCombo == null || this.devicesCombo.Items.Count == 0 ) {
                return retVal;
            }
            if ( this.devicesCombo.Items[0].ToString().StartsWith("rtsp", true, CultureInfo.InvariantCulture) ) {
                retVal = true;
            }
            return retVal;
        }

        // enable/disable camera connection related controls
        private void EnableConnectionControls(bool enable) {
            this.devicesCombo.Enabled = enable;
            this.uvcResolutionsCombo.Enabled = enable;
            this.connectButton.Text = enable ? _buttonConnectString : "-- stop --";
        }

        // enable/disable camera live related controls
        private void EnableLiveControls(bool enable) {
            this.buttonCameraProperties.Enabled = enable;
            this.hScrollBarExposure.Enabled = enable;
            this.buttonDefaultCameraProps.Enabled = enable;
            this.buttonAutoExposure.Enabled = enable;
        }

        // video device selection was changed
        private void devicesCombo_SelectedIndexChanged(object sender, EventArgs e) {
            int offset = RtspDeviceExists() ? 1 : 0;
            string device = this.devicesCombo.Items[this.devicesCombo.SelectedIndex].ToString();
            if ( device.StartsWith("rtsp", true, CultureInfo.InvariantCulture) ) {
                this.uvcResolutionsCombo.Items.Clear();
                EnableLiveControls(false);
                Settings.ImageSource = ImageSourceType.RTSP;
                this.SourceResolution = Settings.RtspResolution;
            } else {
                EnableLiveControls(true);
                if ( _uvcDevices.Count != 0 ) {
                    _uvcDevice = new VideoCaptureDevice(_uvcDevices[devicesCombo.SelectedIndex - offset].MonikerString);
                    Settings.CameraMoniker = _uvcDevices[devicesCombo.SelectedIndex - offset].MonikerString;
                    Settings.ImageSource = ImageSourceType.UVC;
                    EnumerateSupportedFrameSizes(_uvcDevice);
                    this.SourceResolution = Settings.CameraResolution;
                }
            }
        }

        // collect supported video frame sizes for USV devices in a combo box
        private void EnumerateSupportedFrameSizes(VideoCaptureDevice videoDevice) {
            this.Cursor = Cursors.WaitCursor;
            this.uvcResolutionsCombo.Items.Clear();
            try {
                int indexToSelect = 0;
                int ndx = 0;
                foreach ( VideoCapabilities capabilty in videoDevice.VideoCapabilities ) {
                    string currRes = string.Format("{0} x {1}", capabilty.FrameSize.Width, capabilty.FrameSize.Height);
                    // for unknown reason 'videoDevice.VideoCapabilities' sometimes contains all resolutions of a given camera twice
                    if ( this.uvcResolutionsCombo.FindString(currRes) == -1 ) {
                        this.uvcResolutionsCombo.Items.Add(currRes);
                    }
                    if ( currRes == String.Format("{0} x {1}", Settings.CameraResolution.Width, Settings.CameraResolution.Height) ) {
                        indexToSelect = ndx;
                    }
                    ndx++;
                }
                if ( videoDevice.VideoCapabilities.Length > 0 ) {
                    this.uvcResolutionsCombo.SelectedIndex = indexToSelect;
                }
            } finally {
                this.Cursor = Cursors.Default;
            }
        }

        // camera resolution was changed
        private void uvcResolutionsCombo_SelectedIndexChanged(object sender, EventArgs e) {
            // get altered video resolution
            if ( (_uvcDevice.VideoCapabilities != null) && (_uvcDevice.VideoCapabilities.Length != 0) ) {
                _uvcDevice.VideoResolution = _uvcDevice.VideoCapabilities[this.uvcResolutionsCombo.SelectedIndex];
                Settings.CameraResolution = new Size(_uvcDevice.VideoCapabilities[this.uvcResolutionsCombo.SelectedIndex].FrameSize.Width, _uvcDevice.VideoCapabilities[this.uvcResolutionsCombo.SelectedIndex].FrameSize.Height);
                this.SourceResolution = Settings.CameraResolution;
            }
        }

        // "Start" button clicked
        private void connectButton_Click(object sender, EventArgs e) {
            // restore PictureBox after 'Big Red Cross' exception
            ResetExceptionState(this.PictureBox);

            string device = this.devicesCombo.Items[this.devicesCombo.SelectedIndex].ToString();
            if ( device.StartsWith("rtsp", true, CultureInfo.InvariantCulture) ) {
                // RTSP streaming device
                if ( _buttonConnectString == this.connectButton.Text ) {
                    // UI controls
                    EnableConnectionControls(false);
                    EnableLiveControls(false);
                    // RTSP stream is accessible thru the following object// raises an event, if a new frame arrived 
                    _rtspStream = new RtspControl();
                    _rtspStream.NewFrame += rtspStream_NewFrame; // raises an event, if a new frame arrived 
                    _rtspStream.Error += rtspStream_Error;       // raises an error, if it happens internally 
                    // a separate thread as a measure of last resort to end a hanging RTSP client
                    if ( _rtspStreamThread != null ) {
                        _rtspStreamThread.Abort();
                    }
                    // fire & forget: extra thread, in case _rtspStream would not error out
                    _rtspStreamThread = new Thread(() => {
                        // runs until RTSP stream is stopped or error
                        _rtspStream.StartRTSP(this.SourceResolution, Settings.RtspConnectUrl);
                        Logger.logTextLn(DateTime.Now, "connectButton_Click: _rtspStreamThread init done");
                    });
                    _rtspStreamThread.Start();
                    // init flag 
                    _justConnected = true;
                    // minimize app if set
                    if ( Settings.DetectMotion && Settings.MinimizeApp) {
                        MinimizeApp();
                    }
                    Logger.logTextLn(DateTime.Now, "connectButton_Click: RTSP stream started");
                } else {
                    Logger.logTextLn(DateTime.Now, "connectButton_Click: RTSP stream is about to stop");
                    // shutdown webserver no matter what
                    ImageWebServer.Stop();
                    // disconnect handlers
                    _rtspStream.Error -= rtspStream_Error;
                    _rtspStream.NewFrame -= rtspStream_NewFrame;
                    // RTSP stop
                    _rtspStream.StopRTSP();
                    // wait a max of 5 seconds for cancellation: if it takes longer, brute force will be needed
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    do {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(100);
                        // normally CANCELLED is to expect, but EXCEPTION might be possible
                        if ( _rtspStream != null && (_rtspStream.GetStatus == RtspControl.Status.CANCELLED || _rtspStream.GetStatus == RtspControl.Status.EXCEPTION) ) {
                            Logger.logTextLn(DateTime.Now, "connectButton_Click: RTSP stream is stopped");
                            EnableConnectionControls(true);
                            return;
                        }
                    } while ( sw.ElapsedMilliseconds < 5000 );

                    // brute force RTSP stream cancellation via aborting its parent thread
                    if ( _rtspStreamThread != null ) {
                        Logger.logTextLn(DateTime.Now, "connectButton_Click: RTSP stream stopping failed, now aborting the calling thread");
                        _rtspStreamThread.Abort();
                        if ( _rtspStreamThread.Join(500) ) {
                            Logger.logTextLnU(DateTime.Now, String.Format("connectButton_Click: _rtspStreamThread successfully aborted"));
                        } else {
                            Logger.logTextLnU(DateTime.Now, String.Format("connectButton_Click: _rtspStreamThread did not abort, giving up ..."));
                        }
                    } else {
                        Logger.logTextLnU(DateTime.Now, String.Format("connectButton_Click: _rtspStreamThread was already finished"));
                    }
                    EnableConnectionControls(true);
                }
            } else {
                // UVC camera
                if ( _buttonConnectString == this.connectButton.Text ) {
                    // only connect if feasible
                    if ( (_uvcDevice == null) || (_uvcDevice.VideoCapabilities == null) || (_uvcDevice.VideoCapabilities.Length == 0) || (this.uvcResolutionsCombo.Items.Count == 0) ) {
                        return;
                    }
                    Logger.logTextLn(DateTime.Now, "connectButton_Click: start UVC camera");
                    _uvcDevice = new VideoCaptureDevice(Settings.CameraMoniker);
                    _uvcDevice.VideoResolution = _uvcDevice.VideoCapabilities[uvcResolutionsCombo.SelectedIndex];
                    _uvcDevice.Start();
                    _uvcDevice.NewFrame += new AForge.Video.NewFrameEventHandler(uvcDevice_NewFrame);
                    _justConnected = true;
                    // in case, the _uvcDevice won't start within 10s or _justConnected is still true (aka no uvcDevice_NewFrame event)
                    Task.Delay(10000).ContinueWith(t => {
                        Invoke(new Action(() => {
                            // trigger for delayed action: camera is clicked on, aka  shows '- stop -'  AND camera is not running OR no new frame event happened
                            if ( _buttonConnectString != this.connectButton.Text && (!_uvcDevice.IsRunning || _justConnected) ) {
                                if ( _uvcDeviceRestartCounter < 5 ) {
                                    _uvcDeviceRestartCounter++;
                                    if ( !_uvcDevice.IsRunning ) {
                                        Logger.logTextLn(DateTime.Now, String.Format("connectButton_Click: _uvcDevice is not running"));
                                    }
                                    if ( _justConnected ) {
                                        Logger.logTextLn(DateTime.Now, String.Format("connectButton_Click: no uvcDevice_NewFrame event received"));
                                    }
                                    // stop camera 
                                    this.connectButton.PerformClick();
                                    // wait
                                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                                    sw.Start();
                                    do {
                                        Application.DoEvents();
                                        System.Threading.Thread.Sleep(100);
                                    } while ( sw.ElapsedMilliseconds < 5000 );
                                    // reset all camera properties to camera default values:
                                    //         for instance, OV5640 vid_05a3&pid_9520 stops working after gotten fooled with awkward exposure params
                                    this.buttonDefaultCameraProps.PerformClick();
                                    // start camera
                                    this.connectButton.PerformClick();
                                } else {
                                    // give up note
                                    Logger.logTextLn(DateTime.Now, String.Format("connectButton_Click: _uvcDeviceRestartCounter >= 5, giving up in current app session"));
                                    // stop camera
                                    this.connectButton.PerformClick();
                                    this.Text = "!! UVC camera failure !!";
                                }
                            }
                        }));
                    });
                    // get camera auto exposure status
                    CameraControlFlags flag = getCameraExposureAuto();
                    this.buttonAutoExposure.Enabled = !(flag == CameraControlFlags.Auto);
                    Settings.ExposureAuto = (flag == CameraControlFlags.Auto);
                    // get camera exposure range parameters
                    int min, max, step, def, value;
                    CameraControlFlags cFlag;
                    _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out step, out def, out cFlag);
                    this.hScrollBarExposure.Maximum = max;
                    this.hScrollBarExposure.Minimum = min;
                    this.hScrollBarExposure.SmallChange = step;
                    this.hScrollBarExposure.LargeChange = step;
                    if ( Settings.ExposureAuto ) {
                        this.hScrollBarExposure.Value = def;
                    } else {
                        _uvcDevice.GetCameraProperty(CameraControlProperty.Exposure, out value, out cFlag);
                        this.hScrollBarExposure.Value = value;
                    }
                    Settings.ExposureVal = hScrollBarExposure.Value;
                    Settings.ExposureMin = min;
                    Settings.ExposureMax = max;
                    this.toolTip.SetToolTip(this.hScrollBarExposure, "camera exposure time = " + Settings.ExposureVal.ToString() + " (" + this.hScrollBarExposure.Minimum.ToString() + ".." + this.hScrollBarExposure.Maximum.ToString() + ")");
                    // prepare for camera exposure time monitoring / adjusting
                    GrayAvgBuffer.ResetData();
                    // disable camera combos
                    EnableConnectionControls(false);
                    // minimize app if set
                    if ( Settings.DetectMotion && Settings.MinimizeApp) {
                        MinimizeApp();
                    }
                    // init done
                    Logger.logTextLn(DateTime.Now, "connectButton_Click: start UVC camera done");
                    //
                    // NOTE: as soon as camera works -> '_justConnected', the webserver is activated depending on Settings.RunWebserver 
                    //
                } else {
                    // shutdown webserver no matter what
                    ImageWebServer.Stop();
                    // disconnect means stop video device
                    if ( _uvcDevice.IsRunning ) {
                        _uvcDevice.NewFrame -= new AForge.Video.NewFrameEventHandler(uvcDevice_NewFrame);
                        _uvcDevice.Stop();
                        _uvcDevice.SignalToStop();
                    }
                    // some controls
                    EnableConnectionControls(true);
                    this.buttonAutoExposure.Enabled = true;
                    Logger.logTextLn(DateTime.Now, "connectButton_Click: stop UVC camera done");
                }
            }
        }

        // minimize app to tray
        void MinimizeApp() {
            System.Threading.Timer timer = null;
            timer = new System.Threading.Timer((obj) => {
                Invoke(new Action(() => {
                    this.WindowState = FormWindowState.Minimized;
                }));
                timer.Dispose();
            },
            null, 5000, System.Threading.Timeout.Infinite);
        }

        // control screenshot
        public Bitmap takeCtlScreenShot(Control ctl) {
            Point location = new Point();
            Invoke(new Action(() => { location = ctl.PointToScreen(Point.Empty); }));
            Bitmap bmp = new Bitmap(ctl.Width, ctl.Height, PixelFormat.Format32bppArgb);
            using ( Graphics g = Graphics.FromImage(bmp) ) {
                g.CopyFromScreen(location.X, location.Y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }
        // full form screenshot
        public Bitmap thisScreenShot() {
            var form = Form.ActiveForm;
            var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            return bmp;
        }
        // show current snapshot modeless in new window
        private void snapshotButton_Click(object sender, EventArgs e) {
            if ( _origFrame == null ) {
                return;
            }
            try {
                Bitmap snapshotFull = thisScreenShot();
                Bitmap snapshotCtl = takeCtlScreenShot(this.PictureBox);
                SnapshotForm snapshotForm = new SnapshotForm(snapshotFull, snapshotCtl, (Bitmap)_origFrame.Clone());
                snapshotForm.Show();
            } catch {
                Logger.logTextLnU(DateTime.Now, "snapshotButton_Click: exception");
            }
        }

        // extended camera props dialog
        private void buttonCameraProperties_Click(object sender, EventArgs e) {
            if ( _uvcDevice != null ) {
                try {
                    // providing a handle makes the dialog modal, aka UI blocking
                    _uvcDevice.DisplayPropertyPage(this.Handle);
                } catch {
                    Logger.logTextLnU(DateTime.Now, "buttonProperties_Click: Cannot connect to camera properties");
                }
                // since the above dialog is modal, the only way to get here, is after the camera property dialog was closed
                updateUiCameraProperties();
            }
        }

        // update the few UI camera controls
        private void updateUiCameraProperties() {
            // get camera auto exposure status
            CameraControlFlags flag = getCameraExposureAuto();
            this.buttonAutoExposure.Enabled = !(flag == CameraControlFlags.Auto);
            Settings.ExposureAuto = (flag == CameraControlFlags.Auto);
            // get camera exposure value
            if ( Settings.ExposureAuto ) {
                // if auto exposure, camera exposure time is def 
                int min, max, step, def;
                _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out step, out def, out flag);
                this.hScrollBarExposure.Value = def;
            } else {
                int value;
                CameraControlFlags controlFlags;
                _uvcDevice.GetCameraProperty(CameraControlProperty.Exposure, out value, out controlFlags);
                this.hScrollBarExposure.Value = value;
            }
            Settings.ExposureVal = hScrollBarExposure.Value;
            this.toolTip.SetToolTip(this.hScrollBarExposure, "camera exposure time = " + Settings.ExposureVal.ToString() + " (" + this.hScrollBarExposure.Minimum.ToString() + ".." + this.hScrollBarExposure.Maximum.ToString() + ")");
            // needed to update the scroller according to the new value
            this.PerformLayout();
        }

        // force camera to set exposure time to automatic
        private void buttonAutoExposure_Click(object sender, EventArgs e) {
            if ( _uvcDevice == null ) {
                return;
            }
            if ( setCameraExposureAuto() != CameraControlFlags.Auto ) {
                Logger.logTextLnU(DateTime.Now, "buttonAutoExposure_Click: Cannot set camera exposure time to automatic.");
            }
            updateUiCameraProperties();
        }

        // force camera to set all its properties to default values
        private void buttonDefaultCameraProps_Click(object sender, EventArgs e) {
            if ( _uvcDevice == null ) {
                return;
            }

            // camera props
            int min, max, step, def;
            CameraControlFlags cFlag;
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out step, out def, out cFlag);
            _uvcDevice.SetCameraProperty(CameraControlProperty.Exposure, def, CameraControlFlags.Auto);
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Focus, out min, out max, out step, out def, out cFlag);
            _uvcDevice.SetCameraProperty(CameraControlProperty.Focus, def, CameraControlFlags.Manual);
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Iris, out min, out max, out step, out def, out cFlag);
            _uvcDevice.SetCameraProperty(CameraControlProperty.Iris, def, CameraControlFlags.Manual);
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Pan, out min, out max, out step, out def, out cFlag);
            _uvcDevice.SetCameraProperty(CameraControlProperty.Pan, def, CameraControlFlags.Manual);
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Roll, out min, out max, out step, out def, out cFlag);
            _uvcDevice.SetCameraProperty(CameraControlProperty.Roll, def, CameraControlFlags.Manual);
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Tilt, out min, out max, out step, out def, out cFlag);
            _uvcDevice.SetCameraProperty(CameraControlProperty.Tilt, def, CameraControlFlags.Manual);
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Zoom, out min, out max, out step, out def, out cFlag);
            _uvcDevice.SetCameraProperty(CameraControlProperty.Zoom, def, CameraControlFlags.Manual);

            // video props
            VideoProcAmpFlags vFlag;
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.BacklightCompensation, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.BacklightCompensation, def, VideoProcAmpFlags.Manual);
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.Brightness, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.Brightness, def, VideoProcAmpFlags.Manual);
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.ColorEnable, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.ColorEnable, def, VideoProcAmpFlags.Manual);
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.Contrast, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.Contrast, def, VideoProcAmpFlags.Manual);
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.Gain, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.Gain, def, VideoProcAmpFlags.Manual);
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.Gamma, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.Gamma, def, VideoProcAmpFlags.Manual);
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.Hue, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.Hue, def, VideoProcAmpFlags.Manual);
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.Saturation, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.Saturation, def, VideoProcAmpFlags.Manual);
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.Sharpness, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.Sharpness, def, VideoProcAmpFlags.Manual);
            _uvcDevice.GetVideoPropertyRange(VideoProcAmpProperty.WhiteBalance, out min, out max, out step, out def, out vFlag);
            _uvcDevice.SetVideoProperty(VideoProcAmpProperty.WhiteBalance, def, VideoProcAmpFlags.Auto);

            // update UI
            updateUiCameraProperties();
        }

        // set/get camera to auto exposure time
        private CameraControlFlags setCameraExposureAuto() {
            if ( _uvcDevice == null ) {
                return CameraControlFlags.None;
            }
            CameraControlFlags flag;
            int min, max, stp, def;
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out stp, out def, out flag); // only to get def
            _uvcDevice.SetCameraProperty(CameraControlProperty.Exposure, def, CameraControlFlags.Auto);                      // set def again  
            _uvcDevice.GetCameraProperty(CameraControlProperty.Exposure, out def, out flag);                                 // get flag again  
            return flag;
        }
        private CameraControlFlags getCameraExposureAuto() {
            if ( _uvcDevice == null ) {
                return CameraControlFlags.None;
            }
            CameraControlFlags flag;
            int intValue;
            _uvcDevice.GetCameraProperty(CameraControlProperty.Exposure, out intValue, out flag);
            return flag;
        }
        // set/get camera to exposure time
        private bool setCameraExposureTime(int expTime, out int newValue) {
            if ( _uvcDevice == null ) {
                newValue = -100;
                return false;
            }
            CameraControlFlags flag;
            int min, max, stp, def;
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out stp, out def, out flag);
            if ( expTime < min ) {
                expTime = min;
            }
            if ( expTime > max ) {
                expTime = max;
            }
            _uvcDevice.SetCameraProperty(CameraControlProperty.Exposure, expTime, CameraControlFlags.Manual);
            _uvcDevice.GetCameraProperty(CameraControlProperty.Exposure, out newValue, out flag);
            if ( newValue > max || newValue < min ) {
                return false;
            }
            return true;
        }
        private bool getCameraExposureTime(out int value) {
            if ( _uvcDevice == null ) {
                value = -100;
                return false;
            }
            CameraControlFlags flag;
            int min, max, stp, def;
            _uvcDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out stp, out def, out flag);
            _uvcDevice.GetCameraProperty(CameraControlProperty.Exposure, out value, out flag);
            if ( value > max || value < min ) {
                return false;
            }
            return true;
        }

        // set camera exposure & brightness manually via UI scrollers
        private void hScrollBarExposure_Scroll(object sender, ScrollEventArgs e) {
            this.buttonAutoExposure.Enabled = true;
            _uvcDevice.SetCameraProperty(CameraControlProperty.Exposure, this.hScrollBarExposure.Value, CameraControlFlags.Manual);
            Settings.ExposureVal = hScrollBarExposure.Value;
            Settings.ExposureAuto = false;
            this.toolTip.SetToolTip(this.hScrollBarExposure, "camera exposure time = " + Settings.ExposureVal.ToString() + " (" + this.hScrollBarExposure.Minimum.ToString() + ".." + this.hScrollBarExposure.Maximum.ToString() + ")");
        }

        // average green brightness of bmp
        public unsafe byte Bmp24bppToGreenAverage(Bitmap bmp) {
            if ( bmp.PixelFormat != PixelFormat.Format24bppRgb ) {
                return 0;
            }
            BitmapData bData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0 = (byte*)bData.Scan0.ToPointer();
            int lenBmpFull = bData.Stride * bmp.Height;
            int stepCount = 3 * 100;        // 1% of pixels should be enough
            double collector = 0;
            int divisor = 0;
            for ( int i = 0; i < lenBmpFull; i += stepCount ) {
                divisor++;
                collector += scan0[i + 1];  // just green is faster than real gray 
            }
            bmp.UnlockBits(bData);
            byte avgGreen = (byte)(collector / divisor);
            return avgGreen;
        }

        // EXPERIMENTAL: gray average ring buffer
        static class GrayAvgBuffer {
            static byte[] arr = new byte[3600]; // array with 3600 byte values
            private static int arrNdx = 0;      // active array index  
            private static int arrLevel = 0;    // current array level, could be smaller than length of array
            // set most recent gray value
            public static void SetLatestValue(byte value) {
                arr[arrNdx] = value;
                arrNdx++;
                if ( arrLevel < arr.Length ) {
                    arrLevel++;
                }
                if ( arrNdx >= arr.Length ) {
                    arrNdx = 0;
                }
            }
            // get trend of the last gray averages 
            public static double GetSlope() {
                // source https://classroom.synonym.com/f-value-statistics-6039.html
                double sumX = 0;
                double sumY = 0;
                double sumXxY = 0;
                double sumXsq = 0;
                for ( int i = 0; i < arrLevel; i++ ) {
                    sumXxY += i * arr[i];
                    sumX += i;
                    sumY += arr[i];
                    sumXsq += i * i;
                }
                double A = arrLevel * sumXxY;
                double B = sumX * sumY;
                double C = arrLevel * sumXsq;
                double D = sumX * sumX;
                double m = (A - B) / (C - D);
                return m;
            }
            // reset all history data in array
            public static void ResetData() {
                arr = new byte[3600];
                arrNdx = 0;
                arrLevel = 0;
            }
        }

        // Bitmap ring buffer for 5 images
        static class BmpRingBuffer {
            private static Bitmap[] bmpArr = new Bitmap[] { new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1) };
            private static int bmpNdx = 0;
            // public get & set
            public static Bitmap bmp {
                // always return the penultimate bmp
                get {
                    int prevNdx = bmpNdx - 1;
                    if ( prevNdx < 0 ) {
                        prevNdx = 4;
                    }
                    return bmpArr[prevNdx];
                }
                // override bmp in array and increase array index
                set {
                    bmpArr[bmpNdx].Dispose();
                    bmpArr[bmpNdx] = value;
                    bmpNdx++;
                    if ( bmpNdx > 4 ) {
                        bmpNdx = 0;
                    }
                }
            }
        }

        // dispose all RTSP ressources
        void DisposeRtsp() {
            // stop RTSP stream: normally should have ended
            if ( _rtspStream != null ) {
                _rtspStream.Error -= rtspStream_Error;
                _rtspStream.NewFrame -= rtspStream_NewFrame;
                _rtspStream.StopRTSP();
                bool bruteForceStopNeeded = false;
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                do {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                } while ( sw.ElapsedMilliseconds < 500 );
                // CANCELLED or EXCEPTION to expect
                if ( _rtspStream != null && (_rtspStream.GetStatus == RtspControl.Status.CANCELLED || _rtspStream.GetStatus == RtspControl.Status.EXCEPTION) ) {
                    Logger.logTextLn(DateTime.Now, "DisposeRtsp: RTSP stream is stopped");
                } else {
                    bruteForceStopNeeded = true;
                }
                // brute force RTSP stream cancellation via aborting its parent thread
                if ( bruteForceStopNeeded ) {
                    if ( _rtspStreamThread != null ) {
                        Logger.logTextLn(DateTime.Now, "DisposeRtsp: RTSP stream stopping failed, now aborting the calling thread");
                        _rtspStreamThread.Abort();
                        if ( _rtspStreamThread.Join(500) ) {
                            Logger.logTextLn(DateTime.Now, String.Format("DisposeRtsp: _rtspStreamThread successfully aborted"));
                        } else {
                            Logger.logTextLn(DateTime.Now, String.Format("DisposeRtsp: _rtspStreamThread did not abort, giving up ..."));
                        }
                    } else {
                        Logger.logTextLn(DateTime.Now, String.Format("DisposeRtsp: _rtspStreamThread was already finished"));
                    }
                }
            }
        }

        // RTSP error after exception, which aborts the loop around "rtspClient.ReceiveAsync"
        void rtspStream_Error() {
            if ( _rtspStream != null && this.connectButton.Text != _buttonConnectString ) {
                // happens, if RTSP camera turns off (like power off)
                if ( _rtspStream.GetStatus == RtspControl.Status.NORESPOND ) {
                    Logger.logTextLnU(DateTime.Now, String.Format("rtspStream_Error: camera obviously powered off, restart at next full quarter"));
                    ImageWebServer.Stop();
                    DisposeRtsp();
                    EnableConnectionControls(true);
                    // retry RTSP connection in timerFlowControl 15 minutes interval
                    Settings.RtspRetry = true;
                    return;
                }
                // any exception needs GrzMotion to restart
                if ( _rtspStream.GetStatus == RtspControl.Status.EXCEPTION ) {
                    _rtspDeviceExceptionCounter++;
                    // RTSP stream was previously started and now has an exception
                    Logger.logTextLnU(DateTime.Now, String.Format("rtspStream_Error: {0} exc_cnt={1}", _rtspStream.GetStatus.ToString(), _rtspDeviceExceptionCounter));
                    DisposeRtsp(); 
                    // set flag, that the following restart is not caused by a real app crash
                    if ( Settings.RtspRestartAppCount < 5 ) {
                        AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                        ini.IniWriteValue("GrzMotion", "AppCrash", "False");
                        // memorize count of app restarts, needed to avoid restart loops; flag is reset around midnight, so the game may begin on a new day again 
                        Settings.RtspRestartAppCount++;
                        ini.IniWriteValue("GrzMotion", "RtspRestartAppCount", Settings.RtspRestartAppCount.ToString());
                        // IMessageFilter
                        Application.RemoveMessageFilter(this);
                        // shutdown webserver no matter what
                        ImageWebServer.Stop();
                        // stop ping looper task
                        _runPing = false;
                        // restart GrzMotion
                        Logger.logTextLnU(DateTime.Now, String.Format("RTSP exception count {0} < 5, now restarting GrzMotion", _rtspDeviceExceptionCounter));
                        System.Diagnostics.Process.Start(Application.ExecutablePath);
                        Environment.Exit(0);
                    } else {
                        Logger.logTextLnU(DateTime.Now, String.Format("RTSP app restart count {0} >= 5, NOT restarting GrzMotion for today anymore", Settings.RtspRestartAppCount));
                    }
                } else {
                    Logger.logTextLnU(DateTime.Now, String.Format("RTSP error, continue w/o further action"));
                }
            }
        }

        // RTSP new frame event handler
        void rtspStream_NewFrame(Bitmap bmp) {
            // put recent image into ring buffer
            BmpRingBuffer.bmp = (Bitmap)bmp.Clone();
            // start motion detection after the first received image 
            if ( _justConnected ) {
                new Thread( () => rtspImageGrabber() ).Start();
                _justConnected = false;
            }
        }

        // image grabber for motion detection runs independent from frame event to ensure, frame rate being exact 2fps
        void rtspImageGrabber() {
            // on first enter flag
            bool firstImageProcessing = true;
            // stopwatch
            System.Diagnostics.Stopwatch swFrameProcessing = new System.Diagnostics.Stopwatch();
            DateTime lastFrameTime = DateTime.Now;
            Logger.logTextLn(DateTime.Now, String.Format("rtspImageGrabber: entering image processing loop"));
            // init such vars just once 
            int timestampHeight = 0;
            int timestampLength = 0;
            int oneCharStampLength = 0;
            int yFill = 0;
            int yDraw = 0;
            // dispose 'previous image', resolution might have changed
            if ( _prevFrame != null ) {
                _prevFrame.Dispose();
                _prevFrame = null;
            }
            // sync to motion count from today
            getTodaysMotionsCounters();

            int oomeCnt = 0;

            //
            // loop as long as camera is running
            //
            int excStep = -1;
            while ( _rtspStream.GetStatus == RtspControl.Status.RUNNING ) {

                // calc fps
                DateTime now = DateTime.Now;
                double revFps = (double)(now - lastFrameTime).TotalMilliseconds;
                lastFrameTime = now;
                _fps = 1000.0f / revFps;

                // measure consumed time for image processing
                swFrameProcessing.Restart();

                try {

                    // avoid Exception when GC is too slow
                    excStep = 0;
                    if ( _origFrame != null ) {
                        _origFrame.Dispose();
                    }

                    // get original frame from BmpRingBuffer
                    excStep = 1;
                    _origFrame = (Bitmap)BmpRingBuffer.bmp.Clone();

                    // prepare and add timestamp watermark + motions detected counter
                    if ( firstImageProcessing ) {
                        // reset exception counter
                        _rtspDeviceExceptionCounter = 0;
                        // some scalings depend on the captured image
                        timestampHeight = _origFrame.Height / 30;
                        _timestampFont = new Font("Arial", timestampHeight, FontStyle.Bold, GraphicsUnit.Pixel);
                        timestampHeight += 15;
                        _frameAspectRatio = (double)_origFrame.Width / (double)_origFrame.Height;
                        yFill = _origFrame.Height - timestampHeight;
                        yDraw = yFill + 5;
                        // for later processing scaled images are used 
                        if ( _origFrame.Width > 800 ) {
                            Settings.ScaledImageSize = new Size(800, (int)(800.0f / _frameAspectRatio));
                        } else {
                            Settings.ScaledImageSize = new Size(_origFrame.Width, _origFrame.Height);
                        }
                    }
                    excStep = 2;
                    try {
                        using ( var graphics = Graphics.FromImage(_origFrame) ) {
                            string text = now.ToString("yyyy.MM.dd HH:mm:ss_fff", System.Globalization.CultureInfo.InvariantCulture);
                            if ( firstImageProcessing ) {
                                timestampLength = (int)graphics.MeasureString(text, _timestampFont).Width + 10;
                                oneCharStampLength = (int)((double)timestampLength / 20.0f);
                            }
                            graphics.FillRectangle(Brushes.Yellow, 0, 0, timestampLength, timestampHeight);
                            graphics.DrawString(text, _timestampFont, Brushes.Black, 5, 5);
                            text = _motionsDetected.ToString() + "/" + _consecutivesDetected.ToString();
                            int xPos = _origFrame.Width - oneCharStampLength * text.Length - 5;
                            graphics.FillRectangle(Brushes.Yellow, xPos, yFill, _origFrame.Width, _origFrame.Height);
                            graphics.DrawString(text, _timestampFont, Brushes.Black, xPos, yDraw);
                            oomeCnt = 0;
                        }
                    } catch ( OutOfMemoryException oome ) {
                        oomeCnt++;
                        if ( oomeCnt > 10 ) {
                            Logger.logTextLnU(now, String.Format("rtspImageGrabber: excStep=2 oomeCnt = {0} giving up", oomeCnt));
                            break;
                        } else {
                            Logger.logTextLnU(now, String.Format("rtspImageGrabber: excStep=2 {0} continue ...", oome.Message));
                            System.Threading.Thread.Sleep(Math.Max(0, TWO_FPS - (int)_procMs));
                            continue;
                        }
                    }

                    // motion detector works with a scaled image, typically 800 x 600 or whatever fits to width 800 regarding image's aspect ratio
                    excStep = 3;
                    if ( _currFrame != null ) {
                        _currFrame.Dispose();
                    }
                    excStep = 4;
                    try {
                        _currFrame = resizeBitmap(_origFrame, Settings.ScaledImageSize);
                    } catch ( OutOfMemoryException oome ) {
                        oomeCnt++;
                        if ( oomeCnt > 10 ) {
                            Logger.logTextLnU(now, String.Format("rtspImageGrabber: excStep=4 oomeCnt = {0} giving up", oomeCnt));
                            break;
                        } else {
                            Logger.logTextLnU(now, String.Format("rtspImageGrabber: excStep=4 {0} continue ...", oome.Message));
                            System.Threading.Thread.Sleep(Math.Max(0, TWO_FPS - (int)_procMs));
                            continue;
                        }
                    }

                    // this will become the processed frame
                    excStep = 5;
                    if ( _procFrame != null ) {
                        _procFrame.Dispose();
                    }
                    _procFrame = (Bitmap)_currFrame.Clone();

                    // make one time sure, there is a previous image
                    excStep = 6;
                    if ( _prevFrame == null ) {
                        _prevFrame = (Bitmap)_currFrame.Clone();
                    }

                    // process image
                    excStep = 7;
                    if ( detectMotion(now, _currFrame, _prevFrame) ) {
                        // update counter
                        _motionsDetected++;
                        // RTSP is usually wide angle, allow snapshot if there is a good UVC
                        if ( Settings.RtspSnapshot ) {
                            excStep = 8;
                            Task.Run(() => {
                                MakeSnapshotWithUVC();
                            });
                        }
                    }

                    // show current, scaled and processed image in pictureBox, if not minimized
                    if ( WindowState != FormWindowState.Minimized ) {
                        excStep = 9;
                        PictureBox.Image = (Bitmap)_procFrame.Clone();
                    }

                    // if 1st image processing is done 
                    if ( firstImageProcessing ) {
                        firstImageProcessing = false;
                        // adjust the MainForm canvas matching to the presumable new aspect ratio of the bmp
                        Invoke(new Action(() => { adjustMainFormSize(_frameAspectRatio); }));
                        // handle webserver activity depending of Settings
                        if ( Settings.RunWebserver ) {
                            ImageWebServer.Start();
                        } else {
                            ImageWebServer.Stop();
                        }
                        Logger.logTextLn(now, String.Format("rtspImageGrabber: firstImageProcessing done"));
                    }

                    // finally make the current frame to the previous frame
                    excStep = 10;
                    _prevFrame.Dispose();
                    excStep = 11;
                    _prevFrame = (Bitmap)_currFrame.Clone();

                    // get process time in ms
                    swFrameProcessing.Stop();
                    _procMs = swFrameProcessing.ElapsedMilliseconds;
                    // calc some log statistics
                    if ( _procMs > _procMsMax ) {
                        _procMsMax = _procMs;
                        if ( _procMs > 450 ) {
                            _proc450Ms++;
                            Logger.logTextLn(now, String.Format("_procTime={0}", _procMs));
                        }
                    } else {
                        if ( _procMs < _procMsMin ) {
                            _procMsMin = _procMs;
                        }
                    }

                    // update title
                    Invoke(new Action(() => { headLine(); }));
                } catch ( Exception ex ) {
                    Logger.logTextLnU(now, String.Format("rtspImageGrabber: excStep={0} {1}", excStep, ex.Message));
                } finally {
                    // sleep for '500ms - process time' to ensure 2fps
                    System.Threading.Thread.Sleep(Math.Max(0, TWO_FPS - (int)_procMs));
                }
            }
            Logger.logTextLn(DateTime.Now, String.Format("rtspImageGrabber: RTSP stream not running"));
        }

        // camera new frame event handler for image processing
        void uvcDevice_NewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs) {
            // put recent image into ring buffer
            BmpRingBuffer.bmp = (Bitmap)eventArgs.Frame.Clone();
            // start motion detection after the first received image 
            if ( _justConnected ) {
                new Thread(() => cameraImageGrabber()).Start();
                _justConnected = false;
            }
        }
        // image grabber for motion detection runs independent from UVC camera new frame event to ensure, frame rate being exact 2fps
        void cameraImageGrabber() {
            // on first enter flag
            bool firstImageProcessing = true;
            // stopwatch
            System.Diagnostics.Stopwatch swFrameProcessing = new System.Diagnostics.Stopwatch();
            DateTime lastFrameTime = DateTime.Now;
            Logger.logTextLn(DateTime.Now, String.Format("cameraImageGrabber: entering image processing loop"));
            // init such vars just once 
            int timestampHeight = 0;
            int timestampLength = 0;
            int oneCharStampLength = 0;
            int yFill = 0;
            int yDraw = 0;
            // dispose 'previous image', camera resolution might have changed
            if ( _prevFrame != null ) {
                _prevFrame.Dispose();
                _prevFrame = null;
            }
            // sync to motion count from today
            getTodaysMotionsCounters();
            // camera sanity check
            if ( !_uvcDevice.IsRunning ) {
                Logger.logTextLn(DateTime.Now, String.Format("cameraImageGrabber: _uvcDevice is not running"));
            }

            //
            // loop as long as camera is running
            //
            int excStep = -1;
            while ( _uvcDevice.IsRunning ) {

                // calc fps
                DateTime now = DateTime.Now;
                double revFps = (double)(now - lastFrameTime).TotalMilliseconds;
                lastFrameTime = now;
                _fps = 1000.0f / revFps;

                // measure consumed time for image processing
                swFrameProcessing.Restart();

                try {

                    // avoid Exception when GC is too slow
                    excStep = 0;
                    if ( _origFrame != null ) {
                        _origFrame.Dispose();
                    }

                    // get original frame from BmpRingBuffer
                    excStep = 1;
                    _origFrame = (Bitmap)BmpRingBuffer.bmp.Clone();

                    // prepare and add timestamp watermark + motions detected counter
                    if ( firstImageProcessing ) {
                        timestampHeight = _origFrame.Height / 30;
                        _timestampFont = new Font("Arial", timestampHeight, FontStyle.Bold, GraphicsUnit.Pixel);
                        timestampHeight += 15;
                        _frameAspectRatio = (double)_origFrame.Width / (double)_origFrame.Height;
                        yFill = _origFrame.Height - timestampHeight;
                        yDraw = yFill + 5;
                        // for later processing scaled images are used 
                        if ( _origFrame.Width > 800 ) {
                            Settings.ScaledImageSize = new Size(800, (int)(800.0f / _frameAspectRatio));
                        } else {
                            Settings.ScaledImageSize = new Size(_origFrame.Width, _origFrame.Height);
                        }
                    }
                    excStep = 2;
                    using ( var graphics = Graphics.FromImage(_origFrame) ) {
                        string text = now.ToString("yyyy.MM.dd HH:mm:ss_fff", System.Globalization.CultureInfo.InvariantCulture);
                        if ( firstImageProcessing ) {
                            timestampLength = (int)graphics.MeasureString(text, _timestampFont).Width + 10;
                            oneCharStampLength = (int)((double)timestampLength / 20.0f);
                        }
                        graphics.FillRectangle(Brushes.Yellow, 0, 0, timestampLength, timestampHeight);
                        graphics.DrawString(text, _timestampFont, Brushes.Black, 5, 5);
                        text = _motionsDetected.ToString() + "/" + _consecutivesDetected.ToString();
                        int xPos = _origFrame.Width - oneCharStampLength * text.Length - 5;
                        graphics.FillRectangle(Brushes.Yellow, xPos, yFill, _origFrame.Width, _origFrame.Height);
                        graphics.DrawString(text, _timestampFont, Brushes.Black, xPos, yDraw);
                    }

                    // motion detector works with a scaled image, typically 800 x 600
                    excStep = 3;
                    if ( _currFrame != null ) {
                        _currFrame.Dispose();
                    }
                    excStep = 4;
                    _currFrame = resizeBitmap(_origFrame, Settings.ScaledImageSize);

                    // this will become the processed frame
                    excStep = 5;
                    if ( _procFrame != null ) {
                        _procFrame.Dispose();
                    }
                    _procFrame = (Bitmap)_currFrame.Clone();

                    // make one time sure, there is a previous image
                    excStep = 6;
                    if ( _prevFrame == null ) {
                        _prevFrame = (Bitmap)_currFrame.Clone();
                    }

                    // process image
                    excStep = 7;
                    if ( detectMotion(now, _currFrame, _prevFrame) ) {
                        _motionsDetected++;
                    }

                    // show current, scaled and processed image in pictureBox, if not minimized
                    excStep = 8;
                    if ( this.WindowState != FormWindowState.Minimized ) {
                        excStep = 9;
                        this.PictureBox.Image = (Bitmap)_procFrame.Clone();
                    }

                    // if 1st image processing is done 
                    if ( firstImageProcessing ) {
                        firstImageProcessing = false;
                        // adjust the MainForm canvas matching to the presumable new aspect ratio of the bmp
                        Invoke(new Action(() => { adjustMainFormSize(_frameAspectRatio); }));
                        // handle webserver activity depending of Settings
                        if ( Settings.RunWebserver ) {
                            ImageWebServer.Start();
                        } else {
                            ImageWebServer.Stop();
                        }
                        Logger.logTextLn(now, String.Format("cameraImageGrabber: firstImageProcessing done"));
                    }

                    // finally make the current frame to the previous frame
                    excStep = 10;
                    _prevFrame.Dispose();
                    excStep = 11;
                    _prevFrame = (Bitmap)_currFrame.Clone();

                    // get process time in ms
                    swFrameProcessing.Stop();
                    _procMs = swFrameProcessing.ElapsedMilliseconds;
                    // calc some log statistics
                    if ( _procMs > _procMsMax ) {
                        _procMsMax = _procMs;
                        if ( _procMs > 450 ) {
                            _proc450Ms++;
                            Logger.logTextLn(now, String.Format("_procTime={0}", _procMs));
                        }
                    } else {
                        if ( _procMs < _procMsMin ) {
                            _procMsMin = _procMs;
                        }
                    }

                    // update title
                    Invoke(new Action(() => { headLine(); }));

                } catch ( Exception ex ) {
                    Logger.logTextLnU(now, String.Format("cameraImageGrabber: excStep={0} {1}", excStep, ex.Message));
                } finally {
                    // sleep for '500ms - process time' to ensure 2fps
                    System.Threading.Thread.Sleep(Math.Max(0, TWO_FPS - (int)_procMs));
                }
            }
            Logger.logTextLn(DateTime.Now, String.Format("cameraImageGrabber: camera not running"));
        }

        // resize bitmap and keep pixel format
        public static Bitmap resizeBitmap(Bitmap imgToResize, Size size) {
            Bitmap rescaled = new Bitmap(size.Width, size.Height, imgToResize.PixelFormat);
            using ( Graphics g = Graphics.FromImage(rescaled) ) {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;  // significant cpu load decrease 
                g.DrawImage(imgToResize, 0, 0, size.Width, size.Height);
            }
            return rescaled;
        }

        // return RGB pixel data as two boxed (boxDim x boxDim) gray byte arrays
        public unsafe void TwoBmp24bppToGray8bppByteArrayScaledBox(Bitmap bmp_1, out byte[] arr_1, Bitmap bmp_2, out byte[] arr_2, int boxDim) {
            // sanity checks to make sure. both bmp have matching dimensions 
            arr_1 = new byte[1];
            arr_2 = new byte[1];
            if ( bmp_1.PixelFormat != PixelFormat.Format24bppRgb || bmp_2.PixelFormat != PixelFormat.Format24bppRgb ) {
                return;
            }
            if ( (bmp_1.Width != bmp_2.Width) || (bmp_1.Height != bmp_2.Height) ) {
                return;
            }
            // needed later
            uint boxDimSquare = (uint)boxDim * (uint)boxDim;
            // box dimension constraints
            int arrWidth = bmp_1.Width / boxDim;
            int arrHeight = bmp_1.Height / boxDim;
            // adjusted bmp width and height (adjusted values are a multiple to boxDim)
            int bmpWidth = arrWidth * boxDim;
            int bmpFullWidth = bmpWidth * 3;
            int bmpHeight = arrHeight * boxDim;
            // temporary buffers for data of a "new" row after boxDim x boxDim shrinking
            int tmpLen = (int)(bmpWidth / boxDim);
            uint[] tmp_1 = new uint[tmpLen];
            uint[] tmp_2 = new uint[tmpLen];
            // final array size
            arr_1 = new byte[arrWidth * arrHeight];
            arr_2 = new byte[arrWidth * arrHeight];
            int arrNdx = 0;
            // bmp pointers to two Bitmaps
            BitmapData bmpData_1 = bmp_1.LockBits(new Rectangle(0, 0, bmp_1.Width, bmp_1.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0_1 = (byte*)bmpData_1.Scan0.ToPointer();
            BitmapData bmpData_2 = bmp_2.LockBits(new Rectangle(0, 0, bmp_2.Width, bmp_2.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0_2 = (byte*)bmpData_2.Scan0.ToPointer();
            // loop bmp height
            for ( int y = 0; y < bmpHeight; ) {
                // loop b times boxDim rows and increment y with each b increment
                for ( int b = 0; b < boxDim; b++, y++ ) {
                    // offset to data pointer goes with the full width of bmp, aka Stride
                    int scanOfs = y * bmpData_1.Stride;
                    // loop a full row width in x and fill the tmp array along it
                    for ( int x = 0; x < bmpFullWidth; x += 3 ) {
                        // one pixel gray value
                        byte gray1 = (byte)(0.3f * (float)scan0_1[scanOfs + x + 0] + 0.6f * (float)scan0_1[scanOfs + x + 1] + 0.1f * (float)scan0_1[scanOfs + x + 2]);
                        byte gray2 = (byte)(0.3f * (float)scan0_2[scanOfs + x + 0] + 0.6f * (float)scan0_2[scanOfs + x + 1] + 0.1f * (float)scan0_2[scanOfs + x + 2]);
                        // tmpNdx shall alter after one boxDim pixels are collected
                        int tmpNdx = (x / 3) / boxDim;
                        // add gray values of boxDim pixels and store them in tmp
                        tmp_1[(uint)tmpNdx] += gray1;
                        tmp_2[(uint)tmpNdx] += gray2;
                    }
                }
                // now two shrunk rows are ready
                for ( int t = 0; t < tmpLen; t++ ) {
                    // make avg Pixel of a XxX box
                    tmp_1[t] = (tmp_1[t] / boxDimSquare);
                    tmp_2[t] = (tmp_2[t] / boxDimSquare);
                    // get both tmp arrays to buf arrays
                    arr_1[arrNdx] = (byte)tmp_1[t];
                    arr_2[arrNdx] = (byte)tmp_2[t];
                    arrNdx++;
                    // tmp clear
                    tmp_1[t] = 0;
                    tmp_2[t] = 0;
                }
            }
            // unlock Bitmaps
            bmp_1.UnlockBits(bmpData_1);
            bmp_2.UnlockBits(bmpData_2);
        }

        // write boxed (boxDim x boxDim) gray byte array into a larger 24bppRgb color bmp as overlay
        public unsafe Bitmap ScaledBoxGray8bppByteArrayToBmp24bppOverlay(Bitmap ori, Rectangle rcDest, byte[] arr, int boxDim, bool motionInRectDetected) {
            // box dimension constraints
            int arrHeight = rcDest.Height / boxDim;
            int arrWidth = rcDest.Width / boxDim;
            // adjusted rcDest width and height to assure such values are a multiple of boxDim
            int rcDestHeight = arrHeight * boxDim;
            int rcDestWidth = arrWidth * boxDim;
            int rcDestFullWidth = rcDestWidth * 3;
            // scan0 y offset goes with the full ori Bitmap width
            int oriFullWidth = ori.Width * 3;
            // pointer to start in the original Bitmap is determined by the upper left corner of rcDest
            BitmapData bmpData = ori.LockBits(rcDest, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0 = (byte*)bmpData.Scan0.ToPointer();
            // loop over rcDest height
            for ( int y = 0; y < rcDestHeight; y++ ) {
                // y offset for pointer goes with the full ori Bitmap width (Stride), scanOfsY stands always at left border of rcDest
                int scanOfsY = y * bmpData.Stride;
                // offset in arr is changed every 8th row of rcDestHeight
                int arrNdxY = (int)(y / boxDim) * arrWidth;
                // loop over the full length of a row in rcDest
                for ( int x = 0; x < rcDestFullWidth; x += 3 ) {
                    // arr index changes every 8th full column in rcDest
                    int arrNdxX = (int)((x / 3) / boxDim);
                    int arrNdxFin = arrNdxY + arrNdxX;
                    // obtain gray value from box
                    int gray = arr[arrNdxFin];
                    // 255 is an indication for a _roi[i].thresholdIntensity exceed
                    if ( gray == 255 ) {
                        if ( motionInRectDetected ) {
                            // set Pixel in rcDest to transparent red
                            scan0[scanOfsY + x + 2] = 255;
                        } else {
                            // set Pixel in output image to 'sort of white/gray'
                            scan0[scanOfsY + x + 0] = (byte)Math.Min(255, scan0[scanOfsY + x + 0] + 50);
                            scan0[scanOfsY + x + 1] = (byte)Math.Min(255, scan0[scanOfsY + x + 1] + 50);
                            scan0[scanOfsY + x + 2] = (byte)Math.Min(255, scan0[scanOfsY + x + 2] + 50);
                        }
                    }
                }
            }
            ori.UnlockBits(bmpData);
            return ori;
        }

        //
        // motion detector process image method
        //
        bool detectMotion(DateTime nowFile, Bitmap currFrame, Bitmap prevFrame) {

            // camera running w/o motion detection
            if ( !Settings.DetectMotion ) {
                return false;
            }

            // flags
            bool motionDetected = false;
            bool falsePositive = false;
            bool itsDarkOutside = false;

            // we have ROIs, each of them generates a tile out of the two images to compare
            for ( int i = 0; i < ROICOUNT; i++ ) {

                // only use a valid ROI
                if ( i >= _roi.Count ) {
                    break;
                }
                if ( _roi[i].rect.Width <= 0 ) {
                    continue;
                }
                // camera resolution might not fit to ROI
                if ( _roi[i].rect.X + _roi[i].rect.Width > currFrame.Width || _roi[i].rect.Y + _roi[i].rect.Height > currFrame.Height ) {
                    continue;
                }

                // number of pixels in the current tile
                double numberOfPixels = _roi[i].rect.Width * _roi[i].rect.Height;
                double currentPixelsChanged = 0;
                bool motionDetectedInRoi = false;

                // make two Bitmap tiles out of prevFrame and currFrame according to the active ROI
                Bitmap prevTile = prevFrame.Clone(_roi[i].rect, PixelFormat.Format24bppRgb);
                Bitmap currTile = currFrame.Clone(_roi[i].rect, PixelFormat.Format24bppRgb);

                // if reference roi
                if ( _roi[i].reference ) {
                    //  get the average gray value of the current tile
                    byte avgGrayCurr = Bmp24bppToGreenAverage(currTile);
                    // day / night flag
                    itsDarkOutside = (bool)(avgGrayCurr < Settings.NightThreshold);
                    // app could adjust camera exposure time by itself (fixes camera OV5640 with IR lens: sometimes tends to brightness jumps if ambient is very bright)
                    if ( Settings.ExposureByApp ) {
                        // store it in a buffer for further inspection
                        GrayAvgBuffer.SetLatestValue(avgGrayCurr);
                    }
                }

                // two tiles to compare, now as _roi[i].boxScaler x _roi[i].boxScaler boxed byte buffers for easy comparison
                TwoBmp24bppToGray8bppByteArrayScaledBox(prevTile, out byte[] prevBuf, currTile, out byte[] currBuf, _roi[i].boxScaler);

                // sanity check
                if ( currBuf.Length <= 1 ) {
                    continue;
                }

                // build a resulting buffer
                byte[] bufResu = new byte[currBuf.Length];

                // loop thru both tile buffers to compare pixel by pixel (which is actually the avg of a _roi[i].boxScaler box of pixels)
                for ( int pix = 0; pix < currBuf.Length; pix++ ) {
                    // simple difference of the two input tiles in a certain _roi[i].boxScaler box
                    bufResu[pix] = (byte)Math.Abs((int)currBuf[pix] - (int)prevBuf[pix]);
                    // the following threshold (must be larger than noise) decides, whether the pixel turns white/red or not
                    if ( bufResu[pix] >= _roi[i].thresholdIntensity ) {
                        // result image shall contain changes between the two above tile in red color (or white color, if pixel percent threshold is not reached)
                        bufResu[pix] = 255;
                        currentPixelsChanged++;
                    }
                }

                // check whether the change is considered as a motion
                currentPixelsChanged *= (_roi[i].boxScaler * _roi[i].boxScaler);
                currentPixelsChanged /= numberOfPixels;

                // pixel change is a potential motion
                if ( currentPixelsChanged > _roi[i].thresholdChanges ) {
                    // motion detected inside active ROI
                    motionDetectedInRoi = true;
                    // return value indicates, a motion took place
                    motionDetected = true;
                    // overvote indicator string default value
                    _strOverVoteFalsePositive = "";
                    // false positive motion, if reference ROI detects a motion
                    if ( _roi[i].reference ) {
                        falsePositive = true;
                    } else {
                        // false positive motion, if pixel change upper limit threshold is exceeded
                        if ( currentPixelsChanged >= _roi[i].thresholdUpperLimit ) {
                            falsePositive = true;
                        }
                    }
                    // an active motion sequence (if timer is enabled) overvotes any false positive status
                    if ( falsePositive && _timerMotionSequenceActive.Enabled ) {
                        falsePositive = false;
                        if ( Settings.DebugMotions ) {
                            _strOverVoteFalsePositive = "o";
                            Logger.logMotionListExtra(String.Format("{0} timerMotionSequenceActive overvote {1}", nowFile.ToString("HH-mm-ss_fff"), _motionsList.Count));
                        }
                    }
                }

                // draw bufResu into _procFrame, which is shown in UI
                _procFrame = ScaledBoxGray8bppByteArrayToBmp24bppOverlay(_procFrame, _roi[i].rect, bufResu, _roi[i].boxScaler, motionDetectedInRoi);

                // show the currently affected ROI + pixel change percentage
                using ( Graphics g = Graphics.FromImage(_procFrame) ) {
                    g.DrawRectangle(_roi[i].reference ? new Pen(Color.Yellow) : new Pen(Color.Red), _roi[i].rect);
                    if ( Settings.ShowPixelChangePercent ) {
                        g.FillRectangle(Brushes.Yellow, _roi[i].rect.X, _roi[i].rect.Y, 30, 17);
                        g.DrawString(String.Format("{0}{1}%", _strOverVoteFalsePositive, (int)(currentPixelsChanged * 100.0f)), _pctFont, Brushes.Black, _roi[i].rect.X, _roi[i].rect.Y);
                    }
                }

                // release the two Bitmap tiles
                prevTile.Dispose();
                currTile.Dispose();

            } // end of loop ROIs for motion detection

            // image to show on webserver
            if ( Settings.RunWebserver && ImageWebServer.IsRunning ) {
                ImageWebServer.Image = (Settings.WebserverImage == AppSettings.WebserverImageType.PROCESS) ?
                                                 (Bitmap)_procFrame.Clone() :
                                                 (Settings.WebserverImage == AppSettings.WebserverImageType.LORES) ?
                                                           (Bitmap)_currFrame.Clone() :
                                                           (Bitmap)_origFrame.Clone();
            }

            // if video will be generated, images captured between 19:00 ... 24:00 will be saved into the next day's folder --> adjust pathname
            DateTime nowPath = nowFile;
            if ( Settings.MakeDailyVideo ) {
                if ( DateTime.Now.TimeOfDay >= new System.TimeSpan(19, 0, 0) ) {
                    // jump one day forward
                    nowPath = nowPath.AddDays(1);
                }
            }

            // save motions needs useful filename & pathname based on timestamp
            _nowStringFile = nowFile.ToString("yyyy-MM-dd-HH-mm-ss_fff", CultureInfo.InvariantCulture);
            _nowStringPath = nowPath.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // false positive handling
            if ( falsePositive ) {
                // false positive images are no motion images
                motionDetected = false;
                // save lores fully processed false positive file for debug purposes
                if ( Settings.DebugFalsePositiveImages ) {
                    Task.Run(() => {
                        try {
                            // filename based on current time stamp
                            string path = System.IO.Path.Combine(Settings.StoragePath, _nowStringPath + "_false");
                            string fileName = System.IO.Path.Combine(path, _nowStringFile + ".jpg");
                            // storage directory
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fileName));
                            // save the 'false positive image'
                            Bitmap tmp = (Bitmap)_procFrame.Clone();
                            tmp.Save(fileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                            tmp.Dispose();
                        } catch ( Exception ex ) {
                            string msg = ex.Message;
                        }
                    });
                }
            }

            // a motion was detected
            if ( motionDetected ) {

                // save hires & lores images, post alarms
                if ( Settings.SaveMotion || Settings.SaveSequences || _alarmSequence || _alarmNotify ) {
                    try {
                        // storage directory is built from nowStringPath, which already takes care about image save >19:00 into the next day folder
                        string filePath = System.IO.Path.Combine(Settings.StoragePath, _nowStringPath);
                        System.IO.Directory.CreateDirectory(filePath);
                        // build filename from nowStringPath + simple time stamp
                        string fileName = System.IO.Path.Combine(filePath, _nowStringFile + ".jpg");
                        // in case of debug lores, filename is based on current time stamp, yet no need to create path
                        string pathDbg = System.IO.Path.Combine(Settings.StoragePath, _nowStringPath + "_proc");
                        string fileNameDbg = System.IO.Path.Combine(pathDbg, _nowStringFile + ".jpg");

                        // save current motion directly OR if it's dark outside (makes sure, nightly single events are saved)
                        bool motionSaved = false;
                        if ( Settings.SaveMotion || itsDarkOutside ) {
                            // save hires
                            Task.Run(() => {
                                _origFrame.Save(fileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                            });
                            // save lores fully processed file for debug purposes
                            if ( Settings.DebugProcessImages ) {
                                Task.Run(() => {
                                    try {
                                        // storage directory
                                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fileNameDbg));
                                        // save lores image
                                        Bitmap tmp = (Bitmap)_procFrame.Clone();
                                        tmp.Save(fileNameDbg, System.Drawing.Imaging.ImageFormat.Jpeg);
                                        tmp.Dispose();
                                    } catch ( Exception ex ) {
                                        string msg = ex.Message;
                                    }
                                });
                            }
                            // set flag
                            motionSaved = true;
                        }

                        // conditions to maintain _motionsList
                        if ( Settings.SaveSequences || _alarmSequence || _alarmNotify ) {

                            // save 'motion sequence data' either to list or add an info entry depending on 'motion save status'
                            if ( !motionSaved ) {
                                _motionsList.Add(new Motion(fileName, nowFile, (Bitmap)_origFrame.Clone(), fileNameDbg, Settings.DebugProcessImages ? (Bitmap)_procFrame.Clone() : null));
                            } else {
                                _motionsList.Add(new Motion(fileName, nowFile));
                            }

                            // debug motion list
                            if ( Settings.DebugMotions ) {
                                int i = _motionsList.Count - 1;
                                Motion m = _motionsList[i];
                                Logger.logMotionListEntry("detect", i, m.imageMotion != null, m.motionConsecutive, m.motionDateTime, m.motionSaved);
                            }

                            // need to wait for at least 3 queued images to allow some time comparison between the list entries
                            if ( _motionsList.Count > 2 ) {

                                // slightly faster using vars of array elements other than accessing array with index
                                Motion m1 = _motionsList[_motionsList.Count - 1];
                                Motion m2 = _motionsList[_motionsList.Count - 2];
                                Motion m3 = _motionsList[_motionsList.Count - 3];

                                // calc time differences: last to 3rd to last, last to penultimate, penultimate to 3rd to last
                                TimeSpan lastToThrd = m1.motionDateTime - m3.motionDateTime;
                                TimeSpan lastToPenu = m1.motionDateTime - m2.motionDateTime;
                                TimeSpan penuToThrd = m2.motionDateTime - m3.motionDateTime;
                                
                                // check if the current motion happened within certain time intervals to previous motions --> 'sequence'
                                if ( (lastToThrd.TotalSeconds < 2.5f) || ((lastToPenu.TotalSeconds < 1.5f) && (penuToThrd.TotalSeconds < 1.5f)) ) {

                                    // make the last three motions consecutive: !! m1, m2, m3 will change _motionsList[ndx] directly !!
                                    m3.motionConsecutive = m3.motionDateTime.Year != 1900 ? true : false;  // value '1900' acts as a video sequence stop marker
                                    m2.motionConsecutive = m2.motionDateTime.Year != 1900 ? true : false;
                                    m1.motionConsecutive = m1.motionDateTime.Year != 1900 ? true : false;
                                    if ( Settings.DebugMotions ) {
                                        int i = _motionsList.Count - 3;
                                        Logger.logMotionListEntry("consec", i, m3.imageMotion != null, m3.motionConsecutive, m3.motionDateTime, m3.motionSaved);
                                        Logger.logMotionListEntry("consec", i+1, m2.imageMotion != null, m2.motionConsecutive, m2.motionDateTime, m2.motionSaved);
                                        Logger.logMotionListEntry("consec", i+2, m1.imageMotion != null, m1.motionConsecutive, m1.motionDateTime, m1.motionSaved);
                                    }

                                    // timer indicates an active motion sequence, expires after 1s as single shot due to 'AutoReset = false'
                                    _timerMotionSequenceActive.Stop();
                                    _timerMotionSequenceActive.Start();

                                    // fire & forget
                                    Task.Run(() => {
                                        // save a consecutive image to disk (only @ 1st enter it's a sequence of three images)
                                        if ( Settings.SaveSequences ) {
                                            saveSequence();
                                        }
                                        // since motion sequence list is up to date, send ONE sequence photo via Telegram
                                        if ( _alarmNotify ) {
                                            sendAlarmNotification();
                                        }
                                    });

                                    // ASAP make a motion sequence video
                                    if ( _alarmSequence && _alarmSequenceAsap ) {
                                        bool continueWithVideoSequence = true;
                                        // limit sending a video sequence to one per 30s
                                        if ( (DateTime.Now - _lastSequenceSendTime).TotalSeconds < 30 ) {
                                            continueWithVideoSequence = false;
                                        }
                                        // have at least 7 consecutives
                                        int consecutiveCount = 0;
                                        for ( int i = _motionsList.Count - 1; i >= 0; i-- ) {
                                            if ( _motionsList[i].motionConsecutive ) {
                                                consecutiveCount++;
                                            } else {
                                                break;
                                            }
                                        }
                                        if ( consecutiveCount < 7 ) {
                                            continueWithVideoSequence = false;
                                        }
                                        if ( continueWithVideoSequence ) {
                                            // fire & forget is ok
                                            Task.Run(() => {
                                                // busy flag
                                                _alarmSequenceBusy = true;
                                                // time stamp
                                                _lastSequenceSendTime = DateTime.Now;
                                                // make a sub list containing the latest consecutive motions
                                                List<Motion> subList = new List<Motion>();
                                                int cnt = _motionsList.Count - 1;
                                                for ( int i = cnt; i >= 0; i-- ) {
                                                    if ( _motionsList[i].motionConsecutive ) {
                                                        subList.Insert(0, _motionsList[i]);
                                                    } else {
                                                        break;
                                                    }
                                                }
                                                // prevent to send the current motion sequence again, by placing a stopper into the motion list
                                                _motionsList.Add(new Motion("", new DateTime(1900, 01, 01)));
                                                if ( Settings.DebugMotions ) {
                                                    int i = _motionsList.Count - 1;
                                                    Motion m = _motionsList[i];
                                                    Logger.logMotionListEntry("alarm", i, m.imageMotion != null, m.motionConsecutive, m.motionDateTime, m.motionSaved);
                                                }
                                                // make latest motion video sequence, send it via Telegram and reset flag _alarmSequenceBusy when done
                                                makeMotionSequence(subList, this.SourceResolution);
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    } catch ( Exception ex ) {
                        string msg = ex.Message;
                        Logger.logTextLnU(DateTime.Now, "image save ex: " + msg);
                    }
                }

                // send motion alarm photo to Telegram: meet conditions and send all night events
                if ( _alarmNotify && (Settings.SaveMotion || itsDarkOutside) ) {
                    Task.Run(() => {
                        sendAsapNotification((Bitmap)_origFrame.Clone());
                    });
                }
            }

            // return assessment regarding motion detection
            return motionDetected;
        }

        // SaveMotion or night time combined with _alarmNotify posts all images
        void sendAsapNotification(Bitmap bmp) {
            try {
                // all alarm images are sent
                _Bot.SetCurrentAction(_notifyReceiver, ChatAction.UploadPhoto);
                byte[] buffer = bitmapToByteArray(bmp);
                bmp.Dispose();
                _Bot.SendPhoto(_notifyReceiver, buffer, "alarm", "alarm photo");
                Logger.logTextLn(DateTime.Now, "alarm photo sent");
            } catch ( Exception ex ) {
                Logger.logTextLnU(DateTime.Now, "_alarmNotify && Settings.SaveMotion: " + ex.Message);
            }
        }

        // supposed to save not yet saved motion images, if they are consecutive
        private void saveSequence() {
            // loop list
            int cnt = _motionsList.Count - 1;
            for ( int i = cnt; i >= 0; i-- ) {

                // debug non consecutive images
                if ( Settings.DebugNonConsecutives ) {
                    if ( !_motionsList[i].motionConsecutive ) {
                        // save lores if existing
                        if ( _motionsList[i].imageProc != null ) {
                            string pathNonC = System.IO.Path.GetDirectoryName(_motionsList[i].fileNameProc);
                            pathNonC = pathNonC.Substring(0, pathNonC.Length - 4) + "nonc";
                            string fileNonC = System.IO.Path.GetFileName(_motionsList[i].fileNameProc);
                            System.IO.Directory.CreateDirectory(pathNonC);
                            _motionsList[i].imageProc.Save(System.IO.Path.Combine(pathNonC, fileNonC), System.Drawing.Imaging.ImageFormat.Jpeg);
                            _motionsList[i].imageProc.Dispose();
                            _motionsList[i].imageProc = null;
                        }
                    }
                }
                // only consider existing images
                if ( _motionsList[i].imageMotion != null ) {
                    // further checks: consecutive + not saved + not locked
                    if ( _motionsList[i].motionConsecutive && !_motionsList[i].motionSaved && !_motionsList[i].bitmapLocked ) {
                        // set bitmap lock bit to prevent task overrun condition
                        _motionsList[i].bitmapLocked = true;
                        // save to disk may take some time
                        int execStep = 0;
                        try {
                            // save hires, inc counter, set 'save flag' & dispose
                            _motionsList[i].imageMotion.Save(_motionsList[i].fileNameMotion, System.Drawing.Imaging.ImageFormat.Jpeg);
                            execStep = 1;
                            _consecutivesDetected++;
                            _motionsList[i].motionSaved = true;
                            _motionsList[i].imageMotion.Dispose();
                            execStep = 2;
                            _motionsList[i].imageMotion = null;
                            // debug motion list
                            if ( Settings.DebugMotions ) {
                                Motion m = _motionsList[i];
                                Logger.logMotionListEntry("saved", i, m.imageMotion != null, m.motionConsecutive, m.motionDateTime, m.motionSaved);
                            }
                            // reset bitmap lock bit
                            _motionsList[i].bitmapLocked = false;
                            // save lores if existing
                            if ( _motionsList[i].imageProc != null ) {
                                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_motionsList[i].fileNameProc));
                                _motionsList[i].imageProc.Save(_motionsList[i].fileNameProc, System.Drawing.Imaging.ImageFormat.Jpeg);
                                _motionsList[i].imageProc.Dispose();
                                _motionsList[i].imageProc = null;
                            }
                        } catch ( Exception ex ) {
                            Logger.logTextLnU(DateTime.Now, "saveSequence ex: " + ex.Message + " execStep" + execStep.ToString() + " " + _motionsList[i].fileNameMotion);
                            if ( execStep == 0 ) {
                                // retry to save hires but make sure, the exception wasn't false flagged
                                try {
                                    if ( !System.IO.File.Exists(_motionsList[i].fileNameMotion) ) {
                                        Bitmap bmp = (Bitmap)_motionsList[i].imageMotion.Clone();
                                        bmp.Save(_motionsList[i].fileNameMotion, System.Drawing.Imaging.ImageFormat.Jpeg);
                                        _consecutivesDetected++;
                                        _motionsList[i].motionSaved = true;
                                        _motionsList[i].imageMotion.Dispose();
                                        _motionsList[i].imageMotion = null;
                                        bmp.Dispose();
                                        // reset bitmap lock bit
                                        _motionsList[i].bitmapLocked = false;
                                    } else {
                                        Logger.logTextLnU(DateTime.Now, "file was already saved: " + _motionsList[i].fileNameMotion);
                                    }
                                } catch ( Exception ex2 ) {
                                    Logger.logTextLnU(DateTime.Now, "saveSequence ex2: " + ex2.Message);
                                }
                            }
                        }
                    } else {
                        // applies to existing, but already saved images - ideally this should not happen 
                        _motionsList[i].imageMotion.Dispose();
                        _motionsList[i].imageMotion = null;
                        // debug motion list
                        if ( Settings.DebugMotions ) {
                            Motion m = _motionsList[i];
                            Logger.logMotionListEntry("!save!", i, m.imageMotion != null, m.motionConsecutive, m.motionDateTime, m.motionSaved);
                        }
                        // also delete processed image
                        if ( _motionsList[i].imageProc != null ) {
                            _motionsList[i].imageProc.Dispose();
                            _motionsList[i].imageProc = null;
                        }
                        // detect task overrun
                        if ( _motionsList[i].bitmapLocked ) {
                            Logger.logTextLnU(DateTime.Now, "saveSequence locked bitmap detected " + _motionsList[i].fileNameMotion);
                        }

                    }
                } else {
                    break;
                }
            }
        }

        // send alarm notification image out of a seqeunce of motions
        async void sendAlarmNotification() {
            // loop for consecutives                 
            int consecutiveCount = 0;
            int lastNdx = _motionsList.Count - 1;
            for ( int i = lastNdx; i >= 0; i-- ) {
                if ( _motionsList[i].motionConsecutive ) {
                    consecutiveCount++;
                } else {
                    // don't continue, if sequence count is to small; have at least 10 consecutives in the most recent sequence
                    if ( consecutiveCount < 10 ) {
                        Logger.logTextLn(DateTime.Now, String.Format("alarm sequence photo count to low #1: {0}", consecutiveCount));
                        return;
                    }
                    break;
                }
            }
            // above loop breaks, if total list count is too small
            if ( consecutiveCount < 10 ) {
                Logger.logTextLn(DateTime.Now, String.Format("alarm sequence photo count to low #2: {0}", consecutiveCount));
                return;
            }
            // limit sending a sequence image to one image per 30s aka 60 images @ 2fps
            if ( (DateTime.Now - _lastSequenceSendTime).TotalSeconds < 30 ) {
                Logger.logTextLn(DateTime.Now, String.Format("alarm sequence photo time to short"));
                return;
            }
            _lastSequenceSendTime = DateTime.Now;
            int execStep = 0;
            try {
                // get last image from the sequence
                Bitmap image = null;
                if ( File.Exists(_motionsList[lastNdx].fileNameMotion) ) {
                    execStep = 1;
                    try {
                        image = new Bitmap(_motionsList[lastNdx].fileNameMotion);
                    } catch ( Exception exc ) {
                        // file might be locked due to save operation, so try again one time
                        execStep = 2;
                        await Task.Delay(200);
                        image = new Bitmap(_motionsList[lastNdx].fileNameMotion);
                    }
                } else {
                    execStep = 3;
                    image = (Bitmap)_motionsList[lastNdx].imageMotion.Clone();
                }
                if ( image == null ) {
                    Logger.logTextLnU(DateTime.Now, String.Format("alarm sequence photo: image == null"));
                }
                // send image via Telegram
                execStep = 4;
                IRestResponse response = _Bot.SetCurrentAction(_notifyReceiver, ChatAction.UploadPhoto);
                if ( response != null && response.StatusCode == System.Net.HttpStatusCode.BadRequest ) {
                    execStep = 5;
                    Logger.logTextLnU(DateTime.Now, String.Format("alarm sequence photo: bad request {0} #1", Settings.TelegramNotifyReceiver));
                    _notifyReceiver.Id = -1;
                    Settings.TelegramNotifyReceiver = -1;
                    Settings.KeepTelegramNotifyAction = false;
                    _notifyText = "";
                }
                execStep = 6;
                byte[] buffer = bitmapToByteArray(image);
                execStep = 7;
                TeleSharp.Entities.Message msgResponse = _Bot.SendPhoto(_notifyReceiver, buffer, "alarm", "alarm sequence photo");
                if ( msgResponse != null && msgResponse.MessageId == 0 ) {
                    execStep = 8;
                    Logger.logTextLnU(DateTime.Now, String.Format("alarm sequence photo: invalid Id {0} #2", Settings.TelegramNotifyReceiver));
                    _notifyReceiver.Id = -1;
                    Settings.TelegramNotifyReceiver = -1;
                    Settings.KeepTelegramNotifyAction = false;
                    _notifyText = "";
                }
                execStep = 9;
                Logger.logTextLn(DateTime.Now, String.Format("alarm sequence photo {0} sent", lastNdx));
                if ( Settings.DebugMotions ) {
                    Motion m = _motionsList[lastNdx];
                    Logger.logMotionListEntry("alarm", lastNdx, m.imageMotion != null, m.motionConsecutive, m.motionDateTime, m.motionSaved, "alarm");
                }
                execStep = 10;
                image.Dispose();
            } catch ( Exception ex ) {
                Logger.logTextLnU(DateTime.Now, String.Format("alarm sequence photo ex: {0} {1} {2} {3}", lastNdx, execStep, _motionsList[lastNdx].motionDateTime , ex.Message));
            }
        }

        // supposed to reset an exception state, sometimes needed for pictureBox and 'red cross exception'
        // perhaps not needed when PictureBox is subclassed 
        void ResetExceptionState(Control control) {
            typeof(Control).InvokeMember("SetState", System.Reflection.BindingFlags.NonPublic |
                                                     System.Reflection.BindingFlags.InvokeMethod |
                                                     System.Reflection.BindingFlags.Instance,
                                                     null,
                                                     control,
                                                     new object[] { 0x400000, false });
        }

        // TBD something useful
        const int WM_KEYDOWN = 0x100;
        const int WM_SYSKEYDOWN = 0x105;
        public bool PreFilterMessage(ref System.Windows.Forms.Message m)     // IMessageFilter: intercept messages
        {
            // 'Alt-P' is the magic key combination
            if ( (ModifierKeys == Keys.Alt) && ((Keys)m.WParam == Keys.P) && (m.Msg == WM_SYSKEYDOWN) ) {
                return false;
            }
            // in case to add something useful
            if ( m.Msg == WM_KEYDOWN ) {
                // <ESC> 
                if ( (Keys)m.WParam == Keys.Escape ) {
                }
            }
            return false;
        }

        // return a Bitmap as byte array
        byte[] bitmapToByteArray(Bitmap bmp) {
            using ( MemoryStream memoryStream = new MemoryStream() ) {
                bmp.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                return memoryStream.ToArray();
            }
        }

        // MainForm resize: match MainForm size to aspect ratio of pictureBox
        void adjustMainFormSize(double aspectRatioBmp) {
            // PFM offset
            int toolbarOfs = this.Height - this.PictureBox.Height - 12;
            // what new dimension is the driver for the size change?
            int deltaWidth = _sizeBeforeResize.Width - this.Size.Width;
            int deltaHeight = _sizeBeforeResize.Height - this.Size.Height;
            if ( Math.Abs(deltaWidth) > Math.Abs(deltaHeight) ) {
                // keep width
                this.Size = new Size(this.Width, (int)((double)this.Width / aspectRatioBmp) + toolbarOfs);
            } else {
                // keep height
                this.Size = new Size((int)((double)(this.Height - toolbarOfs) * aspectRatioBmp), this.Height);
            }
            this.Invalidate();
            this.Update();
        }
        private void MainForm_ResizeBegin(object sender, EventArgs e) {
            _sizeBeforeResize = this.Size;
        }
        private void MainForm_Resize(object sender, EventArgs e) {
            try {
                if ( this.WindowState != FormWindowState.Minimized ) {
                    adjustMainFormSize(_frameAspectRatio);
                    headLine();
                }
            } catch ( System.InvalidOperationException ioe ) {
                ;
            } finally {
            }
        }
        private void MainForm_ResizeEnd(object sender, EventArgs e) {
            _sizeBeforeResize = this.Size;
        }

        // update title bar info
        void headLine() {
            try {
                Text = String.Format("GrzMotion - {0}ms @{1:0.00}fps{2}", _procMs, _fps, _notifyText);
            } catch ( Exception ex ) {
                Text = "headLine() Exception " + ex.Message;
            }
        }

        // show "about" in system menu
        const int WM_DEVICECHANGE = 0x0219;
        const int WM_SYSCOMMAND = 0x112;
        [DllImport("user32.dll")]
        private static extern int GetSystemMenu(int hwnd, int bRevert);
        [DllImport("user32.dll")]
        private static extern int AppendMenu(int hMenu, int Flagsw, int IDNewItem, string lpNewItem);
        private void SetupSystemMenu() {
            // get handle to app system menu
            int menu = GetSystemMenu(this.Handle.ToInt32(), 0);
            // add a separator
            AppendMenu(menu, 0xA00, 0, null);
            // add items with unique message ID
            AppendMenu(menu, 0, 1238, "UVC camera snapshot");
            AppendMenu(menu, 0, 1237, "Telegram send 'test' to 1st whitelist entry");
            AppendMenu(menu, 0, 1236, "Loupe");
            AppendMenu(menu, 0, 1235, "Still Image");
            AppendMenu(menu, 0, 1234, "About GrzMotion");
        }
        protected override void WndProc(ref System.Windows.Forms.Message m) {
            // something happened to USB, not clear whether camera or something else
            if ( m.Msg == WM_DEVICECHANGE ) {
                getCameraBasics();
            }

            // WM_SYSCOMMAND is 0x112
            if ( m.Msg == WM_SYSCOMMAND ) {
                // UVC snapshot
                if ( m.WParam.ToInt32() == 1238 ) {
                    if ( _uvcDevice != null && _uvcDevice.IsRunning ) {
                        MessageBox.Show("Only available, if RTSP device is active.", "Note", MessageBoxButtons.OK);
                    } else {
                        Task.Run(() => {
                            MakeSnapshotWithUVC();
                        });
                    }
                }
                // Telegram test message
                if ( m.WParam.ToInt32() == 1237 ) {
                    // send a test message to master
                    TelegramSendMasterMessage("test");
                }
                // loupe
                if ( m.WParam.ToInt32() == 1236 ) {
                    Loupe.Loupe lp = new Loupe.Loupe();
                    lp.StartPosition = FormStartPosition.Manual;
                    lp.Location = new Point(this.Location.X - lp.Width - 5, this.Location.Y + 5);
                    lp.Show(this);
                }
                // open a still image
                if ( m.WParam.ToInt32() == 1235 ) {
                    if ( _uvcDevice != null && _uvcDevice.IsRunning || _rtspStream.GetStatus == RtspControl.Status.RUNNING ) {
                        MessageBox.Show("Stop the running camera prior to open a still image.", "Note");
                        return;
                    }
                    OpenFileDialog of = new OpenFileDialog();
                    of.InitialDirectory = Application.StartupPath;
                    of.Filter = "All Files|*.*|JPeg Image|*.jpg";
                    DialogResult result = of.ShowDialog();
                    if ( result != DialogResult.OK ) {
                        EnableConnectionControls(true);
                        return;
                    }
                    try {
                        _origFrame = new Bitmap(of.FileName);
                        double ar = (double)_origFrame.Width / (double)_origFrame.Height;
                        int height = (int)(800.0f / ar);
                        _currFrame = resizeBitmap(_origFrame, new Size(800, height));
                        _procFrame = (Bitmap)_currFrame.Clone();
                        _prevFrame = (Bitmap)_currFrame.Clone();
                        // show image in picturebox
                        this.PictureBox.Image = _origFrame;
                    } catch (Exception e) {
                        MessageBox.Show(e.Message, "Error"); 
                    }
                }
                // show About box: check for added menu item's message ID
                if ( m.WParam.ToInt32() == 1234 ) {
                    // show About box here...
                    AboutBox dlg = new AboutBox();
                    dlg.ShowDialog();
                    dlg.Dispose();
                }
            }

            // it is essential to call the base behavior
            base.WndProc(ref m);
        }

        // make a single snapshot with the UVC camera
        void MakeSnapshotWithUVC() {
            // return if already busy   
            if ( _uvcDeviceSnapIsBusy ) {
                return;
            }
            // busy flag
            _uvcDeviceSnapIsBusy = true;
            // locals
            int uvcResolutionIndex = -1;
            try {
                // create exclusive snapshot device
                _uvcDeviceSnap = new VideoCaptureDevice(Settings.CameraMoniker);
                if ( _uvcDeviceSnap == null ) {
                    _uvcDeviceSnapIsBusy = false;
                    return;
                }
                // get best UVC snapshot resolution
                int maxW = 0;
                int maxH = 0;
                for ( int i = 0; i < _uvcDeviceSnap.VideoCapabilities.Length; i++ ) {
                    if ( _uvcDeviceSnap.VideoCapabilities[i].FrameSize.Width >= maxW && _uvcDeviceSnap.VideoCapabilities[i].FrameSize.Height >= maxH ) {
                        maxW = _uvcDeviceSnap.VideoCapabilities[i].FrameSize.Width;
                        maxH = _uvcDeviceSnap.VideoCapabilities[i].FrameSize.Height;
                        uvcResolutionIndex = i;
                    }
                }
            } catch ( Exception e ) {
                Logger.logTextLn(DateTime.Now, String.Format("MakeSnapshotWithUVC init exception: {0}", e.Message));
                _uvcDeviceSnapIsBusy = false;
                return;
            }
            if ( uvcResolutionIndex != -1 ) {
                int step = 0;
                try {
                    // init and start UVC snapshot device
                    _uvcDeviceSnap.VideoResolution = _uvcDeviceSnap.VideoCapabilities[uvcResolutionIndex];
                    step = 1;
                    _uvcDeviceSnap.Start();
                    step = 2;
                    _uvcDeviceSnap.NewFrame += uvcDeviceSnapHandler;
                } catch ( Exception e ) {
                    Logger.logTextLn(DateTime.Now, String.Format("MakeSnapshotWithUVC start exception {0}: {1}", step, e.Message));
                    _uvcDeviceSnapIsBusy = false;
                    _uvcDeviceSnap = null;
                }
            } else {
                _uvcDeviceSnapIsBusy = false;
                _uvcDeviceSnap = null;
            }
        }
        void uvcDeviceSnapHandler(object sender, AForge.Video.NewFrameEventArgs eventArgs) {
            try {
                // save UVC snapshot bmp
                try {
                    Bitmap bmp = (Bitmap)eventArgs.Frame.Clone();
                    using ( var graphics = Graphics.FromImage(bmp) ) {
                        string text = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss_fff", System.Globalization.CultureInfo.InvariantCulture);
                        graphics.DrawString(text, _timestampFont, Brushes.Black, 5, 5);
                    }
                    string path = System.IO.Path.Combine(Settings.StoragePath, _nowStringPath + "_snap");
                    System.IO.Directory.CreateDirectory(path);
                    string fileName = System.IO.Path.Combine(path, _nowStringFile + ".jpg");
                    bmp.Save(fileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                    bmp.Dispose();
                } catch ( Exception e ) {
                    Logger.logTextLn(DateTime.Now, "uvcDeviceSnapHandler bmp exception: " + e.Message);
                    _uvcDeviceSnapIsBusy = false;
                    _uvcDeviceSnap = null;
                    return;
                }
                // unregister event handler
                _uvcDeviceSnap.NewFrame -= uvcDeviceSnapHandler;
                // stop UVC snapshot device
                _uvcDeviceSnap.SignalToStop();
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                do {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                } while ( sw.ElapsedMilliseconds < 500 );
            } catch ( Exception e ) {
                Logger.logTextLn(DateTime.Now, "uvcDeviceSnapHandler exception: " + e.Message);
            } finally {
                _uvcDeviceSnapIsBusy = false;
                _uvcDeviceSnap = null;
            }
        }

        // delete oldest image folder
        void deleteOldestImageFolder(string homeFolder) {
            FileSystemInfo dirInfo = new DirectoryInfo(homeFolder).GetDirectories().OrderBy(fi => fi.CreationTime).First();
            Directory.Delete(dirInfo.FullName, true);
        }

        // PInvoke for Windows API function GetDiskFreeSpaceEx
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);
        public static long driveFreeBytes(string folderName) {
            long freespace = -1;
            if ( string.IsNullOrEmpty(folderName) ) {
                return freespace;
            }
            if ( !folderName.EndsWith("\\") ) {
                folderName += '\\';
            }
            ulong free = 0, dummy1 = 0, dummy2 = 0;
            if ( GetDiskFreeSpaceEx(folderName, out free, out dummy1, out dummy2) ) {
                freespace = (long)free;
                return freespace;
            } else {
                return freespace;
            }
        }

        // manually call to app settings: PropertyGrid dialog
        private void buttonSettings_Click(object sender, EventArgs e) {
            // transfer current app settings to Settings class
            updateSettingsFromAppProperties();
            // start settings dialog
            Settings dlg = new Settings(Settings);
            // memorize settings
            AppSettings oldSettings;
            Settings.CopyAllTo(Settings, out oldSettings);
            if ( dlg.ShowDialog() == DialogResult.OK ) {
                // get changed values back from PropertyGrid settings dlg
                Settings = dlg.Setting;
                // update app settings
                updateAppPropertiesFromSettings();
                // backup ini
                string src = System.Windows.Forms.Application.ExecutablePath + ".ini";
                string dst = System.Windows.Forms.Application.ExecutablePath + ".ini_bak";
                // make a backup history
                if ( System.IO.File.Exists(dst) ) {
                    int counter = 0;
                    do {
                        dst = System.Windows.Forms.Application.ExecutablePath + ".ini_bak" + counter.ToString();
                        counter++;
                    } while ( System.IO.File.Exists(dst) );
                    // option to delete oldest backups
                    if ( counter > 10 ) {
                        var retDelete = MessageBox.Show("The most recent two Settings backups will be kept.\n\nContinue?", "Delete oldest Settings backups?", MessageBoxButtons.YesNo);
                        if ( retDelete == DialogResult.Yes ) {
                            // save most recent .ini_bakX to .ini_bak
                            int mostRecentIndex = counter - 2;
                            string srcMostRecent = System.Windows.Forms.Application.ExecutablePath + ".ini_bak" + mostRecentIndex.ToString();
                            string dstOldest = System.Windows.Forms.Application.ExecutablePath + ".ini_bak";
                            System.IO.File.Copy(srcMostRecent, dstOldest, true);
                            // now delete all .ini_bakX
                            for ( int i = mostRecentIndex; i >= 0; i-- ) {
                                string dstDelete = System.Windows.Forms.Application.ExecutablePath + ".ini_bak" + i.ToString();
                                System.IO.File.Delete(dstDelete);
                            }
                            // beside of .ini_bak, ".ini_bak0" will become the most recent bak
                            dst = System.Windows.Forms.Application.ExecutablePath + ".ini_bak0";
                        }
                    }
                }
                try {
                    System.IO.File.Copy(src, dst, false);
                } catch ( Exception ex ) {
                    var ret = MessageBox.Show(ex.Message + "\n\nContinue without Settings backup?.\n\nChanges are directly written to .ini.", "Settings backup failed", MessageBoxButtons.YesNo);
                    if ( ret != DialogResult.Yes ) {
                        // changes to Settings are not saved to ini
                        return;
                    }
                }
                // INI: write settings to ini
                Settings.writePropertyGridToIni();
                // since app continues to run, update app's health flag
                AppSettings.IniFile ini = new AppSettings.IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
                ini.IniWriteValue("GrzMotion", "AppCrash", "True");
            } else {
                Settings.CopyAllTo(oldSettings, out Settings);
                // restore ROIs dummy list no matter what
                Settings.dummyListROIs = Settings.getROIsStringListFromPropertyGrid().ToArray();
            }
        }

        // allow panel with disabled controls to show tooltips
        private void tableLayoutPanel_MouseHover(object sender, EventArgs e) {
            Point pt = ((TableLayoutPanel)sender).PointToClient(Control.MousePosition);
            try {
                TableLayoutPanelCellPosition pos = GetCellPosition((TableLayoutPanel)sender, pt);
                Control c = ((TableLayoutPanel)sender).GetControlFromPosition(pos.Column, pos.Row);
                if ( c != null ) {
                    string tt = this.toolTip.GetToolTip(c);
                    toolTip.Show(tt, (TableLayoutPanel)sender, pt, 5000);
                }
            } catch {;} 
        }
        // TableLayoutPanel cell position under the mouse: https://stackoverflow.com/questions/39040847/show-text-when-hovering-over-cell-in-tablelayoutpanel-c-sharp 
        private TableLayoutPanelCellPosition GetCellPosition(TableLayoutPanel panel, Point p) {
            // cell position
            TableLayoutPanelCellPosition pos = new TableLayoutPanelCellPosition(0, 0);
            // panel size
            Size size = panel.Size;
            // get the cell row y coordinate
            float y = 0;
            for ( int i = 0; i < panel.RowCount; i++ ) {
                // calculate the sum of the row heights.
                SizeType type = panel.RowStyles[i].SizeType;
                float height = panel.RowStyles[i].Height;
                switch ( type ) {
                    case SizeType.Absolute:
                        y += height;
                        break;
                    case SizeType.Percent:
                        y += height / 100 * size.Height;
                        break;
                    case SizeType.AutoSize:
                        SizeF cellAutoSize = new SizeF(size.Width / panel.ColumnCount, size.Height / panel.RowCount);
                        y += cellAutoSize.Height;
                        break;
                }
                // check the mouse position to decide if the cell is in current row.
                if ( (int)y > p.Y ) {
                    pos.Row = i;
                    break;
                }
            }
            // get the cell column x coordinate
            float x = 0;
            for ( int i = 0; i < panel.ColumnCount; i++ ) {
                // calculate the sum of the row widths
                SizeType type = panel.ColumnStyles[i].SizeType;
                float width = panel.ColumnStyles[i].Width;
                switch ( type ) {
                    case SizeType.Absolute:
                        x += width;
                        break;
                    case SizeType.Percent:
                        x += width / 100 * size.Width;
                        break;
                    case SizeType.AutoSize:
                        SizeF cellAutoSize = new SizeF(size.Width / panel.ColumnCount, size.Height / panel.RowCount);
                        x += cellAutoSize.Width;
                        break;
                }
                // check the mouse position to decide if the cell is in current column
                if ( (int)x > p.X ) {
                    pos.Column = i;
                    break;
                }
            }
            // return the mouse position
            return pos;
        }
    }

    // app settings
    public class AppSettings {
        // the literal name of the ini section
        private string iniSection = "GrzMotion";

        // show ROIs edit dialog from a property grid
        [Editor(typeof(RoiEditor), typeof(System.Drawing.Design.UITypeEditor))]
        class RoiEditor : System.Drawing.Design.UITypeEditor {
            public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return System.Drawing.Design.UITypeEditorEditStyle.Modal;
            }
            public override object EditValue(ITypeDescriptorContext context, System.IServiceProvider provider, object value) {
                System.Windows.Forms.Design.IWindowsFormsEditorService svc = provider.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)) as System.Windows.Forms.Design.IWindowsFormsEditorService;
                // no current image --> no ROIs edit dialog
                if ( MainForm._currFrame != null ) {
                    // ROIs edit dialog
                    using ( DefineROI form = new DefineROI() ) {
                        // set image
                        form.SetImage(MainForm._currFrame);
                        // get ROIs data from PropertyGrid 
                        form.ROIsList = MainForm.Settings.getROIsListFromPropertyGrid();
                        // exec dialog
                        if ( svc.ShowDialog(form) == DialogResult.OK ) {
                            // save dialog ROIs to settings PropertyGrid
                            MainForm.Settings.setPropertyGridToROIsList(form.ROIsList);
                        }
                    }
                } else {
                    MessageBox.Show("First, start camera in main window.");
                }
                return value;
            }
        }

        // custom form to show text inside a property grid
        [Editor(typeof(FooEditor), typeof(System.Drawing.Design.UITypeEditor))]
        class FooEditor : System.Drawing.Design.UITypeEditor {
            public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return System.Drawing.Design.UITypeEditorEditStyle.Modal;
            }
            public override object EditValue(ITypeDescriptorContext context, System.IServiceProvider provider, object value) {
                System.Windows.Forms.Design.IWindowsFormsEditorService svc = provider.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)) as System.Windows.Forms.Design.IWindowsFormsEditorService;
                String foo = value as String;
                if ( svc != null && foo != null ) {
                    using ( FooForm form = new FooForm() ) {
                        form.Value = foo;
                        svc.ShowDialog(form);
                    }
                }
                return value;
            }
        }
        class FooForm : Form {
            private TextBox textbox;
            private Button okButton;
            public FooForm() {
                textbox = new TextBox();
                textbox.Multiline = true;
                textbox.Dock = DockStyle.Fill;
                textbox.WordWrap = false;
                textbox.Font = new Font(FontFamily.GenericMonospace, textbox.Font.Size);
                textbox.ScrollBars = ScrollBars.Both;
                Controls.Add(textbox);
                okButton = new Button();
                okButton.Text = "OK";
                okButton.Dock = DockStyle.Bottom;
                okButton.DialogResult = DialogResult.OK;
                Controls.Add(okButton);
            }
            public string Value {
                get { return textbox.Text; }
                set { textbox.Text = value; }
            }
        }

        // form to start 'make video now'
        [Editor(typeof(ActionButtonVideoEditor), typeof(System.Drawing.Design.UITypeEditor))]
        class ActionButtonVideoEditor : System.Drawing.Design.UITypeEditor {
            public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return System.Drawing.Design.UITypeEditorEditStyle.Modal;
            }
            public override object EditValue(ITypeDescriptorContext context, System.IServiceProvider provider, object value) {
                System.Windows.Forms.Design.IWindowsFormsEditorService svc = provider.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)) as System.Windows.Forms.Design.IWindowsFormsEditorService;
                if ( svc != null ) {
                    MainForm.Settings.MakeVideoNow = false;
                    using ( ActionButton form = new ActionButton("Make motion video now") ) {
                        MainForm.Settings.MakeVideoNow = svc.ShowDialog(form) == DialogResult.OK;
                    }
                }
                return value;
            }
        }
        // form to start 'reboot windows now'
        [Editor(typeof(ActionButtonVideoEditor), typeof(System.Drawing.Design.UITypeEditor))]
        class ActionButtonRebootEditor : System.Drawing.Design.UITypeEditor {
            public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return System.Drawing.Design.UITypeEditorEditStyle.Modal;
            }
            public override object EditValue(ITypeDescriptorContext context, System.IServiceProvider provider, object value) {
                System.Windows.Forms.Design.IWindowsFormsEditorService svc = provider.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)) as System.Windows.Forms.Design.IWindowsFormsEditorService;
                if ( svc != null ) {
                    MainForm.Settings.MakeVideoNow = false;
                    using ( ActionButton form = new ActionButton("Reboot Windows now - are you sure?") ) {
                        if ( svc.ShowDialog(form) == DialogResult.OK ) {
                            // pretend to workflow time tick, that boot time is now
                            DateTime now = DateTime.Now;
                            MainForm.BootTimeBeg = new System.TimeSpan(now.Hour, now.Minute, now.Second);
                            MainForm.BootTimeEnd = new System.TimeSpan(now.Hour, now.Minute + 1, now.Second);
                        }
                    }
                }
                return value;
            }
        }
        // a general action form
        class ActionButton : Form {
            private Label textbox;
            private Button okButton;
            private Button cancelButton;
            public ActionButton(String title) {
                this.FormBorderStyle = FormBorderStyle.FixedSingle;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                textbox = new Label();
                textbox.Location = new System.Drawing.Point(0, 50);
                textbox.Font = new Font("Microsoft Sans Serif", 18, FontStyle.Regular, GraphicsUnit.Point);
                textbox.TextAlign = ContentAlignment.MiddleCenter;
                textbox.Size = new Size(this.Width, 100);
                textbox.Text = title;
                Controls.Add(textbox);
                okButton = new Button();
                okButton.Text = "OK";
                okButton.Location = new System.Drawing.Point(this.ClientSize.Width - 80, this.ClientSize.Height - 60);
                okButton.Size = new Size(60, 25);
                okButton.DialogResult = DialogResult.OK;
                Controls.Add(okButton);
                cancelButton = new Button();
                cancelButton.Location = new System.Drawing.Point(20, this.ClientSize.Height - 60);
                cancelButton.Text = "Cancel";
                cancelButton.Size = new Size(60, 25);
                cancelButton.DialogResult = DialogResult.OK;
                Controls.Add(cancelButton);
            }
        }

        public class FolderNameEditorWithRootFolder : UITypeEditor {
            public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return UITypeEditorEditStyle.Modal;
            }

            public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value) {
                using ( FolderBrowserDialog dlg = new FolderBrowserDialog() ) {
                    dlg.SelectedPath = (string)value;
                    if ( dlg.ShowDialog() == DialogResult.OK )
                        return dlg.SelectedPath;
                }
                return base.EditValue(context, provider, value);
            }
        }

        // make a copy of all class properties
        public void CopyAllTo(AppSettings source, out AppSettings target) {
            target = new AppSettings();
            var type = typeof(AppSettings);
            foreach ( var sourceProperty in type.GetProperties() ) {
                var targetProperty = type.GetProperty(sourceProperty.Name);
                targetProperty.SetValue(target, sourceProperty.GetValue(source, null), null);
            }
            foreach ( var sourceField in type.GetFields() ) {
                var targetField = type.GetField(sourceField.Name);
                targetField.SetValue(target, sourceField.GetValue(source));
            }
        }

        // webserver image type
        public enum WebserverImageType {
            LORES = 0,
            PROCESS = 1,
            HIRES = 2,
        }

        // image source type
        public enum ImageSourceType {
            UVC = 0,
            RTSP = 1,
            UNDEFINED = 2,
        }

        // define app properties
        [CategoryAttribute("Camera")]
        [Description("Source is either RTSP-Stream or UVC-Camera")]
        [ReadOnly(true)]
        public ImageSourceType ImageSource { get; set; }
        [CategoryAttribute("Camera")]
        [Description("RTSP connection string in format: rtsp://<user>:<pwd>@<ip-address>:<port>/<stream name>")]
        [ReadOnly(false)]
        public string RtspConnectUrl { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(false)]
        public Size RtspResolution { get; set; }
        [CategoryAttribute("Camera")]
        [Description("Allow UVC snapshot (if available), whever RTSP detects a motion")]
        [ReadOnly(false)]
        public bool RtspSnapshot { get; set; }
        [CategoryAttribute("Camera")]
        [Description("Number of app restarts due to RTSP exceptions")]
        [ReadOnly(true)]
        public int RtspRestartAppCount { get; set; }
        [Browsable(false)]
        public bool RtspRetry { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public string CameraMoniker { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public Size CameraResolution { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public Size ScaledImageSize { get; set; }
        [CategoryAttribute("Camera")]
        [Description("experimental - let GrzMotion adjust camera exposure time")]
        [ReadOnly(false)]
        public bool ExposureByApp { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public bool ExposureAuto { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int ExposureVal { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int ExposureMin { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int ExposureMax { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int Brightness { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int BrightnessMin { get; set; }
        [CategoryAttribute("Camera")]
        [ReadOnly(true)]
        public int BrightnessMax { get; set; }
        [CategoryAttribute("User Interface")]
        [ReadOnly(true)]
        public Size FormSize { get; set; }
        [ReadOnly(true)]

        [CategoryAttribute("User Interface")]
        public Point FormLocation { get; set; }
        [Description("Minimize app if motion detection is active")]
        [CategoryAttribute("User Interface")]
        [ReadOnly(false)]
        public Boolean MinimizeApp { get; set; }

        [CategoryAttribute("Network")]
        [ReadOnly(true)]
        [Description("Current network status via ping")]
        public Boolean PingOk { get; set; }
        [CategoryAttribute("Network")]
        [ReadOnly(false)]
        [Description("Network test IP address for ping")]
        public string PingTestAddress { get; set; }
        public string PingTestAddressRef;

        [CategoryAttribute("Data Storage")]
        private string storagePath;
        [Description("App storage path: images, ini, logfiles")]
        [CategoryAttribute("Data Storage")]
        [ReadOnly(false)]
        [EditorAttribute(typeof(FolderNameEditorWithRootFolder), typeof(UITypeEditor))]
        public string StoragePath { 
            get {
                return this.storagePath;
            } 
            set {
                this.storagePath = value;
                if ( this.storagePath.Length == 0 ) {
                    return;
                }
                if ( this.storagePath == "\\" ) {
                    AutoMessageBox.Show(String.Format("StoragePath = '{0}' is not regular.", this.storagePath), "Error", 5000);
                    this.storagePath = "";
                    return;
                }
                if ( this.storagePath.IndexOf(":") != 1 ) {
                    AutoMessageBox.Show(String.Format("StoragePath = '{0}' is not valid.", this.storagePath), "Error", 5000);
                    return;
                }
                if ( this.storagePath.IndexOf("\\") != 2 ) {
                    AutoMessageBox.Show(String.Format("StoragePath = '{0}' is not acceptable.", this.storagePath), "Error", 5000);
                    return;
                }
                try {
                    Directory.CreateDirectory(this.storagePath);
                    if ( !this.storagePath.EndsWith("\\") ) {
                        this.storagePath += "\\";
                    }
                } catch ( Exception ) {
                    AutoMessageBox.Show(String.Format("StoragePath = '{0}' is not accessible.", this.storagePath), "Error", 5000);
                }
            } 
        }
        [CategoryAttribute("Data Storage")]
        private string storagePathAlt;
        [CategoryAttribute("Data Storage")]
        [Description("Alternative app storage path, if regular storage path (see above) is full")]
        [ReadOnly(false)]
        [EditorAttribute(typeof(FolderNameEditorWithRootFolder), typeof(UITypeEditor))]
        public string StoragePathAlt {
            get {
                return this.storagePathAlt;
            }
            set {
                this.storagePathAlt = value;
                if ( this.storagePathAlt.Length == 0 ) {
                    return;
                }
                if ( this.storagePathAlt == "\\" ) {
                    AutoMessageBox.Show(String.Format("StoragePathAlt = '{0}' is not regular.", this.storagePathAlt), "Error", 5000);
                    this.storagePathAlt = "";
                    return;
                }
                if ( this.storagePathAlt.IndexOf(":") != 1 ) {
                    AutoMessageBox.Show(String.Format("StoragePathAlt = '{0}' is not valid.", this.storagePathAlt), "Error", 5000);
                    return;
                }
                if ( this.storagePathAlt.IndexOf("\\") != 2 ) {
                    AutoMessageBox.Show(String.Format("StoragePathAlt = '{0}' is not acceptable.", this.storagePathAlt), "Error", 5000);
                    return;
                }
                try {
                    Directory.CreateDirectory(this.storagePathAlt);
                    if ( !this.storagePathAlt.EndsWith("\\") ) {
                        this.storagePathAlt += "\\";
                    }
                } catch ( Exception ) {
                    AutoMessageBox.Show(String.Format("StoragePathAlt = '{0}' is not accessible.", this.storagePathAlt), "Error", 5000);
                }            
            }
        }
        [Description("Free storage space")]
        [CategoryAttribute("Data Storage")]
        [ReadOnly(true)]
        public string FreeStorageSpace { get; set; }
        [Description("App writes to logfile")]
        [CategoryAttribute("Data Storage")]
        [ReadOnly(false)]
        public Boolean WriteLogfile { get; set; }
        [CategoryAttribute("Data Storage")]
        [Description("Increases logfile size, only useful for debug purposes")]
        [ReadOnly(false)]
        public Boolean LogRamUsage { get; set; }

        [Description("Show pixel change percentage in live view")]
        [CategoryAttribute("Debugging")]
        [ReadOnly(false)]
        public Boolean ShowPixelChangePercent { get; set; }
        [Description("Save processed images, useful for debug purposes")]
        [CategoryAttribute("Debugging")]
        [ReadOnly(false)]
        public Boolean DebugProcessImages { get; set; }
        [Description("Save false positive images, useful for debug purposes")]
        [CategoryAttribute("Debugging")]
        [ReadOnly(false)]
        public Boolean DebugFalsePositiveImages { get; set; }
        [Description("Save non consecutive images, useful for debug purposes")]
        [CategoryAttribute("Debugging")]
        [ReadOnly(false)]
        public Boolean DebugNonConsecutives { get; set; }
        [Description("Save motions list 'GrzMotion.motions', useful for debug purposes")]
        [CategoryAttribute("Debugging")]
        [ReadOnly(false)]
        public Boolean DebugMotions { get; set; }

        [CategoryAttribute("Motion Save Strategy")]
        [Description("Save motion detection sequences")]
        [ReadOnly(false)]
        public Boolean SaveSequences { get; set; }
        [Description("Save hi resolution motion detection images")]
        [CategoryAttribute("Motion Save Strategy")]
        [ReadOnly(false)]
        public Boolean SaveMotion { get; set; }
        [Description("Auto start motion detection at app start")]
        [CategoryAttribute("Motion Save Strategy")]
        [ReadOnly(false)]
        public Boolean DetectMotion { get; set; }
        [Description("Pixel threshold value for darkness (saves non consecutive motions at night time, 0 = OFF)")]
        [CategoryAttribute("Motion Save Strategy")]
        [ReadOnly(false)]
        public int NightThreshold { get; set; }
        [ReadOnly(false)]
        [Description("Enable this, if videos are sent to Android phones.")]
        [CategoryAttribute("Video H264 for Android")]
        public bool VideoH264 { get; set; }
        [CategoryAttribute("Motion Save Strategy")]
        [Description("Start making video, after closing the Settings dialog")]
        [Editor(typeof(ActionButtonVideoEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string MakeMotionVideoNow { get; set; }
        public bool MakeVideoNow;
        [Description("Make daily video of saved motion images at 19:00")]
        [CategoryAttribute("Motion Save Strategy")]
        [ReadOnly(false)]
        public Boolean MakeDailyVideo { get; set; }
        [Description("Status flag, whether the daily motion video is already generated")]
        [CategoryAttribute("Motion Save Strategy")]
        [ReadOnly(false)]
        public Boolean DailyVideoDone { get; set; }
        [ReadOnly(false)]

        [CategoryAttribute("Reboot Behaviour")]
        [Description("Reboot Windows daily at 00:30")]
        public Boolean RebootDaily { get; set; }
        [CategoryAttribute("Reboot Behaviour")]
        [Description("Reboot Windows now - may take up to 30s")]
        [Editor(typeof(ActionButtonRebootEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string RebootWindowsNow { get; set; }
        [CategoryAttribute("Reboot Behaviour")]
        [Description("Reboot Windows allowed after ping fail > 10 minutes")]
        [ReadOnly(false)]
        public Boolean RebootPingAllowed { get; set; }
        [CategoryAttribute("Reboot Behaviour")]
        [Description("Reboot after ping fail counter")]
        [ReadOnly(false)]
        public int RebootPingCounter { get; set; }

        [CategoryAttribute("Telegram")]
        [Description("Use Telegram bot")]
        [ReadOnly(false)]
        public Boolean UseTelegramBot { get; set; }
        [CategoryAttribute("Telegram")]
        [Description("Use Telegram whitelist, needs whitelisted clients")]
        [ReadOnly(false)]
        public Boolean UseTelegramWhitelist { get; set; }
        [CategoryAttribute("Telegram")]
        [DisplayName("Telegram whitelist")]
        [Description("Clients list allowed to communicate with the bot, use format: '<readable name>,notifier'")]
        [ReadOnly(false)]
        public BindingList<string> TelegramWhitelist { get; set; }
        [CategoryAttribute("Telegram")]
        [Description("Whitelist top member receives status messages")]
        [ReadOnly(false)]
        public Boolean TelegramSendMaster { get; set; }
        [CategoryAttribute("Telegram")]
        [Description("Keep Telegram alarm notification permanently")]
        [ReadOnly(false)]
        public Boolean KeepTelegramNotifyAction { get; set; }
        [CategoryAttribute("Telegram")]
        [Description("Telegram alarm notification receiver")]
        [ReadOnly(false)]
        public int TelegramNotifyReceiver { get; set; }
        [CategoryAttribute("Telegram")]
        [Description("Number of app restarts due to Telegram errors")]
        [ReadOnly(true)]
        public int TelegramRestartAppCount { get; set; }
        [CategoryAttribute("Telegram")]
        [Description("Telegram bot authentication token")]
        [ReadOnly(false)]
        public string BotAuthenticationToken { get; set; }
        [CategoryAttribute("Telegram")]
        [Description("Open link in browser to learn, how to use a Telegram Bot")]
        [Editor(typeof(FooEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public String HowToUseTelegram { get; set; }

        [CategoryAttribute("Webserver")]
        [Description("Run app embedded image webserver")]
        [ReadOnly(false)]
        public Boolean RunWebserver { get; set; }
        [CategoryAttribute("Webserver")]
        [Description("Webserver image type: LORES = low resolution image vs. PROCESS = processed image")]
        [ReadOnly(false)]
        public WebserverImageType WebserverImage { get; set; }

        [CategoryAttribute("ROI")]
        [ReadOnly(true)]
        [Description("Edit regions of interest = ROIs")]
        [Editor(typeof(RoiEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public String EditROIs { get; set; }
        [CategoryAttribute("ROI")]
        [DisplayName("ListROIs")]
        [Description("List all regions of interest = ROIs (not editable)")]
        [ReadOnly(true)]
        public string[] dummyListROIs { get; set; }
        [Browsable(false)]
        public string[] ListROIs { get; set; }

        // INI: read PropertyGrid from ini
        public void fillPropertyGridFromIni()
        {
            IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            int tmpInt;
            bool tmpBool;
            string tmpStr;

            
            ini.IniReadValue(iniSection, "ImageSourceType", ImageSource.ToString());

            // image source type
            tmpStr = ini.IniReadValue(iniSection, "ImageSourceType", "empty");
            Array values = Enum.GetValues(typeof(ImageSourceType));
            foreach ( ImageSourceType val in values ) {
                if ( val.ToString() == tmpStr ) {
                    ImageSource = val;
                    break;
                }
                ImageSource = ImageSourceType.UNDEFINED;
            }
            // RTSP stream string
            RtspConnectUrl = ini.IniReadValue(iniSection, "RtspConnectUrl", "");
            // RTSP UVC snapshot
            if ( bool.TryParse(ini.IniReadValue(iniSection, "RtspSnapshot", "False"), out tmpBool) ) {
                RtspSnapshot = tmpBool;
            }
            // RTSP resolution width
            if ( int.TryParse(ini.IniReadValue(iniSection, "RtspResolutionWidth", "2000"), out tmpInt) ) {
                RtspResolution = new Size(tmpInt, 0);
            }
            // RTSP resolution height
            if ( int.TryParse(ini.IniReadValue(iniSection, "RtspResolutionHeight", "1000"), out tmpInt) ) {
                RtspResolution = new Size(RtspResolution.Width, tmpInt);
            }
            // RTSP app restart count
            if ( int.TryParse(ini.IniReadValue(iniSection, "RtspRestartAppCount", "0"), out tmpInt) ) {
                RtspRestartAppCount = tmpInt;
            }
            // RTSP retry flag
            if ( bool.TryParse(ini.IniReadValue(iniSection, "RtspRetry", "False"), out tmpBool) ) {
                RtspRetry = tmpBool;
            }
            // camera moniker string
            CameraMoniker = ini.IniReadValue(iniSection, "CameraMoniker", "empty");
            // camera resolution width
            if ( int.TryParse(ini.IniReadValue(iniSection, "CameraResolutionWidth", "100"), out tmpInt) ) {
                CameraResolution = new Size(tmpInt, 0);
            }
            // camera resolution height
            if ( int.TryParse(ini.IniReadValue(iniSection, "CameraResolutionHeight", "200"), out tmpInt) ) {
                CameraResolution = new Size(CameraResolution.Width, tmpInt);
            }
            // camera exposure 
            if ( bool.TryParse(ini.IniReadValue(iniSection, "ExposureByApp", "False"), out tmpBool) ) {
                ExposureByApp = tmpBool;
            }
            if ( bool.TryParse(ini.IniReadValue(iniSection, "ExposureAuto", "False"), out tmpBool) ) {
                ExposureAuto = tmpBool;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "Exposure", "-5"), out tmpInt) ) {
                ExposureVal = tmpInt;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "ExposureMin", "-200"), out tmpInt) ) {
                ExposureMin = tmpInt;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "ExposureMax", "200"), out tmpInt) ) {
                ExposureMax = tmpInt;
            }
            // camera brightness
            if ( int.TryParse(ini.IniReadValue(iniSection, "Brightness", "-6"), out tmpInt) ) {
                Brightness = tmpInt;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "BrightnessMin", "-200"), out tmpInt) ) {
                BrightnessMin = tmpInt;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "BrightnessMax", "200"), out tmpInt) ) {
                BrightnessMax = tmpInt;
            }
            // form width
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormWidth", "657"), out tmpInt) ) {
                FormSize = new Size(tmpInt, 0);
            }
            // form height
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormHeight", "588"), out tmpInt) ) {
                FormSize = new Size(FormSize.Width, tmpInt);
            }
            // form x
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormX", "10"), out tmpInt) ) {
                FormLocation = new Point(tmpInt, 0);
            }
            // form y
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormY", "10"), out tmpInt) ) {
                FormLocation = new Point(FormLocation.X, tmpInt);
            }
            // show pixel change percentage
            if ( bool.TryParse(ini.IniReadValue(iniSection, "ShowPixelChangePercent", "False"), out tmpBool) ) {
                ShowPixelChangePercent = tmpBool;
            }
            // debug image processing  
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DebugProc", "False"), out tmpBool) ) {
                DebugProcessImages = tmpBool;
            }
            // debug false positive images
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DebugFalsePositives", "False"), out tmpBool) ) {
                DebugFalsePositiveImages = tmpBool;
            }
            // debug non consecutive images
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DebugNonConsecutives", "False"), out tmpBool) ) {
                DebugNonConsecutives = tmpBool;
            }
            // debug motions list
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DebugMotions", "False"), out tmpBool) ) {
                DebugMotions = tmpBool;
            }
            // save motion sequences
            if ( bool.TryParse(ini.IniReadValue(iniSection, "SaveSequences", "False"), out tmpBool) ) {
                SaveSequences = tmpBool;
            }
            // save motion images
            if ( bool.TryParse(ini.IniReadValue(iniSection, "SaveMotion", "False"), out tmpBool) ) {
                SaveMotion = tmpBool;
            }
            // auto detect motion at app start
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DetectMotion", "False"), out tmpBool) ) {
                DetectMotion = tmpBool;
            }
            // video codec H264 is suitable for Android
            if ( bool.TryParse(ini.IniReadValue(iniSection, "VideoH264", "True"), out tmpBool) ) {
                VideoH264 = tmpBool;
            }
            // define darkness
            if ( int.TryParse(ini.IniReadValue(iniSection, "NightThreshold", "30"), out tmpInt) ) {
                NightThreshold = tmpInt;
            }
            // minimize app while motion detection
            if ( bool.TryParse(ini.IniReadValue(iniSection, "MinimizeApp", "True"), out tmpBool) ) {
                MinimizeApp = tmpBool;
            }
            // always false
            MakeVideoNow = false;
            // make daily motion video
            if ( bool.TryParse(ini.IniReadValue(iniSection, "MakeDailyVideo", "False"), out tmpBool) ) {
                MakeDailyVideo = tmpBool;
            }
            // app start after 19:00 should not start making daily video again, if it was already done
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DailyVideoDoneForToday", "False"), out tmpBool) ) {
                DailyVideoDone = tmpBool;
            }
            // reboot windows once a day
            if ( bool.TryParse(ini.IniReadValue(iniSection, "RebootDaily", "False"), out tmpBool) ) {
                RebootDaily = tmpBool;
            }
            // reboot windows allowed after heavy ping fail
            if ( bool.TryParse(ini.IniReadValue(iniSection, "RebootPingAllowed", "False"), out tmpBool) ) {
                RebootPingAllowed = tmpBool;
            }
            // reboot counter after heavy ping fail
            if ( int.TryParse(ini.IniReadValue(iniSection, "RebootPingCounter", "0"), out tmpInt) ) {
                RebootPingCounter = tmpInt;
            }
            // run webserver
            if ( bool.TryParse(ini.IniReadValue(iniSection, "RunWebserver", "False"), out tmpBool) ) {
                RunWebserver = tmpBool;
            }
            // webserver image type
            tmpStr = ini.IniReadValue(iniSection, "WebserverImageType", "empty");
            values = Enum.GetValues(typeof(WebserverImageType));
            foreach ( WebserverImageType val in values ) {
                if ( val.ToString() == tmpStr ) {
                    WebserverImage = val;
                    break;
                }
                WebserverImage = WebserverImageType.PROCESS;
            }
            // ping test address + a ref var with the same purpose (get/set cannot be a ref var)
            PingTestAddress = ini.IniReadValue(iniSection, "PingTestAddress", "8.8.8.8");
            PingTestAddressRef = PingTestAddress;
            // use Telegram bot
            if ( bool.TryParse(ini.IniReadValue(iniSection, "UseTelegramBot", "False"), out tmpBool) ) {
                UseTelegramBot = tmpBool;
            }
            // get Telegram whitelist from INI
            TelegramWhitelist = new BindingList<string>();
            var ndx = 0;
            while ( true ) {
                string strFull = ini.IniReadValue("TelegramWhitelist", "client" + ndx++.ToString(), ",");
                if ( strFull != "," ) {
                    TelegramWhitelist.Add(strFull);
                } else {
                    break;
                }
            }
            // use Telegram whitelist
            if ( bool.TryParse(ini.IniReadValue(iniSection, "UseTelegramWhitelist", "False"), out tmpBool) ) {
                UseTelegramWhitelist = tmpBool;
                if ( TelegramWhitelist.Count == 0 ) {
                    UseTelegramWhitelist = false;
                }
            }
            // use Telegram master message
            if ( bool.TryParse(ini.IniReadValue(iniSection, "TelegramSendMaster", "False"), out tmpBool) ) {
                TelegramSendMaster = tmpBool;
                if ( TelegramWhitelist.Count == 0 ) {
                    TelegramSendMaster = false;
                }
            }
            // make Telegram alarm notification permanent
            if ( bool.TryParse(ini.IniReadValue(iniSection, "KeepTelegramNotifyAction", "False"), out tmpBool) ) {
                KeepTelegramNotifyAction = tmpBool;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "TelegramNotifyReceiver", "-1"), out tmpInt) ) {
                TelegramNotifyReceiver = tmpInt;
            }
            // app restart count due to a Telegram malfunction
            if ( int.TryParse(ini.IniReadValue(iniSection, "TelegramRestartAppCount", "0"), out tmpInt) ) {
                TelegramRestartAppCount = tmpInt;
            }
            // Telegram bot authentication token
            BotAuthenticationToken = ini.IniReadValue(iniSection, "BotAuthenticationToken", "");
            // app common storage path
            StoragePath = ini.IniReadValue(iniSection, "StoragePath", Application.StartupPath + "\\");
            // alternative app storage path, if above path is full
            StoragePathAlt = ini.IniReadValue(iniSection, "StoragePathAlt", "");
            // free storage space
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = MainForm.driveFreeBytes(StoragePath);
            int order = 0;
            while ( len >= 1024 && order < sizes.Length - 1 ) {
                order++;
                len = len / 1024;
            }
            FreeStorageSpace = String.Format("{0:0.##} {1}", len, sizes[order]);
            // app writes logfile
            if ( bool.TryParse(ini.IniReadValue(iniSection, "WriteLogfile", "False"), out tmpBool) ) {
                WriteLogfile = tmpBool;
            }
            // app writes RAM usage into logfile
            if ( bool.TryParse(ini.IniReadValue(iniSection, "LogRamUsage", "False"), out tmpBool) ) {
                LogRamUsage = tmpBool;
            }
            // hint to edit ROIs
            EditROIs = "Click, then click again the right hand side '...' button to edit ROIs";
            // set all ROIs in PropertyGrid array
            bool asapSaveRoisToIni = false;
            ListROIs = new string[] { "", "", "", "", "", "", "", "", "", ""};
            List<string> strList = new List<string>();
            for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                // normal read from ini
                string strROI = ini.IniReadValue("ROI section", "roi" + i.ToString(), "0,0,0,0,0,0.0,0.0,False,1");
                strList.Add(strROI);
                //
                // app UPDATE <= 1.0.0.2 --> >= 1.0.0.3 "add threshold upper limit" changes ROI structure
                //
                // supposed to happen once, when first time using the 9 elements ROI 
                string[] arr = strROI.Split(',');
                if ( arr.Length == 8 ) {
                    asapSaveRoisToIni = true;
                    strROI = String.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", arr[0], arr[1], arr[2], arr[3], arr[4], arr[5], "1.0", arr[6], arr[7]);
                }
                // build list of ROI
                ListROIs[i] = strROI;
            }
            // for the sake of mind, save ROIs to INI asap
            if ( asapSaveRoisToIni ) {
                // write ROIs from PropertyGrid array to INI
                for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                    ini.IniWriteValue("ROI section", "roi" + i.ToString(), ListROIs[i]);
                }
            }
            dummyListROIs = strList.ToArray();
            // how to use a Telegram bot
            HowToUseTelegram = "https://core.telegram.org/bots#creating-a-new-bot\\";
        }

        // INI: write to ini
        public void writePropertyGridToIni()
        {
            // wipe existing ini
            System.IO.File.Delete(System.Windows.Forms.Application.ExecutablePath + ".ini");
            // ini from scratch
            IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            // image source type
            ini.IniWriteValue(iniSection, "ImageSourceType", ImageSource.ToString());
            // RTSP stream string
            ini.IniWriteValue(iniSection, "RtspConnectUrl", RtspConnectUrl);
            // RTSP UVC snapshot
            ini.IniWriteValue(iniSection, "RtspSnapshot", RtspSnapshot.ToString());
            // RTSP resolution width
            ini.IniWriteValue(iniSection, "RtspResolutionWidth", RtspResolution.Width.ToString());
            // RTSP resolution height
            ini.IniWriteValue(iniSection, "RtspResolutionHeight", RtspResolution.Height.ToString());
            // RTSP app restart count
            ini.IniWriteValue(iniSection, "RtspRestartAppCount", RtspRestartAppCount.ToString());
            // RTSP retry flag
            ini.IniWriteValue(iniSection, "RtspRetry", RtspRetry.ToString());
            // camera moniker string
            ini.IniWriteValue(iniSection, "CameraMoniker", CameraMoniker);
            // camera resolution width
            ini.IniWriteValue(iniSection, "CameraResolutionWidth", CameraResolution.Width.ToString());
            // camera resolution height
            ini.IniWriteValue(iniSection, "CameraResolutionHeight", CameraResolution.Height.ToString());
            // camera exposure
            ini.IniWriteValue(iniSection, "ExposureByApp", ExposureByApp.ToString());
            ini.IniWriteValue(iniSection, "ExposureAuto", ExposureAuto.ToString());
            ini.IniWriteValue(iniSection, "Exposure", ExposureVal.ToString());
            ini.IniWriteValue(iniSection, "ExposureMin", ExposureMin.ToString());
            ini.IniWriteValue(iniSection, "ExposureMax", ExposureMax.ToString());
            // camera brightness
            ini.IniWriteValue(iniSection, "Brightness", Brightness.ToString());
            ini.IniWriteValue(iniSection, "BrightnessMin", BrightnessMin.ToString());
            ini.IniWriteValue(iniSection, "BrightnessMax", BrightnessMax.ToString());
            // form width
            ini.IniWriteValue(iniSection, "FormWidth", FormSize.Width.ToString());
            // form height
            ini.IniWriteValue(iniSection, "FormHeight", FormSize.Height.ToString());
            // form width
            ini.IniWriteValue(iniSection, "FormX", FormLocation.X.ToString());
            // form height
            ini.IniWriteValue(iniSection, "FormY", FormLocation.Y.ToString());
            // show pixel change percentage
            ini.IniWriteValue(iniSection, "ShowPixelChangePercent", ShowPixelChangePercent.ToString());
            // debug image processing
            ini.IniWriteValue(iniSection, "DebugProc", DebugProcessImages.ToString());
            // debug false positive images
            ini.IniWriteValue(iniSection, "DebugFalsePositives", DebugFalsePositiveImages.ToString());
            // debug non consecutive images
            ini.IniWriteValue(iniSection, "DebugNonConsecutives", DebugNonConsecutives.ToString());
            // debug non consecutive images
            ini.IniWriteValue(iniSection, "DebugMotions", DebugMotions.ToString());
            // save motion sequences
            ini.IniWriteValue(iniSection, "SaveSequences", SaveSequences.ToString());
            // save motion images
            ini.IniWriteValue(iniSection, "SaveMotion", SaveMotion.ToString());
            // auto detect motion at app start
            ini.IniWriteValue(iniSection, "DetectMotion", DetectMotion.ToString());
            // H264 video codec is suitable for Android
            ini.IniWriteValue(iniSection, "VideoH264", VideoH264.ToString());
            // define darkness
            ini.IniWriteValue(iniSection, "NightThreshold", NightThreshold.ToString());
            // minimize app while motion detection
            ini.IniWriteValue(iniSection, "MinimizeApp", MinimizeApp.ToString());
            // make daily motion video
            ini.IniWriteValue(iniSection, "MakeDailyVideo", MakeDailyVideo.ToString());
            // flag make daily motion video done
            ini.IniWriteValue(iniSection, "DailyVideoDoneForToday", DailyVideoDone.ToString());
            // reboot counter
            ini.IniWriteValue(iniSection, "RebootPingCounter", RebootPingCounter.ToString());
            // app storage path
            ini.IniWriteValue(iniSection, "StoragePath", StoragePath.ToString());
            // alternative app storage path
            ini.IniWriteValue(iniSection, "StoragePathAlt", StoragePathAlt.ToString());
            // app writes logfile
            ini.IniWriteValue(iniSection, "WriteLogfile", WriteLogfile.ToString());
            // app writes RAM usage into logfile
            ini.IniWriteValue(iniSection, "LogRamUsage", LogRamUsage.ToString());
            // run webserver
            ini.IniWriteValue(iniSection, "RunWebserver", RunWebserver.ToString());
            // webserver image type
            ini.IniWriteValue(iniSection, "WebserverImageType", WebserverImage.ToString());
            // ping test address
            ini.IniWriteValue(iniSection, "PingTestAddress", PingTestAddress);
            // use Telegram bot
            ini.IniWriteValue(iniSection, "UseTelegramBot", UseTelegramBot.ToString());
            // write Telegram whitelist to INI
            for ( int i = 0; i < TelegramWhitelist.Count; i++ ) {
                ini.IniWriteValue("TelegramWhitelist", "client" + i.ToString(), TelegramWhitelist[i]);
            }
            // use Telegram whitelist
            if ( TelegramWhitelist.Count == 0 ) {
                UseTelegramWhitelist = false;
            }
            ini.IniWriteValue(iniSection, "UseTelegramWhitelist", UseTelegramWhitelist.ToString());
            // use Telegram master message
            if ( TelegramWhitelist.Count == 0 ) {
                TelegramSendMaster = false;
            }
            ini.IniWriteValue(iniSection, "TelegramSendMaster", TelegramSendMaster.ToString());
            // make Telegram alarm notification permanent
            ini.IniWriteValue(iniSection, "KeepTelegramNotifyAction", KeepTelegramNotifyAction.ToString());
            if ( KeepTelegramNotifyAction ) {
                ini.IniWriteValue(iniSection, "TelegramNotifyReceiver", TelegramNotifyReceiver.ToString());
            } else {
                ini.IniWriteValue(iniSection, "TelegramNotifyReceiver", "");
            }
            // app restart count due to Telegram malfunction
            ini.IniWriteValue(iniSection, "TelegramRestartAppCount", TelegramRestartAppCount.ToString());
            // Telegram bot authentication token
            ini.IniWriteValue(iniSection, "BotAuthenticationToken", BotAuthenticationToken);
            // reboot windows daily
            ini.IniWriteValue(iniSection, "RebootDaily", RebootDaily.ToString());
            // reboot windows allowed after heavy ping fail
            ini.IniWriteValue(iniSection, "RebootPingAllowed", RebootPingAllowed.ToString());
            // write ROIs from PropertyGrid array to INI
            List<string> strList = new List<string>();
            for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                ini.IniWriteValue("ROI section", "roi" + i.ToString(), ListROIs[i]);
                strList.Add(ListROIs[i]);
            }
            dummyListROIs = strList.ToArray();
        }

        // obtain a string list of ROIs from the settings PropertyGrid 
        public List<string> getROIsStringListFromPropertyGrid() {
            List<string> list = new List<string>();
            for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                list.Add(ListROIs[i]);
            }
            return list;
        }
        // obtain the list of ROIs from the settings PropertyGrid
        public List<MainForm.oneROI> getROIsListFromPropertyGrid() {
            List<MainForm.oneROI> list = new List<MainForm.oneROI>();
            for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                string[] arr = ListROIs[i].Split(',');
                list.Add(new MainForm.oneROI());
                list[i].rect = new Rectangle(int.Parse(arr[0]), int.Parse(arr[1]), int.Parse(arr[2]), int.Parse(arr[3]));
                list[i].thresholdIntensity = int.Parse(arr[4]);
                double outVal;
                double.TryParse(arr[5], NumberStyles.Any, CultureInfo.InvariantCulture, out outVal);
                list[i].thresholdChanges = outVal;
                double.TryParse(arr[6], NumberStyles.Any, CultureInfo.InvariantCulture, out outVal);
                list[i].thresholdUpperLimit = outVal;
                list[i].reference = bool.Parse(arr[7]);
                list[i].boxScaler = int.Parse(arr[8]);
            }
            return list;
        }
        // set the settings PropertyGrid to the list of ROIs provided by the ROIs edit dialog
        public void setPropertyGridToROIsList(List<MainForm.oneROI> list) {
            for ( int i = 0; i < MainForm.ROICOUNT; i++ ) {
                if ( i >= list.Count ) {
                    break;
                }
                ListROIs[i] =
                    list[i].rect.X.ToString() + "," +
                    list[i].rect.Y.ToString() + "," +
                    list[i].rect.Width.ToString() + "," +
                    list[i].rect.Height.ToString() + "," +
                    list[i].thresholdIntensity.ToString() + "," +
                    list[i].thresholdChanges.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    list[i].thresholdUpperLimit.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    list[i].reference.ToString() + "," +
                    list[i].boxScaler.ToString();
            }
        }

        // INI-Files CLass : easiest (though outdated) way to administer app specific setup data
        public class IniFile
        {
            private string path;
            [DllImport("kernel32")]
            private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);
            [DllImport("kernel32")]
            private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);
            public IniFile(string path)
            {
                this.path = path;
            }
            public void IniWriteValue(string Section, string Key, string Value)
            {
                try {
                    WritePrivateProfileString(Section, Key, Value, this.path);
                }
                catch ( Exception ex ) {
                    Logger.logTextLnU(DateTime.Now, "IniWriteValue ex: " + ex.Message);
                    AutoMessageBox.Show("INI-File could not be saved. Please select another 'home folder' in the Main Window.", "Error", 5000);
                }
            }
            public string IniReadValue(string Section, string Key, string DefaultValue)
            {
                StringBuilder retVal = new StringBuilder(255);
                int i = GetPrivateProfileString(Section, Key, DefaultValue, retVal, 255, this.path);
                return retVal.ToString();
            }
        }
    }

}

