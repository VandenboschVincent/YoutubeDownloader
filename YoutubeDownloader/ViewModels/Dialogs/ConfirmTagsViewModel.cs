using System.Collections.Generic;
using System.Linq;
using Tyrrrz.Extensions;
using YoutubeDownloader.Services;
using YoutubeDownloader.ViewModels.Framework;
using TagLib;
using TagLib.Mpeg4;
using File = TagLib.File;
using System.Windows.Media.Imaging;
using System.IO;
using System;
using YoutubeExplode.Videos;
using System.Threading;

namespace YoutubeDownloader.ViewModels.Dialogs
{
    public static class ConfirmTagsViewModelExtensions
    {
        public static ConfirmTagsViewModel CreateConfirmTagsViewModel(
            this IViewModelFactory factory,
            SettingsService settingsService,
            TaggingService taggingService,
            IVideo video,
            string format,
            string FilePath,
            CancellationTokenSource cancellationToken = default)
        {
            var viewModel = factory.CreateConfirmTagsViewModel();
            viewModel.FilePath = FilePath;
            viewModel.settingsService = settingsService;
            viewModel.taggingService = taggingService;
            viewModel.cancellationToken = cancellationToken;
            viewModel.video = video;
            viewModel.format = format;
            viewModel.GetFileInfo();
            return viewModel;
        }
    }
    public class ConfirmTagsViewModel : DialogScreen
    {
        public ConfirmTagsViewModel()
        {
        }

        public string FilePath;
        public SettingsService settingsService;
        public TaggingService taggingService;
        public CancellationTokenSource cancellationToken;
        public IVideo video;
        public string format;
        public bool tagsuccesfull = false;

        private string originalname;
        public string OriginalName { get => originalname; set => SetAndNotify(ref originalname, value); }

        private string songName;
        public string SongName { get => songName; set => SetAndNotify(ref songName, value); }

        private string artistName;
        public string ArtistName { get => artistName; set => SetAndNotify(ref artistName, value); }

        private Dictionary<string, string> mydictionary;
        public Dictionary<string, string> MyDictionary { get => mydictionary; set => SetAndNotify(ref mydictionary, value); }

        public BitmapImage SongImage { get => songimage; 
            set => SetAndNotify(ref songimage, value); }
        private BitmapImage songimage;

        public async void ResetTagging()
        {
            FilePath = await taggingService.ResetTagging(
                video,
                FilePath,
                format,
                cancellationToken?.Token ?? new CancellationTokenSource().Token
            );

            GetFileInfo();
        }

        public async void RefreshOnline()
        {
            List<string> shazamapikeys = settingsService.FastAPIShazamKeys?.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();
            List<string> vagalumeapikeys = settingsService.VagalumeAPIKeys?.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();

            var tagging = await taggingService.InjectAudioTagsAsync(
                video,
                FilePath,
                format,
                settingsService.AutoRenameFile,
                shazamapikeys,
                vagalumeapikeys,
                cancellationToken?.Token ?? new CancellationTokenSource().Token,
                string.IsNullOrWhiteSpace(artistName) ? null : songName,
                string.IsNullOrWhiteSpace(artistName) ? null : artistName
            );
            tagsuccesfull = tagging.Succesfull;
            FilePath = tagging.FileName;
            GetFileInfo();
        }

        public void GetFileInfo()
        {
            var file = File.Create(FilePath);
            var fileinfo = new FileInfo(file.Name);

            OriginalName = video.Title + Environment.NewLine + video.Author.Title;
            SongName = file.Tag.Title ?? fileinfo.Name.Replace(fileinfo.Extension,"");
            ArtistName = file.Tag.Performers?.JoinToString(", ");

            if (!string.IsNullOrWhiteSpace(file.Tag.Title))
                tagsuccesfull = true;
            else 
                tagsuccesfull = false;

            ExtractImage(file);
            var newdictionary = new Dictionary<string, string>()
            {
                { "Album", file.Tag.Album }
                , { "BeatsPerMinute", file.Tag.BeatsPerMinute.ToString() }
                , { "Genres",file.Tag.Genres.JoinToString(", ") }
                , { "Year" , file.Tag.Year == 0 ? "" : file.Tag.Year.ToString() }
                , { "Length", file.Properties.Duration.ToString() }
                , { "AudioBitrate", file.Properties.AudioBitrate.ToString() }
                , { "AudioSampleRate", file.Properties.AudioSampleRate.ToString() }
                , { "Description", file.Tag.Description }
                , { "Performers", file.Tag.Performers?.JoinToString(", ") }
            };
            MyDictionary = newdictionary;
        }

        public void ExtractImage(File file)
        {
            // Load you image data in MemoryStream
            if (file.Tag.Pictures.Any())
            {
                TagLib.IPicture pic = file.Tag.Pictures[0];
                MemoryStream ms = new MemoryStream(pic.Data.Data);
                ms.Seek(0, SeekOrigin.Begin);

                // ImageSource for System.Windows.Controls.Image
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.EndInit();

                SongImage = bitmap;
            }
        }
    }
}