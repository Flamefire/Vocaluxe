using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Vocaluxe.Base;
using VocaluxeLib.Draw;

namespace Vocaluxe.Lib.Video.Acinerella
{
    class CDecoder
    {
        private IntPtr _Instance = IntPtr.Zero; // acinerella instance
        private IntPtr _Videodecoder = IntPtr.Zero; // acinerella video decoder instance

        private readonly Stopwatch _LoopTimer = new Stopwatch();
        private string _FileName; // current video file name

        private bool _FileOpened;

        private float _VideoTimeBase; // frame time
        private float _VideoDecoderTime; // time of last decoded frame
        private float _CurrentVideoTime; // current video position
        private bool _BufferFull; // buffer is full, waiting for free frame slot

        private float _Time;
        private float _Gap;
        private float _Start;
        private bool _Loop;
        private float _Duration;
        private float _VideoSkipTime; // = VideoGap
        private float _SkipTime; // = Start + VideoGap

        private bool _Paused;
        private bool _NoMoreFrames;
        private bool _BeforeLoop;

        private int _Width;
        private int _Height;
        private float _SetTime;
        private float _SetGap;
        private float _SetStart;
        private bool _SetSkip;
        private bool _Terminated;

        private readonly Thread _Thread;
        //AutoResetEvent EventDecode = new AutoResetEvent(false);
        private readonly SFrameBuffer[] _FrameBuffer = new SFrameBuffer[5];
        private bool _NewFrame;
        private readonly Object _MutexFramebuffer = new Object();
        private readonly Object _MutexSyncSignals = new Object();

        public CDecoder()
        {
            _Thread = new Thread(_Execute);
        }

        public float Length
        {
            get { return _FileOpened ? _Duration : 0f; }
        }

        public bool Paused
        {
            set
            {
                lock (_MutexSyncSignals)
                {
                    _Paused = value;
                    if (_Paused)
                        _LoopTimer.Stop();
                    else
                        _LoopTimer.Start();
                }
            }
        }

        public bool Loop { private get; set; }

        public bool Finished { get; private set; }

        public bool Open(string fileName)
        {
            if (_FileOpened)
                return false;

            if (!File.Exists(fileName))
                return false;

            //Do this here as one may want to get the length afterwards!
            _FileOpened = _DoOpen();

            _FileName = fileName;
            _Thread.Priority = ThreadPriority.Normal;
            _Thread.Name = Path.GetFileName(fileName);
            _Thread.Start();
            return true;
        }

        public void Close()
        {
            _Terminated = true;
        }

        public bool GetFrame(ref CTexture frame, float time, out float videoTime)
        {
            videoTime = 0;
            if (!_FileOpened)
                return false;

            if (_Paused)
                return false;

            if (Loop)
            {
                lock (_MutexSyncSignals)
                {
                    _SetTime += _LoopTimer.ElapsedMilliseconds / 1000f;
                    videoTime = _SetTime;

                    _LoopTimer.Stop();
                    _LoopTimer.Reset();
                    _LoopTimer.Start();
                }


                _UploadNewFrame(ref frame);
                //EventDecode.Set();
                return frame != null;
            }

            if (Math.Abs(_SetTime - time) > float.Epsilon)
            {
                lock (_MutexSyncSignals)
                {
                    _SetTime = time;
                }
                _UploadNewFrame(ref frame);
                videoTime = _CurrentVideoTime;
                //EventDecode.Set();
                return frame != null;
            }
            return false;
        }

        public bool Skip(float start, float gap)
        {
            lock (_MutexSyncSignals)
            {
                _SetStart = start;
                _SetGap = gap;
                _SetSkip = true;
                _NoMoreFrames = false;
                Finished = false;
            }
            //EventDecode.Set();
            return true;
        }

