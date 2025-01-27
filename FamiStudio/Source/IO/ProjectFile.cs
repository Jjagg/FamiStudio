﻿using System;
using System.IO;
using System.IO.Compression;

namespace FamiStudio
{
    public static class ProjectFile
    {
        const uint MagicNumber = 0x21534D46;

        public static Project Load(string filename)
        {
            try
            {
                using (var stream = File.OpenRead(filename))
                {
                    var data = new byte[4];
                    stream.Read(data, 0, 4);
                    if (BitConverter.ToUInt32(data, 0) != MagicNumber)
                    {
                        stream.Close();
                        return null;
                    }

                    stream.Read(data, 0, 4);
                    int loadVersion = BitConverter.ToInt32(data, 0);

                    if (loadVersion > Project.Version)
                    {
                        // TODO: Error message.
                        return null;
                    }

                    var buffer = new byte[stream.Length - stream.Position];
                    stream.Read(buffer, 0, buffer.Length);
                    buffer = Compression.DecompressBytes(buffer);

                    var project = new Project();
                    var serializer = new ProjectLoadBuffer(project, buffer, loadVersion);
                    project.SerializeState(serializer);
                    project.Filename = filename;
                    return project;
                }
            }
            catch
            {
                return null;
            }
        }

        public static bool Save(Project project, string filename)
        {
            try
            {
                var serializer = new ProjectSaveBuffer(project);
                project.SerializeState(serializer);
                var buffer = serializer.GetBuffer();

                using (var stream = File.Create(filename))
                {
                    stream.Write(BitConverter.GetBytes(MagicNumber), 0, 4);
                    stream.Write(BitConverter.GetBytes(Project.Version), 0, 4);

                    buffer = Compression.CompressBytes(buffer, CompressionLevel.Optimal);
                    stream.Write(buffer, 0, buffer.Length);
                    stream.Flush();
                    stream.Close();

                    project.Filename = filename;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
