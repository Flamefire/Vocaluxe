using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;

using Un4seen.Bass;

using Vocaluxe.Base;
using Vocaluxe.Menu;

namespace Vocaluxe.Lib.Sound
{
    class CBassPlay : IPlayback
    {
        private class CVolumes
        {
            public float Volume = 50f;
            public float VolumeMax = 100f;
        }
        private bool _Initialized = false;
        private List<AudioStreams> _Streams;
        private List<CVolumes> _Volumes;
        private SYNCPROC _SyncSlideAndStop;
        private SYNCPROC _SyncSlideAndPause;
        private Object MutexAudioStreams = new Object();


        public CBassPlay()
        {
            Init();
        }

        public bool Init()
        {
            if (_Initialized)
                return true;

            #region Registration
            string EMail = String.Empty;
            string Code = String.Empty;

            ReadRegistrationFile(Path.Combine(Environment.CurrentDirectory, CSettings.sBassRegistration), ref EMail, ref Code);

            if (EMail == String.Empty || Code == String.Empty)
                WriteRegistrationFile(Path.Combine(Environment.CurrentDirectory, CSettings.sBassRegistration), "your mail adress", "your registration code");
            else
                BassNet.Registration(EMail, Code);
            #endregion Registration

            bool ok = false;
            try
            {
                ok = Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
                _Streams = new List<AudioStreams>();
                _Volumes = new List<CVolumes>();
                _SyncSlideAndStop = new SYNCPROC(EndSync);
                _SyncSlideAndPause = new SYNCPROC(PauseSync);
            }
            catch (Exception e)
            {
                ok = false;
                CLog.LogError("Error initializing Bass: " + e.Message);
            }

            _Initialized = ok;
            return ok;
        }

        public void CloseAll()
        {
            while (_Streams.Count > 0)
            {
                Close(_Streams[_Streams.Count - 1].handle);
            }
            lock (MutexAudioStreams)
            {
                _Streams.Clear();
                _Volumes.Clear();
            }
        }

        public void SetGlobalVolume(float Volume)
        {
            if (_Initialized)
            {
                Bass.BASS_SetVolume(Volume / 100f);
            }
        }

        public int GetStreamCount()
        {
            if (!_Initialized)
                return 0;

            lock (MutexAudioStreams)
            {
                return _Streams.Count;
            }
        }

        public void Update()
        {
        }

        #region Stream Handling
        public int Load(string Media)
        {
            return Load(Media, false);
        }

        public int Load(string Media, bool Prescan)
        {
            if (!_Initialized)
                return 0;

            BASSFlag flags = BASSFlag.BASS_DEFAULT;
            if (Prescan)
                flags = BASSFlag.BASS_STREAM_PRESCAN;

            AudioStreams stream = new AudioStreams(0);
            stream.handle = Bass.BASS_StreamCreateFile(Media, 0L, 0L, flags);

            if (stream.handle != 0)
            {
                stream.file = Media;
                lock (MutexAudioStreams)
                {
                    _Streams.Add(stream);
                    _Volumes.Add(new CVolumes());
                }

                return stream.handle;
            }
            return 0;
        }