        #region Threading
        private void _Skip()
        {
            if (!_FileOpened)
                return;

            _VideoSkipTime = _Gap;
            _SkipTime = _Start + _Gap;
            _BeforeLoop = false;

            if (_SkipTime > 0)
            {
                _VideoDecoderTime = _SkipTime;
                try
                {
                    CAcinerella.AcSeek(_Videodecoder, -1, (Int64)(_SkipTime * 1000f));
                }
                catch (Exception e)
                {
                    CLog.LogError("Error seeking video file \"" + _FileName + "\": " + e.Message);
                }
            }
            else
            {
                _VideoDecoderTime = 0f;
                try
                {
                    CAcinerella.AcSeek(_Videodecoder, -1, 0);
                }
                catch (Exception e)
                {
                    CLog.LogError("Error seeking video file \"" + _FileName + "\": " + e.Message);
                }
            }

            lock (_MutexSyncSignals)
            {
                _CurrentVideoTime = _VideoDecoderTime;
            }

            lock (_MutexFramebuffer)
            {
                for (int i = 0; i < _FrameBuffer.Length; i++)
                {
                    _FrameBuffer[i].Displayed = true;
                    _FrameBuffer[i].Time = -1f;
                }
            }

            _BufferFull = false;
            _NewFrame = false;
            //EventDecode.Set();
        }

        private void _Execute()
        {
            //EventDecode.Set();
            while (!_Terminated)
            {
                {
                    //if (EventDecode.WaitOne(10))
                    bool skip = false;
                    lock (_MutexSyncSignals)
                    {
                        _Time = _SetTime;
                        if (_SetSkip)
                            skip = true;

                        _SetSkip = false;
                        _Gap = _SetGap;
                        _Start = _SetStart;
                        _Loop = Loop;
                    }

                    if (skip)
                        _Skip();

                    if (!_NewFrame)
                        _Decode();

                    if (_NewFrame)
                        _Copy();
                    Thread.Sleep(1);
                }
            }

            _Free();
        }

        private bool _DoOpen()
        {
            bool ok;
            try
            {
                _Instance = CAcinerella.AcInit();
                CAcinerella.AcOpen2(_Instance, _FileName);

                SACInstance instance = (SACInstance)Marshal.PtrToStructure(_Instance, typeof(SACInstance));
                _Duration = instance.Info.Duration / 1000f;
                ok = instance.Opened;
            }
            catch (Exception)
            {
                ok = false;
            }

            if (!ok)
            {
                CLog.LogError("Error opening video file: " + _FileName);
                return false;
            }

            int videoStreamIndex;
            SACDecoder videodecoder;
            try
            {
                _Videodecoder = CAcinerella.AcCreateVideoDecoder(_Instance);
                videodecoder = (SACDecoder)Marshal.PtrToStructure(_Videodecoder, typeof(SACDecoder));
                videoStreamIndex = videodecoder.StreamIndex;
            }
            catch (Exception)
            {
                CLog.LogError("Error opening video file (can't find decoder): " + _FileName);
                _Free();
                return false;
            }


            if (videoStreamIndex < 0)
            {
                _Free();
                return false;
            }

            _Width = videodecoder.StreamInfo.VideoInfo.FrameWidth;
            _Height = videodecoder.StreamInfo.VideoInfo.FrameHeight;

            if (videodecoder.StreamInfo.VideoInfo.FramesPerSecond > 0)
                _VideoTimeBase = 1f / (float)videodecoder.StreamInfo.VideoInfo.FramesPerSecond;

            _VideoDecoderTime = 0f;
            _Time = 0f;

            for (int i = 0; i < _FrameBuffer.Length; i++)
            {
                _FrameBuffer[i].Time = -1f;
                _FrameBuffer[i].Displayed = true;
                _FrameBuffer[i].Data = new byte[_Width * _Height * 4];
            }

            return true;
        }

