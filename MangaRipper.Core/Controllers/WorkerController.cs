﻿using MangaRipper.Core.DataTypes;
using MangaRipper.Core.Helpers;
using MangaRipper.Core.Models;
using MangaRipper.Core.Providers;
using MangaRipper.Core.Services;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MangaRipper.Core.Controllers
{
    /// <summary>
    /// Worker support download manga chapters on back ground thread.
    /// </summary>
    public class WorkerController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        CancellationTokenSource _source;
        readonly SemaphoreSlim _sema;

        public WorkerController()
        {
            Logger.Info("> Worker()");
            _source = new CancellationTokenSource();
            _sema = new SemaphoreSlim(2);
        }

        /// <summary>
        /// Stop download
        /// </summary>
        public void Cancel()
        {
            _source.Cancel();
        }

        /// <summary>
        /// Run task download a chapter
        /// </summary>
        /// <param name="task">Contain chapter and save to path</param>
        /// <param name="progress">Callback to report progress</param>
        /// <returns></returns>
        public async Task Run(DownloadChapterTask task, IProgress<int> progress)
        {
            Logger.Info("> DownloadChapter: {0} To: {1}", task.Chapter.Url, task.SaveToFolder);
            await Task.Run(async () =>
            {
                try
                {
                    _source = new CancellationTokenSource();
                    await _sema.WaitAsync();
                    task.IsBusy = true;
                    await DownloadChapterInternal(task.Chapter, task.SaveToFolder, progress);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to download chapter: {0}", task.Chapter.Url);
                    throw;
                }
                finally
                {
                    task.IsBusy = false;
                    if (task.Formats.Contains(OutputFormat.CBZ))
                    {
                        PackageCbzHelper.Create(Path.Combine(task.SaveToFolder, task.Chapter.NomalizeName), Path.Combine(task.SaveToFolder, task.Chapter.NomalizeName + ".cbz"));
                    }
                    _sema.Release();
                }
            });
        }



        /// <summary>
        /// Find all chapters of a manga
        /// </summary>
        /// <param name="mangaPath">The URL of manga</param>
        /// <param name="progress">Progress report callback</param>
        /// <returns></returns>
        public async Task<IEnumerable<Chapter>> FindChapters(string mangaPath, IProgress<int> progress)
        {
            Logger.Info("> FindChapters: {0}", mangaPath);
            return await Task.Run(async () =>
            {
                try
                {
                    await _sema.WaitAsync();
                    return await FindChaptersInternal(mangaPath, progress);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to find chapters: {0}", mangaPath);
                    throw;
                }
                finally
                {
                    _sema.Release();
                }
            });
        }

        private async Task DownloadChapterInternal(Chapter chapter, string mangaLocalPath, IProgress<int> progress)
        {
            progress.Report(0);
            // let service find all images of chapter
            var service = FrameworkProvider.GetService(chapter.Url);
            var images = await service.FindImanges(chapter, new Progress<int>(count =>
            {
                progress.Report(count / 2);
            }), _source.Token);
            // create folder to keep images
            var downloader = new DownloadService();
            var folderName = chapter.NomalizeName;
            var destinationPath = Path.Combine(mangaLocalPath, folderName);
            Directory.CreateDirectory(destinationPath);
            // download images
            var countImage = 0;
            foreach (var image in images)
            {
                string tempFilePath = Path.GetTempFileName();
                string filePath = Path.Combine(destinationPath, GetFilenameFromUrl(image));
                if (!File.Exists(filePath))
                {
                    await downloader.DownloadFileAsync(image, tempFilePath, _source.Token);
                    File.Move(tempFilePath, filePath);
                }
                countImage++;
                int i = Convert.ToInt32((float)countImage / images.Count() * 100 / 2);
                progress.Report(50 + i);
            }
            progress.Report(100);
        }

        private string GetFilenameFromUrl(string url)
        {
            var uri = new Uri(url);
            return Path.GetFileName(uri.LocalPath);
        }


        private async Task<IEnumerable<Chapter>> FindChaptersInternal(string mangaPath, IProgress<int> progress)
        {
            progress.Report(0);
            // let service find all chapters in manga
            var service = FrameworkProvider.GetService(mangaPath);
            var chapters = await service.FindChapters(mangaPath, progress, _source.Token);
            progress.Report(100);
            return chapters;
        }
    }
}