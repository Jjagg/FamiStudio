﻿using System;
using System.Runtime.InteropServices;

namespace FamiStudio
{
    public class Midi
    {
        internal const int MMSYSERR_NOERROR = 0;
        internal const int CALLBACK_FUNCTION = 0x00030000;

        internal const int MM_MIM_OPEN      = 0x3C1;
        internal const int MM_MIM_CLOSE     = 0x3C2;
        internal const int MM_MIM_DATA      = 0x3C3;
        internal const int MM_MIM_LONGDATA  = 0x3C4;
        internal const int MM_MIM_ERROR     = 0x3C5;
        internal const int MM_MIM_LONGERROR = 0x3C6;

        internal const int STATUS_NOTE_ON   = 0x90;
        internal const int STATUS_NOTE_OFF  = 0x80;

        internal delegate void MidiInProc(IntPtr hMidiIn, int wMsg, IntPtr dwInstance, int dwParam1, int dwParam2);
        [DllImport("winmm.dll")]
        internal static extern int midiInGetNumDevs();
        [DllImport("winmm.dll")]
        internal static extern int midiInClose(IntPtr hMidiIn);
        [DllImport("winmm.dll")]
        internal static extern int midiInOpen(out IntPtr lphMidiIn, int uDeviceID, MidiInProc dwCallback, IntPtr dwCallbackInstance, int dwFlags);
        [DllImport("winmm.dll")]
        internal static extern int midiInStart(IntPtr hMidiIn);
        [DllImport("winmm.dll")]
        internal static extern int midiInStop(IntPtr hMidiIn);

        public delegate void MidiNoteDelegate(int note, bool on);
        public event MidiNoteDelegate NotePlayed;

        private MidiInProc midiInProc;
        private IntPtr handle;

        public Midi()
        {
            midiInProc = new MidiInProc(MidiProc);
            handle = IntPtr.Zero;
        }

        public static int InputCount
        {
            get { return midiInGetNumDevs(); }
        }

        public bool Close()
        {
            bool result = midiInClose(handle) == MMSYSERR_NOERROR;
            handle = IntPtr.Zero;
            return result;
        }

        public bool Open(int id)
        {
            return midiInOpen(out handle, id, midiInProc, IntPtr.Zero, CALLBACK_FUNCTION) == MMSYSERR_NOERROR;
        }

        public bool Start()
        {
            return midiInStart(handle) == MMSYSERR_NOERROR;
        }

        public bool Stop()
        {
            return midiInStop(handle) == MMSYSERR_NOERROR;
        }

        private void MidiProc(IntPtr hMidiIn, int wMsg, IntPtr dwInstance, int dwParam1, int dwParam2)
        {
            if (wMsg == MM_MIM_DATA)
            {
                int note = (dwParam1 >> 8) & 0xff;
                int status = dwParam1 & 0xf0;

                if (status == STATUS_NOTE_ON)
                {
                    NotePlayed?.Invoke(note, true);
                }
                else if (status == STATUS_NOTE_OFF)
                {
                    NotePlayed?.Invoke(note, false);
                }
            }
        }
    }
}