        private void _Decode()
        {
            const int framedropcount = 4;

            if (!_FileOpened || _NewFrame)
                return;

            if (_Paused || _NoMoreFrames || _BufferFull)
                return;

            if ((_SkipTime < 0f) && (_Time + _SkipTime >= 0f))
                _SkipTime = 0f;

            float myTime = _Time + _VideoSkipTime;
            float timeDifference = myTime - _VideoDecoderTime;

            bool dropFrame = timeDifference >= (framedropcount - 1) * _VideoTimeBase;

            if (_Terminated)
                return;

            int frameFinished = 0;
            if (dropFrame && !_BeforeLoop)
            {
                try
                {
                    frameFinished = CAcinerella.AcSkipFrames(_Instance, _Videodecoder, framedropcount - 1);
                }
                catch (Exception)
                {
                    CLog.LogError("Error AcSkipFrame " + _FileName);
                }
            }

            if (!_BeforeLoop && (!dropFrame || frameFinished != 0))
            {
                try
                {
                    frameFinished = CAcinerella.AcGetFrame(_Instance, _Videodecoder);
                }
                catch (Exception)
                {
                    CLog.LogError("Error AcGetFrame " + _FileName);
                }
            }

            if (frameFinished == 0)
            {
                if (_Loop)
                {
                    _BeforeLoop = true;
                    bool doskip = true;
                    float tm = 0f;
                    int num = -1;
                    lock (_MutexFramebuffer)
                    {
                        for (int i = 0; i < _FrameBuffer.Length; i++)
                        {
                            if (_FrameBuffer[i].Time > tm && !_FrameBuffer[i].Displayed)
                            {
                                tm = _FrameBuffer[i].Time;
                                num = i;
                            }
                        }

                        if (num >= 0)
                            doskip = _FrameBuffer[num].Displayed;
                    }

                    if (!doskip)
                        return;

                    lock (_MutexSyncSignals)
                    {
                        _Start = 0f;
                        _Gap = 0f;
                        _SetTime = 0f;
                    }

                    _Skip();
                }
                else
                    _NoMoreFrames = true;
            }
            else
            {
                _NewFrame = true;
                _Copy();
            }
        }

        private void _Copy()
        {
            if (!_NewFrame)
                return;

            int num = -1;

            lock (_MutexFramebuffer)
            {
                for (int i = 0; i < _FrameBuffer.Length; i++)
                {
                    if (_FrameBuffer[i].Displayed)
                    {
                        num = i;
                        break;
                    }
                }

                if (num == -1)
                    _BufferFull = true;
                else
                {
                    SACDecoder videodecoder = (SACDecoder)Marshal.PtrToStructure(_Videodecoder, typeof(SACDecoder));

                    _VideoDecoderTime = (float)videodecoder.Timecode;
                    _FrameBuffer[num].Time = _VideoDecoderTime;

                    if (videodecoder.Buffer != IntPtr.Zero)
                    {
                        Marshal.Copy(videodecoder.Buffer, _FrameBuffer[num].Data, 0, _Width * _Height * 4);

                        _FrameBuffer[num].Displayed = false;
                    }

                    int numfull = 0;
                    for (int i = 0; i < _FrameBuffer.Length; i++)
                    {
                        if (!_FrameBuffer[i].Displayed)
                            numfull++;
                    }

                    if (numfull < _FrameBuffer.Length)
                        _BufferFull = false;

                    _NewFrame = false;
                }
            }
        }

        private void _UploadNewFrame(ref CTexture frame)
        {
            if (!_FileOpened)
                return;

            lock (_MutexFramebuffer)
            {
                int num = _FindFrame();

                if (num >= 0)
                {
                    CDraw.UpdateOrAddTexture(ref frame, _Width, _Height, _FrameBuffer[num].Data);

                    lock (_MutexSyncSignals)
                    {
                        _CurrentVideoTime = _FrameBuffer[num].Time;
                    }
                    Finished = false;
                }
                else
                {
                    if (_NoMoreFrames)
                        Finished = true;
                }
            }
        }

        private int _FindFrame()
        {
            int result = -1;
            float diff = 10000000f;

            for (int i = 0; i < _FrameBuffer.Length; i++)
            {
                float td = _SetTime + _VideoSkipTime - _FrameBuffer[i].Time;

                if (td > _VideoTimeBase * 2f)
                    _FrameBuffer[i].Displayed = true;

                if (!_FrameBuffer[i].Displayed && (td < diff) && (td > _VideoTimeBase))
                {
                    diff = Math.Abs(_FrameBuffer[i].Time - _SetTime - _VideoSkipTime);
                    result = i;
                }
            }

            if (result != -1)
            {
                _FrameBuffer[result].Displayed = true;
                _BufferFull = false;
            }

            return result;
        }

        private void _Free()
        {
            if (_Videodecoder != IntPtr.Zero)
                CAcinerella.AcFreeDecoder(_Videodecoder);

            if (_Instance != IntPtr.Zero)
            {
                CAcinerella.AcClose(_Instance);
                CAcinerella.AcFree(_Instance);
            }
        }
        #endregion Threading
    }
}