﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Gress;
using YoutubeDownloader.Models;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.ViewModels.Dialogs;
using YoutubeDownloader.ViewModels.Framework;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.ViewModels.Components
{
    public class DownloadViewModel : PropertyChangedBase
    {
        private readonly IViewModelFactory _viewModelFactory;
        private readonly DialogManager _dialogManager;
        private readonly SettingsService _settingsService;
        private readonly DownloadService _downloadService;
        private readonly TaggingService _taggingService;

        private CancellationTokenSource _cancellationTokenSource;

        public IVideo Video { get; set; } = default!;

        public string FilePath { get; set; } = default!;

        public string FileName => Path.GetFileName(FilePath).Replace("." + Format,"");

        public string Format { get; set; } = default!;

        public VideoQualityPreference QualityPreference { get; set; } = VideoQualityPreference.Maximum;

        public VideoDownloadOption VideoOption { get; set; }

        public SubtitleDownloadOption SubtitleOption { get; set; }

        public IProgressManager ProgressManager { get; set; }

        public IProgressOperation ProgressOperation { get; private set; }

        public bool IsActive { get; private set; }

        public bool IsSuccessful { get; private set; }

        public bool IsCanceled { get; private set; }

        public bool IsFailed { get; private set; }

        public string FailReason { get; private set; }

        public bool TaggingSuccesfull { get; private set; }

        public DownloadViewModel(
            IViewModelFactory viewModelFactory,
            DialogManager dialogManager,
            SettingsService settingsService,
            DownloadService downloadService,
            TaggingService taggingService)
        {
            _viewModelFactory = viewModelFactory;
            _dialogManager = dialogManager;
            _settingsService = settingsService;
            _downloadService = downloadService;
            _taggingService = taggingService;
         }

        public bool CanStart => !IsActive;

        public void Start()
        {
            if (!CanStart)
                return;

            IsActive = true;
            IsSuccessful = false;
            IsCanceled = false;
            IsFailed = false;

            
            RootViewModel.Queue.Enqueue(async () =>
            {
                _cancellationTokenSource = new CancellationTokenSource();
                ProgressOperation = ProgressManager?.CreateOperation();

                try
                {
                    // If download option is not set - get the best download option
                    VideoOption ??= await _downloadService.TryGetBestVideoDownloadOptionAsync(
                        Video.Id,
                        Format,
                        QualityPreference
                    );

                    // It's possible that video has no streams
                    if (VideoOption is null)
                        throw new InvalidOperationException($"Video '{Video.Id}' contains no streams.");

                    await _downloadService.DownloadAsync(
                        VideoOption,
                        SubtitleOption,
                        FilePath,
                        ProgressOperation,
                        _cancellationTokenSource.Token
                    );

                    if (_settingsService.ShouldInjectTags)
                    {
                        List<string> shazamapikeys = _settingsService.FastAPIShazamKeys?.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();
                        List<string> vagalumeapikeys = _settingsService.VagalumeAPIKeys?.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();

                        var taggingsuccesfull = await _taggingService.InjectTagsAsync(
                            Video,
                            Format,
                            FilePath,
                            _settingsService.AutoRenameFile,
                            shazamapikeys,
                            vagalumeapikeys,
                            _cancellationTokenSource.Token
                        );
                        TaggingSuccesfull = taggingsuccesfull.Succesfull;

                        if (_settingsService.ManualCheckTags && Formats.MusicFormats.Contains(Format))
                        {
                            while (RootViewModel.IsBusy)
                            {
                                await Task.Delay(25);
                            }
                            var dialog = _viewModelFactory.CreateConfirmTagsViewModel(
                                _settingsService
                                , _taggingService
                                , Video
                                , Format
                                , taggingsuccesfull.FileName
                                , _cancellationTokenSource
                                );
                            await _dialogManager.ShowDialogAsync(dialog);
                            
                            TaggingSuccesfull = dialog.tagsuccesfull;
                            taggingsuccesfull.FileName = dialog.FilePath;
                        }

                        this.FilePath = taggingsuccesfull.FileName;
                    }

                    IsSuccessful = true;
                }
                catch (OperationCanceledException)
                {
                    IsCanceled = true;
                }
                catch (Exception ex)
                {
                    IsFailed = true;

                    // Short error message for expected errors, full for unexpected
                    FailReason = ex is YoutubeExplodeException
                        ? ex.Message
                        : ex.ToString();

                    Debug.WriteLine($"{DateTime.Now} {FileName} {Environment.NewLine}{FailReason}{Environment.NewLine}{ex.StackTrace}");

                }
                finally
                {
                    IsActive = false;
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                    ProgressOperation?.Dispose();

                    if ((IsFailed || IsCanceled) && File.Exists(FilePath))
                        File.Delete(FilePath);
                }
            });
        }

        public bool CanCancel => IsActive && !IsCanceled;

        public void Cancel()
        {
            if (!CanCancel)
                return;

            _cancellationTokenSource?.Cancel();
        }

        public bool CanShowFile => IsSuccessful;

        public async void ShowFile()
        {
            if (!CanShowFile)
                return;

            try
            {
                // Open explorer, navigate to the output directory and select the video file
                Process.Start("explorer", $"/select, \"{FilePath}\"");
            }
            catch (Exception ex)
            {
                var dialog = _viewModelFactory.CreateMessageBoxViewModel("Error", ex.Message);
                await _dialogManager.ShowDialogAsync(dialog);
            }
        }

        public async void ReTag()
        {
            if (!CanShowFile)
                return;

            try
            {
                while (RootViewModel.IsBusy)
                {
                    await Task.Delay(25);
                }

                var dialog = _viewModelFactory.CreateConfirmTagsViewModel(
                    _settingsService
                    , _taggingService
                    , Video
                    , Format
                    , this.FilePath
                    , _cancellationTokenSource
                    );

                await _dialogManager.ShowDialogAsync(dialog);

                this.FilePath = dialog.FilePath;
                this.TaggingSuccesfull = dialog.tagsuccesfull;
            }
            catch (Exception ex)
            {
                var dialog = _viewModelFactory.CreateMessageBoxViewModel("Error", ex.Message);
                await _dialogManager.ShowDialogAsync(dialog);
            }
        }

        public bool CanOpenFile => IsSuccessful;

        public async void OpenFile()
        {
            if (!CanOpenFile)
                return;

            try
            {
                ProcessEx.StartShellExecute(FilePath);
            }
            catch (Exception ex)
            {
                var dialog = _viewModelFactory.CreateMessageBoxViewModel("Error", ex.Message);
                await _dialogManager.ShowDialogAsync(dialog);
            }
        }

        public bool CanRestart => CanStart && !IsSuccessful;
        public void Restart() => Start();
        public bool CanReTagUnSuccesfull => CanOpenFile && !TaggingSuccesfull && Formats.MusicFormats.Contains(Format);
        public bool CanReTagSuccesfull => CanOpenFile && TaggingSuccesfull && Formats.MusicFormats.Contains(Format);
    }

    public static class DownloadViewModelExtensions
    {
        public static DownloadViewModel CreateDownloadViewModel(
            this IViewModelFactory factory,
            IVideo video,
            string filePath,
            string format,
            VideoDownloadOption videoOption,
            SubtitleDownloadOption subtitleOption)
        {
            var viewModel = factory.CreateDownloadViewModel();

            viewModel.Video = video;
            viewModel.FilePath = filePath;
            viewModel.Format = format;
            viewModel.VideoOption = videoOption;
            viewModel.SubtitleOption = subtitleOption;

            return viewModel;
        }

        public static DownloadViewModel CreateDownloadViewModel(
            this IViewModelFactory factory,
            IVideo video,
            string filePath,
            string format,
            VideoQualityPreference qualityPreference)
        {
            var viewModel = factory.CreateDownloadViewModel();

            viewModel.Video = video;
            viewModel.FilePath = filePath;
            viewModel.Format = format;
            viewModel.QualityPreference = qualityPreference;

            return viewModel;
        }
    }

    public class TaskQueue
    {
        private SemaphoreSlim semaphore;
        public TaskQueue(int simultanioustasks)
        {
            semaphore = new SemaphoreSlim(simultanioustasks);
        }

        public async Task<T> Enqueue<T>(Func<Task<T>> taskGenerator)
        {
            await semaphore.WaitAsync();
            try
            {
                return await taskGenerator();
            }
            finally
            {
                semaphore.Release();
            }
        }
        public async Task Enqueue(Func<Task> taskGenerator)
        {
            await semaphore.WaitAsync();
            try
            {
                await taskGenerator();
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
