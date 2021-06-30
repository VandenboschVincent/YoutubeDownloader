using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json.Linq;
using TagLib;
using TagLib.Mpeg4;
using YoutubeDownloader.Utils;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using File = TagLib.File;

namespace YoutubeDownloader.Services
{
    public class TaggingService
    {
        private readonly HttpClient _httpClient = new();

        private readonly SemaphoreSlim _requestRateSemaphore = new(1, 1);
        private DateTimeOffset _lastRequestInstant = DateTimeOffset.MinValue;

        public TaggingService()
        {
            // MusicBrainz requires user agent to be set
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{App.Name} ({App.GitHubProjectUrl})");
        }

        private async Task MaintainRateLimitAsync(
            TimeSpan interval,
            CancellationToken cancellationToken =default)
        {
            // Gain lock
            await _requestRateSemaphore.WaitAsync(cancellationToken);

            try
            {
                // Wait until enough time has passed since last request
                var timePassedSinceLastRequest = DateTimeOffset.Now - _lastRequestInstant;
                var remainingTime = interval - timePassedSinceLastRequest;
                if (remainingTime > TimeSpan.Zero)
                    await Task.Delay(remainingTime, cancellationToken);

                _lastRequestInstant = DateTimeOffset.Now;
            }
            finally
            {
                // Release the lock
                _requestRateSemaphore.Release();
            }
        }

