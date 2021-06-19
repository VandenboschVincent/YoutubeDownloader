using System;
using System.Collections.Generic;
using System.Text;

namespace YoutubeDownloader
{
    public class Formats
    {
        public const string mp3 = "mp3";
        public const string wav = "wav";
        public const string ogg = "ogg";
        public const string mp4 = "mp4";

        public static readonly List<string> MusicFormats = new() { mp3, wav, ogg };
        public static readonly List<string> VideoFormats = new() { mp4 };
        public static readonly List<string> AllFormats = new() { mp3, wav, ogg, mp4 };
    }
}
