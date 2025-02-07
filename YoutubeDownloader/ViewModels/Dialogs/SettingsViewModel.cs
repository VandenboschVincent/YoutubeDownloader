﻿using System.Collections.Generic;
using System.Linq;
using Tyrrrz.Extensions;
using YoutubeDownloader.Services;
using YoutubeDownloader.ViewModels.Framework;

namespace YoutubeDownloader.ViewModels.Dialogs
{
    public class SettingsViewModel : DialogScreen
    {
        private readonly SettingsService _settingsService;

        public bool IsAutoUpdateEnabled
        {
            get => _settingsService.IsAutoUpdateEnabled;
            set => _settingsService.IsAutoUpdateEnabled = value;
        }

        public bool IsDarkModeEnabled
        {
            get => _settingsService.IsDarkModeEnabled;
            set => _settingsService.IsDarkModeEnabled = value;
        }

        public bool ShouldInjectTags
        {
            get => _settingsService.ShouldInjectTags;
            set => _settingsService.ShouldInjectTags = value;
        }

        public bool ManualCheckTags
        {
            get => _settingsService.ManualCheckTags;
            set => _settingsService.ManualCheckTags = value;
        }

        public bool AutoRenameFile
        {
            get => _settingsService.AutoRenameFile;
            set => _settingsService.AutoRenameFile = value;
        }

        public bool ShouldSkipExistingFiles
        {
            get => _settingsService.ShouldSkipExistingFiles;
            set => _settingsService.ShouldSkipExistingFiles = value;
        }

        public string FileNameTemplate
        {
            get => _settingsService.FileNameTemplate;
            set => _settingsService.FileNameTemplate = value;
        }

        public string FastAPIShazamKeys
        {
            get => _settingsService.FastAPIShazamKeys;
            set => _settingsService.FastAPIShazamKeys = value;
        }

        public string VagalumeAPIKeys
        {
            get => _settingsService.VagalumeAPIKeys;
            set => _settingsService.VagalumeAPIKeys = value;
        }

        public string ExcludedContainerFormats
        {
            get => _settingsService.ExcludedContainerFormats is not null
                ? _settingsService.ExcludedContainerFormats.JoinToString(",")
                : "";

            set => _settingsService.ExcludedContainerFormats = value
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        public int MaxConcurrentDownloads
        {
            get => _settingsService.MaxConcurrentDownloadCount;
            set => _settingsService.MaxConcurrentDownloadCount = value.Clamp(1, 10);
        }
        public int ItemCount
        {
            get => _settingsService.ItemCount;
            set => _settingsService.ItemCount = value;
        }

        public SettingsViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }
    }
}