        private async Task<JToken> TryGetMusicBrainzTagsJsonAsync(
            string artist,
            string title,
            CancellationToken cancellationToken = default)
        {
            var url = Uri.EscapeUriString(
                "http://musicbrainz.org/ws/2/recording/?fmt=json&query=" +
                $"artist:\"{artist}\" AND recording:\"{title}\"");

            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                var raw = await response.Content.ReadAsStringAsync();

                return JToken.Parse(raw)["recordings"]?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private async Task<IPicture> TryGetPictureAsync(
            IVideo video,
            CancellationToken cancellationToken = default,
            string url = null)
        {
            try
            {
                var thumbnail = video.Thumbnails.TryGetWithHighestResolution();
                if (thumbnail is null)
                    return null;

                using var response = await _httpClient.GetAsync(
                    url ?? thumbnail.Url,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken
                );

                if (!response.IsSuccessStatusCode)
                    return null;
                var data = await response.Content.ReadAsByteArrayAsync();
                var cover = new TagLib.Id3v2.AttachmentFrame
                {
                    Type = PictureType.FrontCover,
                    Description = "Cover",
                    MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg,
                    Data = new ByteVector(data),
                    TextEncoding = StringType.Latin1
                };
                var pic = new IPicture[] { cover };

                return pic.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        //private bool TryExtractArtistAndTitle(
        //    string videoTitle,
        //    out string artist,
        //    out string title)
        //{
        //    // Get rid of common rubbish in music video titles
        //    videoTitle = videoTitle.Replace("(official video)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(official lyric video)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(official music video)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(official audio)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(official)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(lyric video)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(lyrics)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(acoustic video)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(acoustic)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(live)", "", StringComparison.OrdinalIgnoreCase);
        //    videoTitle = videoTitle.Replace("(animated video)", "", StringComparison.OrdinalIgnoreCase);

        //    // Split by common artist/title separator characters
        //    var split = videoTitle.Split(new[] {" - ", " ~ ", " — ", " – "}, StringSplitOptions.RemoveEmptyEntries);

        //    // Extract artist and title
        //    if (split.Length >= 2)
        //    {
        //        artist = split[0].Trim();
        //        title = split[1].Trim();
        //        return true;
        //    }

        //    if (split.Length == 1)
        //    {
        //        artist = null;
        //        title = split[0].Trim();
        //        return true;
        //    }

        //    artist = null;
        //    title = null;
        //    return false;
        //}

        private async Task<TaggingSuccesfull> InjectVideoTagsAsync(
            IVideo video,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            var picture = await TryGetPictureAsync(video, cancellationToken);

            var file = File.Create(filePath);

            var appleTag = file.GetTag(TagTypes.Apple) as AppleTag;
            appleTag?.SetDashBox("Channel", "Channel", video.Author.Title);

            var picturestoadd = picture is not null
                ? new[] { picture }
                : Array.Empty<IPicture>();

            file.Tag.Pictures = picturestoadd;

            file.Save();

            return new TaggingSuccesfull() { FileName = filePath, Succesfull = true };
        }

        public async Task<string> ResetTagging(
            IVideo video,
            string filePath,
            string format,
            CancellationToken cancellationToken = default
            )
        {
            FileInfo info = new FileInfo(filePath);
            string filetitle;

            var file = File.Create(filePath);

            filetitle = FileNameGenerator.GenerateFileName(
                FileNameGenerator.DefaultTemplate
                , video
                , format);

            file.Tag.Album = null;
            file.Tag.Performers = new List<string>().ToArray();
            file.Tag.Title = null;
            file.Tag.Subtitle = null;
            file.Tag.Description = null;
            file.Tag.BeatsPerMinute = 0;
            file.Tag.Genres = new List<string>().ToArray();
            file.Tag.Year = 0;

            var picture = await TryGetPictureAsync(video, cancellationToken);
            IPicture[] pictFrames = new IPicture[1];
            pictFrames[0] = picture;
            var picturestoadd = pictFrames;

            file.Tag.Pictures = picturestoadd;

            file.Save();
            file.Dispose();

            string newfilepath = Path.Combine(info.Directory.FullName, filetitle);
            info.MoveTo(newfilepath, true);
            return newfilepath;
        }

        public async Task<TaggingSuccesfull> InjectAudioTagsAsync(
            IVideo video,
            string filePath,
            string format,
            bool AutoRename,
            List<string> shazamapikeys,
            List<string> vagalumeapikeys,
            CancellationToken cancellationToken = default,
            string forcetitle = null,
            string forceartist = null)
        {
            // 4 requests per second
            //await MaintainRateLimitAsync(TimeSpan.FromSeconds(1.0 / 4), cancellationToken);

            FileInfo info = new FileInfo(filePath);
            string filetitle = info.Name.Replace(info.Extension, "");
            bool forced = false;
            string picture = null;
            var removetitlejunk = new Regex(@"\(([0-9)]*)\)");

            var file = File.Create(filePath);

            double biggestchange = 0;

            if (!string.IsNullOrWhiteSpace(forcetitle) || !string.IsNullOrWhiteSpace(forceartist))
            {
                filetitle = $"{forceartist} - {forcetitle}";
                forced = true;
            }

            var searchresponse = await MusicInfo.Search(new DiscogsConnect.SearchCriteria()
            {
                Query = filetitle
                , Title = forcetitle
                , Artist = forceartist
            });

            string originalfiletitle = filetitle;
            file.Tag.Comment = video.Url;
            forceartist = RemoveDiacritics(forceartist);
            forcetitle = RemoveDiacritics(forcetitle);

            // releases
            bool releasesfound = false;
            var releases = searchresponse.Items.Where(t => t.Type == DiscogsConnect.ResourceType.Release);
            if (releases //.Where(t => ((DiscogsConnect.ReleaseSearchResult)t).Formats.Contains("Single") || ((DiscogsConnect.ReleaseSearchResult)t).Formats.Contains("File"))
                .OrderByDescending(
                t => 
                    (forced == true ? 
                    (Convert.ToInt16(RemoveDiacritics(t.Title).Contains(forceartist)) + Convert.ToInt16(RemoveDiacritics(t.Title).Contains(forcetitle))) 
                    : (LevenshteinDistance.Calculate(t.Title, originalfiletitle) * -1))
                    + Convert.ToInt16(((DiscogsConnect.ReleaseSearchResult)t).Formats.Contains("Single") || ((DiscogsConnect.ReleaseSearchResult)t).Formats.Contains("File"))
                )
                .FirstOrDefault() is DiscogsConnect.ReleaseSearchResult single && single != null)
            {
                var diff = LevenshteinDistance.CalculatePercentage(single.Title, originalfiletitle, true);
                if (diff > biggestchange)
                    biggestchange = diff;
                if (diff > 30)
                {
                    //var picture = await TryGetPictureAsync(video, cancellationToken, response.Thumb);
                    picture = single.Thumb;

                    string singlename = removetitlejunk.Replace(single.Title, "");
                    singlename = singlename.Replace("  ", " ");
                    //var resolvedArtist = tagsJson?["artist-credit"]?.FirstOrDefault()?["name"]?.Value<string>();
                    //var resolvedTitle = tagsJson?["title"]?.Value<string>();
                    //var resolvedAlbumName = track ?? tagsJson?["releases"]?.FirstOrDefault()?["title"]?.Value<string>();
                    //var releasedate = tagsJson?["first-release-date"]?.Value<string>();

                    var resolvedArtist = singlename.Split(" - ", StringSplitOptions.RemoveEmptyEntries).First();
                    var resolvedTitle = singlename.Split(" - ", StringSplitOptions.RemoveEmptyEntries).Last();

                    var styles = single.Styles;
                    var genres = single.Genres;

                    List<string> performers = resolvedArtist.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (performers.Count < 2)
                    {
                        performers = resolvedArtist.Split(" And ", StringSplitOptions.RemoveEmptyEntries).ToList();
                    }
                    performers = performers.Select(s => s.Trim()).ToList();

                    file.Tag.Performers = performers.ToArray();
                    file.Tag.Title = resolvedTitle;
                    file.Tag.Album = resolvedTitle;

                    if (genres.Any())
                        file.Tag.Genres = genres.ToArray();
                    if (styles.Any())
                        file.Tag.Description = string.Join(", ", styles);

                    filetitle = singlename;

                    releasesfound = true;
                }
            }

            //albums
            bool albumfound = false;
            if (releases //.Where(t => ((DiscogsConnect.ReleaseSearchResult)t).Formats.Contains("Album"))
                .OrderByDescending(
                t =>
                    (forced == true ?
                    (Convert.ToInt16(RemoveDiacritics(t.Title).Contains(forceartist)) + Convert.ToInt16(RemoveDiacritics(t.Title).Contains(forcetitle)))
                    : (LevenshteinDistance.Calculate(t.Title, originalfiletitle) * -1))
                    + Convert.ToInt16(((DiscogsConnect.ReleaseSearchResult)t).Formats.Contains("Album"))
                )
                .FirstOrDefault() is DiscogsConnect.ReleaseSearchResult album && album != null)
            {
                var diff = LevenshteinDistance.CalculatePercentage(album.Title, originalfiletitle, true);
                if (diff > biggestchange)
                    biggestchange = diff;
                if (diff > 20)
                {
                    string singlename = removetitlejunk.Replace(album.Title, "");
                    singlename = singlename.Replace("  ", " ");

                    file.Tag.Album = singlename.Split(" - ", StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    var releaseyear = album.Year;
                    if (releaseyear > 0)
                        file.Tag.Year = Convert.ToUInt32(releaseyear);

                    picture = album.Thumb;

                    if (!releasesfound)
                    {
                        var resolvedArtist = singlename.Split(" - ", StringSplitOptions.RemoveEmptyEntries).First();
                        List<string> performers = resolvedArtist.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList();
                        if (performers.Count < 2)
                        {
                            performers = resolvedArtist.Split(" And ", StringSplitOptions.RemoveEmptyEntries).ToList();
                        }
                        performers = performers.Select(s => s.Trim()).ToList();
                        file.Tag.Performers = performers.ToArray();
                        file.Tag.Title = filetitle.Split(" - ", StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    }
                    albumfound = true;
                }
            }

            if((!albumfound && !releasesfound) || biggestchange < 30)
            {
                if (ShazamMusicInfo.TryExtractArtistAndTitle(filetitle
                , shazamapikeys
                , vagalumeapikeys
                , out string artist
                , out string title
                , out string picturelink
                , out string track))
                {
                    var diff = LevenshteinDistance.CalculatePercentage($"{artist} - {title}", originalfiletitle, true);
                    if (forced == true ?
                        RemoveDiacritics(artist).Contains(forceartist) && RemoveDiacritics(title).Contains(forcetitle)
                        : diff > 30 && diff > biggestchange)
                    {
                        picture = picturelink;
                        file.Tag.Title = title;
                        file.Tag.Performers = new string[] { artist };
                        file.Tag.Album = title;

                        filetitle = $"{artist} - {title}";
                        releasesfound = true;
                    }
                }
            }
            
            //BPMDetector only in 32bit environment
            if (!Environment.Is64BitProcess)
            {
                double bitrate = 44100;
                if (string.Equals(format, Formats.wav, StringComparison.OrdinalIgnoreCase))
                    using (var reader = new WaveFileReader(filePath))
                    {
                        bitrate = reader.WaveFormat.AverageBytesPerSecond * 8;
                        bitrate = bitrate / 1000;
                    }
                if (string.Equals(format, Formats.mp3, StringComparison.OrdinalIgnoreCase))
                    using (var reader = new Mp3FileReader(filePath))
                    {
                        bitrate = reader.WaveFormat.AverageBytesPerSecond * 8;
                        bitrate = bitrate / 1000;
                    }

                var bpmdetector = new BPMDetector(filePath, Convert.ToInt32(bitrate));
                var bpm = bpmdetector.getBPM();

                file.Tag.BeatsPerMinute = Convert.ToUInt32(bpm);
            }

            var pic = await TryGetPictureAsync(video, cancellationToken, picture);
            IPicture[] pictFrames = new IPicture[1];
            pictFrames[0] = pic;
            var picturestoadd = pictFrames;

            file.Tag.Pictures = picturestoadd;

            file.Save();
            file.Dispose();

            if (AutoRename)
            {
                string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
                foreach (char c in invalid)
                {
                    filetitle = filetitle.Replace(c.ToString(), "");
                }
                string newfilepath = Path.Combine(info.Directory.FullName, $"{filetitle}{info.Extension}");
                                
                if (FileExistsCaseSensitive(newfilepath))
                {
                    var existingfile = File.Create(newfilepath);
                    if (video.Url != existingfile.Tag.Comment)
                    {
                        newfilepath = PathEx.MakeUniqueFilePath(newfilepath);
                        existingfile.Dispose();
                    }
                }

                info.MoveTo(newfilepath, true);
                return new TaggingSuccesfull() { FileName = newfilepath, Succesfull = albumfound || releasesfound };
            }

            return new TaggingSuccesfull() { FileName = filePath, Succesfull = albumfound || releasesfound };
        }

        public async Task<TaggingSuccesfull> InjectTagsAsync(
            IVideo video,
            string format,
            string filePath,
            bool AutoRename,
            List<string> shazamapikeys,
            List<string> vagalumeapikeys,
            CancellationToken cancellationToken = default)
        {
            if (Formats.VideoFormats.Contains(format))
            {
                return await InjectVideoTagsAsync(video, filePath, cancellationToken);
            }
            else if (Formats.MusicFormats.Contains(format))
            {
                return await InjectAudioTagsAsync(video, filePath, format, AutoRename, shazamapikeys, vagalumeapikeys, cancellationToken);
            }

            return null;
            // Other formats are not supported for tagging
        }

        static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            string formD = text.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();

            foreach (char ch in formD)
            {
                UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        public static bool FileExistsCaseSensitive(string filename)
        {
            string name = Path.GetDirectoryName(filename);

            return name != null
                   && Array.Exists(Directory.GetFiles(name), s => s == Path.GetFullPath(filename));
        }
    }

    public class TaggingSuccesfull
    {
        public string FileName { get; set; }
        public bool Succesfull { get; set; }
    }
}