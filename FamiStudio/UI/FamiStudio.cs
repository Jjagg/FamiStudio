﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FamiStudio
{
    public partial class FamiStudioForm : Form
    {
        // TODO: Get rid of this!
        public static Project StaticProject { get; private set; }

        private Project project;
        private Song song;
        private SongPlayer songPlayer;
        private InstrumentPlayer instrumentPlayer;
        private Midi midi;
        private Timer playTimer = new Timer();
        private UndoRedoManager undoRedoManager;
        private int currentFrame = 0;
        private int ghostChannelMask = 0;

        private bool newReleaseAvailable = false;
        private string newReleaseString = null;
        private string newReleaseUrl = null;
        
        public bool RealTimeUpdate => songPlayer.IsPlaying || pianoRoll.IsEditingInstrument;
        public bool IsPlaying => songPlayer.IsPlaying;
        public int CurrentFrame => currentFrame;
        public int ChannelMask { get => songPlayer.ChannelMask; set => songPlayer.ChannelMask = value; }
        public string ToolTip { get => toolbar.ToolTip; set => toolbar.ToolTip = value; }
        public Project Project => project;
        public Song Song => song;
        public UndoRedoManager UndoRedoManager => undoRedoManager;
        public LoopMode LoopMode { get => songPlayer.Loop; set => songPlayer.Loop = value; }

        public FamiStudioForm(string filename = null)
        {
            InitializeComponent();

            songPlayer = new SongPlayer();
            songPlayer.Initialize();
            instrumentPlayer = new InstrumentPlayer();
            instrumentPlayer.Initialize();

            if (Midi.InputCount > 0)
            {
                midi = new Midi();

                if (midi.Open(0) && midi.Start())
                    midi.NotePlayed += Midi_NotePlayed;
                else
                    midi = null;
            }

            playTimer.Tick += playTimer_Tick;
            playTimer.Interval = 4;
            playTimer.Start();

            if (!string.IsNullOrEmpty(filename))
            {
                OpenProject(filename);
            }
            else
            {
                NewProject();
            }

            Task.Factory.StartNew(CheckForNewRelease);
        }

        private void UndoRedoManager_Updated()
        {
            toolbar.Invalidate();
        }

        public bool CheckUnloadProject()
        {
            if (undoRedoManager != null)
            {
                if (undoRedoManager.UndoScope != TransactionScope.Max)
                {
                    var result = MessageBox.Show("Save changes?", "FamiStudio", MessageBoxButtons.YesNoCancel);
                    if (result == DialogResult.Cancel)
                    {
                        return false;
                    }

                    if (result == DialogResult.Yes)
                    {
                        if (!SaveProject())
                        {
                            return false;
                        }
                    }
                }

                undoRedoManager.Updated -= UndoRedoManager_Updated;
                undoRedoManager = null;
                project = null;

                StopEverything();
            }

            return true;
        }

        public void NewProject()
        {
            if (!CheckUnloadProject())
            {
                return;
            }

            project = new Project(true);
            InitProject();
        }

        private void InitProject()
        {
            StaticProject = project;
            song = project.Songs[0];

            undoRedoManager = new UndoRedoManager(project, this);
            undoRedoManager.Updated += UndoRedoManager_Updated;

            currentFrame = 0;
            toolbar.Reset();
            projectExplorer.Reset();
            pianoRoll.Reset();
            sequencer.Reset();
            pianoRoll.CurrentInstrument = projectExplorer.SelectedInstrument;
            InvalidateEverything();
            UpdateTitle();
        }

        public void OpenProject(string filename)
        {
            StopEverything();

            if (!CheckUnloadProject())
            {
                return;
            }

            if (filename.ToLower().EndsWith("fms"))
            {
                project = ProjectFile.Load(filename);
            }
            else if (filename.ToLower().EndsWith("txt"))
            {
                project = FamitrackerFile.Load(filename);
            }

            if (project != null)
            {
                InitProject();
            }
            else 
            {
                NewProject();
            }
        }

        public void OpenProject()
        {
            var ofd = new OpenFileDialog()
            {
                Filter = "All Supported Files (*.fms;*.txt)|*.fms;*.txt|FamiStudio Files (*.fms)|*.fms|Famitracker Text Export (*.txt)|*.txt",
                Title = "Open File"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                OpenProject(ofd.FileName);
            }
        }

        public bool SaveProject(bool forceSaveAs = false)
        {
            bool success = true;

            if (forceSaveAs || string.IsNullOrEmpty(project.Filename))
            {
                var sfd = new SaveFileDialog()
                {
                    Filter = "FamiStudio Files (*.fms)|*.fms",
                    Title = "Save File"
                };

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    success = ProjectFile.Save(project, sfd.FileName);
                    if (success)
                    {
                        UpdateTitle();
                    }
                }
            }
            else
            {
                success = ProjectFile.Save(project, project.Filename);
            }

            if (success)
            {
                undoRedoManager.Clear();
            }
            else
            {
                MessageBox.Show("An error happened while saving.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            InvalidateEverything();

            return true;
        }

        public bool Export()
        {
            var dlg = new ExportDialog(project);
            return dlg.ShowDialog() == DialogResult.OK;
        }

        private void CheckForNewRelease()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri("https://api.github.com/repos/BleuBleu/FamiStudio/releases/latest");
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("FamiStudio");
                    var response = client.GetAsync("").Result;
                    if (response.IsSuccessStatusCode)
                    {
                        var json = response.Content.ReadAsStringAsync().Result;
                        var jsonSerializer = new JavaScriptSerializer();

                        dynamic release = jsonSerializer.Deserialize<dynamic>(json);

                        newReleaseString = release["tag_name"].ToString();
                        newReleaseUrl    = release["html_url"].ToString();

                        // Assume > alphabetical order means newer version.
                        if (newReleaseString.CompareTo(Application.ProductVersion) > 0)
                        {
                            newReleaseAvailable = true;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void CheckNewReleaseDone()
        {
            if (newReleaseAvailable)
            {
                newReleaseAvailable = false;

                if (MessageBox.Show($"A new release ({newReleaseString}) is available. Do you want to go to GitHub to download it?", "New Version", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    Process.Start(newReleaseUrl);
                }
            }
        }

        private void UpdateTitle()
        {
            string projectFile = "New Project";

            if (!string.IsNullOrEmpty(project.Filename))
                projectFile = project.Filename;

            var version = Application.ProductVersion.Substring(0, Application.ProductVersion.LastIndexOf('.'));

            Text = $"FamiStudio {version} - {projectFile}";
        }

        public void PlayInstrumentNote(int n, bool on)
        {
            Note note = new Note();
            note.Value = (byte)n;

            int channel = sequencer.SelectedChannel;
            if (projectExplorer.SelectedInstrument == null)
                channel = 4;
            else
                note.Instrument = projectExplorer.SelectedInstrument;

            instrumentPlayer.PlayNote(channel, note);
        }

        public void StopIntrumentNote()
        {
            instrumentPlayer.StopAllNotes();
        }

        public void StopInstrumentNoteAndWait()
        {
            instrumentPlayer.StopAllNotesAndWait();
        }

        public void StopEverything()
        {
            Stop();
            StopInstrumentNoteAndWait();
        }

        private void Midi_NotePlayed(int n, bool on)
        {
            if (on)
            {
                // In MIDI: 60 = C4, In Famitone 37 = C4
                PlayInstrumentNote(Math.Max(1, Math.Min(n - 23, 63)), on);
            }
            else
            {
                //StopIntrumentNote();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!CheckUnloadProject())
            {
                e.Cancel = true;
                return;
            }

            if (midi != null)
            {
                midi.Stop();
                midi.Close();
                midi = null;
            }

            songPlayer.Shutdown();
            instrumentPlayer.Shutdown();
            base.OnFormClosing(e);
        }

        public void Play()
        {
            if (!songPlayer.IsPlaying)
            {
                songPlayer.Play(song, currentFrame);
            }
        }

        public void Stop()
        {
            if (songPlayer.IsPlaying)
            {
                songPlayer.Stop();
                currentFrame = songPlayer.CurrentFrame;
                InvalidateEverything();
            }
        }

        public void Seek(int frame)
        {
            bool wasPlaying = songPlayer.IsPlaying;
            if (wasPlaying) Stop();
            currentFrame = Math.Min(frame, song.Length * song.PatternLength - 1);
            if (wasPlaying) Play();
            InvalidateEverything();
        }

        public void SeekCurrentPattern()
        {
            bool wasPlaying = songPlayer.IsPlaying;
            if (wasPlaying) Stop();
            currentFrame = currentFrame - (currentFrame % song.PatternLength);
            if (wasPlaying) Play();
            InvalidateEverything();
        }

        public int GhostChannelMask
        {
            get { return ghostChannelMask; }
            set
            {
                ghostChannelMask = value;
                pianoRoll.ConditionalInvalidate();
            }
        }

        public int GetEnvelopeFrame(int envelopeIdx)
        {
            return instrumentPlayer.GetEnvelopeFrame(envelopeIdx);
        }

        private void InvalidateEverything()
        {
            toolbar.Invalidate();
            sequencer.Invalidate();
            pianoRoll.Invalidate();
            //projectExplorer.Invalidate();
        }

        private void playTimer_Tick(object sender, EventArgs e)
        {
            if (RealTimeUpdate)
            {
                songPlayer.CheckIfEnded();
                if (songPlayer.IsPlaying)
                    currentFrame = songPlayer.CurrentFrame;
                InvalidateEverything();
            }

            CheckNewReleaseDone();
        }
        
        private void sequencer_PatternClicked(int trackIndex, int patternIndex)
        {
            pianoRoll.StartEditPattern(trackIndex, patternIndex);
        }

        private void pianoRoll_PatternChanged(Pattern pattern)
        {
            sequencer.NotifyPatternChange(pattern);
            sequencer.Invalidate();
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl  = e.Modifiers.HasFlag(Keys.Control);
            bool shift = e.Modifiers.HasFlag(Keys.Shift);

            if (e.KeyCode == Keys.Space)
            {
                if (IsPlaying)
                {
                    Stop();
                }
                else
                {
                    if (ctrl)
                    {
                        LoopMode = LoopMode.Pattern;
                        Seek(currentFrame / song.PatternLength * song.PatternLength);
                    }
                    else if (shift)
                    {
                        LoopMode = LoopMode.Song;
                    }

                    Play();
                }
            }
            else if (e.KeyCode == Keys.Home)
            {
                if (ctrl)
                {
                    SeekCurrentPattern();
                }
                else
                {
                    Seek(0);
                }
            }
            else if (ctrl && e.KeyCode == Keys.Z)
            {
                undoRedoManager.Undo();
            }
            else if (ctrl && e.KeyCode == Keys.Y)
            {
                undoRedoManager.Redo();
            }
            else if (ctrl && e.KeyCode == Keys.N)
            {
                NewProject();
            }
            else if (ctrl && e.KeyCode == Keys.S)
            {
                SaveProject();
            }
            else if (ctrl && e.KeyCode == Keys.E)
            {
                Export();
            }
            else if (ctrl && e.KeyCode == Keys.O)
            {
                OpenProject();
            }
        }

        private void projectExplorer_InstrumentEdited(Instrument instrument, int envelope)
        {
            if (instrument == null)
            {
                pianoRoll.StartEditDPCMSamples();
            }
            else
            {
                pianoRoll.StartEditEnveloppe(instrument, envelope);
            }
        }

        private void pianoRoll_EnvelopeResized()
        {
            projectExplorer.Invalidate();
        }

        public void SerializeState(ProjectBuffer buffer)
        {
            buffer.Serialize(ref ghostChannelMask);
            buffer.Serialize(ref song);

            projectExplorer.SerializeState(buffer);
            sequencer.SerializeState(buffer);
            pianoRoll.SerializeState(buffer);

            if (buffer.IsReading)
                Invalidate();
        }

        private void projectExplorer_InstrumentSelected(Instrument instrument)
        {
            pianoRoll.CurrentInstrument = instrument;
        }

        private void projectExplorer_InstrumentColorChanged(Instrument instrument)
        {
            sequencer.InvalidatePatternCache();
        }

        private void projectExplorer_SongModified(Song song)
        {
            sequencer.InvalidatePatternCache();
            pianoRoll.ClampScroll();
            InvalidateEverything();
        }

        private void projectExplorer_SongSelected(Song song)
        {
            Stop();
            this.currentFrame = 0;
            this.song = song;
            sequencer.InvalidatePatternCache();
            pianoRoll.ClampScroll();
            InvalidateEverything();
        }

        private void projectExplorer_InstrumentReplaced(Instrument instrument)
        {
            sequencer.InvalidatePatternCache();
            InvalidateEverything();
        }
    }
}