        public void Close(int Stream)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        Bass.BASS_StreamFree(Stream);
                        _Volumes.RemoveAt(GetStreamIndex(Stream));
                        _Streams.RemoveAt(GetStreamIndex(Stream));
                    }
                }

            }
        }

        public void Play(int Stream)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        Play(Stream, false);
                    }
                }

            }
        }

        public void Play(int Stream, bool Loop)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        if (Loop)
                            Bass.BASS_ChannelFlags(Stream, BASSFlag.BASS_SAMPLE_LOOP, BASSFlag.BASS_SAMPLE_LOOP);

                        Bass.BASS_ChannelPlay(Stream, false);
                    }
                }

            }
        }

        public void Pause(int Stream)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        Bass.BASS_ChannelPause(Stream);
                    }
                }

            }
        }

        public void Stop(int Stream)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        Bass.BASS_ChannelStop(Stream);
                    }
                }

            }
        }

        public void Fade(int Stream, float TargetVolume, float Seconds)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        float maxVol = _Volumes[GetStreamIndex(Stream)].VolumeMax;
                        Bass.BASS_ChannelSlideAttribute(Stream, BASSAttribute.BASS_ATTRIB_VOL, TargetVolume * maxVol / 100f, (int)Math.Round(Seconds * 1000f));
                    }
                }

            }
        }

        public void FadeAndPause(int Stream, float TargetVolume, float Seconds)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        float maxVol = _Volumes[GetStreamIndex(Stream)].VolumeMax;
                        Bass.BASS_ChannelSlideAttribute(Stream, BASSAttribute.BASS_ATTRIB_VOL, TargetVolume * maxVol / 100f, (int)Math.Round(Seconds * 1000f));
                        Bass.BASS_ChannelSetSync(Stream, BASSSync.BASS_SYNC_SLIDE, 0L, _SyncSlideAndPause, IntPtr.Zero);
                    }
                }

            }
        }

        public void FadeAndStop(int Stream, float TargetVolume, float Seconds)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        float maxVol = _Volumes[GetStreamIndex(Stream)].VolumeMax;
                        Bass.BASS_ChannelSlideAttribute(Stream, BASSAttribute.BASS_ATTRIB_VOL, TargetVolume * maxVol / 100f, (int)Math.Round(Seconds * 1000f));
                        Bass.BASS_ChannelSetSync(Stream, BASSSync.BASS_SYNC_SLIDE, 0L, _SyncSlideAndStop, IntPtr.Zero);
                    }
                }

            }
        }

        public void SetStreamVolume(int Stream, float Volume)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        float maxVol = _Volumes[GetStreamIndex(Stream)].VolumeMax;
                        _Volumes[GetStreamIndex(Stream)].Volume = Volume / 100f;
                        Bass.BASS_ChannelSetAttribute(Stream, BASSAttribute.BASS_ATTRIB_VOL, Volume * maxVol / 100f);
                    }
                }

            }
        }

        public void SetStreamVolumeMax(int Stream, float Volume)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        float Vol = _Volumes[GetStreamIndex(Stream)].Volume;
                        _Volumes[GetStreamIndex(Stream)].VolumeMax = Volume / 100f;
                        Bass.BASS_ChannelSetAttribute(Stream, BASSAttribute.BASS_ATTRIB_VOL, Volume * Vol / 100f);
                    }
                }

            }
        }

        public float GetLength(int Stream)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        long len = Bass.BASS_ChannelGetLength(Stream);
                        return (float)Bass.BASS_ChannelBytes2Seconds(Stream, len);
                    }
                }

                return 0f;
            }
            return 0f;
        }

        public float GetPosition(int Stream)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        long pos = Bass.BASS_ChannelGetPosition(Stream);
                        return (float)Bass.BASS_ChannelBytes2Seconds(Stream, pos);
                    }
                }

                return 0f;
            }
            return 0f;
        }

        public bool IsPlaying(int Stream)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        return (Bass.BASS_ChannelIsActive(Stream) == BASSActive.BASS_ACTIVE_PLAYING);
                    }
                }

            }
            return false;
        }

        public bool IsPaused(int Stream)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        return (Bass.BASS_ChannelIsActive(Stream) == BASSActive.BASS_ACTIVE_PAUSED);
                    }
                }

            }
            return false;
        }

        public bool IsFinished(int Stream)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        return (Bass.BASS_ChannelIsActive(Stream) == BASSActive.BASS_ACTIVE_STOPPED);
                    }
                }

            }
            return true;
        }

        public void SetPosition(int Stream, float Position)
        {
            if (_Initialized)
            {
                lock (MutexAudioStreams)
                {
                    if (AlreadyAdded(Stream))
                    {
                        Bass.BASS_ChannelSetPosition(Stream, Position);
                    }
                }

            }
        }
        #endregion Stream Handling

        private bool AlreadyAdded(int Stream)
        {
            foreach (AudioStreams st in _Streams)
            {
                if (st.handle == Stream)
                {
                    return true;
                }
            }
            return false;
        }

        private int GetStreamIndex(int Stream)
        {
            for (int i = 0; i < _Streams.Count; i++)
            {
                if (_Streams[i].handle == Stream)
                    return i;
            }
            return -1;
        }

        private void PauseSync(int handle, int Stream, int data, IntPtr user)
        {
            if (_Initialized)
            {
                if (AlreadyAdded(Stream))
                {
                    Pause(Stream);
                }
            }
        }

        private void EndSync(int handle, int Stream, int data, IntPtr user)
        {
            if (_Initialized)
            {
                if (AlreadyAdded(Stream))
                {
                    Close(Stream);
                }
            }
        }

        private void ReadRegistrationFile(string FileName, ref string Email, ref string Code)
        {
            CXMLReader xmlReader = CXMLReader.OpenFile(FileName);

            if (xmlReader != null)
            {
                xmlReader.GetValue("//root/Registration/EMail", ref Email, String.Empty);
                xmlReader.GetValue("//root/Registration/Code", ref Code, String.Empty);
            }
        }

        private void WriteRegistrationFile(string FileName, string Email, string Code)
        {
            XmlWriterSettings _settings = new XmlWriterSettings();
            _settings.Indent = true;
            _settings.Encoding = Encoding.UTF8;
            _settings.ConformanceLevel = ConformanceLevel.Document;

            XmlWriter writer;

            try
            {
                writer = XmlWriter.Create(FileName, _settings);
            }
            catch (Exception)
            {
                return;
            }

            writer.WriteStartDocument();
            writer.WriteStartElement("root");

            writer.WriteStartElement("Registration");
            writer.WriteComment("Register your BASS.NET version and suppress the freeware splash screen (http://bass.radio42.com/bass_register.html)");

            writer.WriteElementString("EMail", Email);
            writer.WriteElementString("Code", Code);

            writer.WriteEndElement();

            // End of File
            writer.WriteEndElement(); //end of root
            writer.WriteEndDocument();

            writer.Flush();
            writer.Close();
        }
    }
}