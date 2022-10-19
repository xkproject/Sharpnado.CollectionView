﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Sharpnado.CollectionView.Services;
using Sharpnado.Tasks;
using Xamarin.Forms;

namespace Sharpnado.CollectionView.Paging
{
    /// <summary>
    /// Component responsible for manually loading the pages of a service.
    /// It also bears the responsibility of loading the next page when scrolling the infinite list.
    /// This automatic loading feature is exposed by the IInfiniteListLoader interface.
    /// And yes: it has 2 responsibilities, which goes against SRP, but it makes the component very efficient, simple and usable.
    /// So *** SRP (well not generally speaking, but for this one time).
    /// </summary>
    public class Paginator<TResult> : IInfiniteListLoader, IDisposable
    {
        private const float LoadingThresholdDefault = 1f / 4;
        private const int PageSizeDefault = 10;
        private const int MaxItemCountDefault = 200;

        private readonly object _syncRoot = new object();
        private readonly int _maxItemCount;
        private readonly Func<int, int, bool, Guid, Task<PageResult<TResult>>> _pageSourceLoader;
        private readonly Action<ITaskMonitor> _onTaskCompleted;
        private readonly float _loadingThreshold;

        private bool _isDisposed;
        private List<TResult> _items;
        private bool _refreshRequested;
        private SemaphoreSlim _loadPageSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// The paginator is a concrete component, it is usable directly by instantiation (please don't abuse DI :).
        /// </summary>
        /// <param name="pageSize">The page size for the data.</param>
        /// <param name="maxItemCount">The maximum number of elements that the paginator can load.</param>
        /// <param name="pageSourceLoader">
        /// The func that will return the data. Here you have two options:
        /// 1. The func calls the REST (or whatever) service, build ViewModels from the Models, then add them to collection the ObservableCollection.
        /// The onTaskCompleted callback will be optional.
        /// 2. The func still calls the domain service, but just returns the PageResult of Models.
        /// The onTaskCompleted will create your ViewModels and add them to the ObservableCollection.
        /// The three parameters of the Func are pageNumber, pageSize and isRefreshed.
        /// </param>
        /// <param name="onTaskCompleted">
        /// This callback is called at the end of each page loading, successful or not.
        /// This is where you want to assign the retrieved items to your ObservableCollection.
        /// </param>
        /// <param name="loadingThreshold">
        /// The list threshold from where the next page loading will be triggered (magic will occur in the OnScroll method of the IInfiniteListLoader interface)
        /// This threshold stands for a percentage of the last page:
        /// Let's say you have 40 items loaded in your List and page size is 10, if the threshold is 0.5,
        /// the loading of the next page will be triggered when element 35 will become visible.
        /// Default is 0.25. Requires loadingThreshold in [0,1].
        /// </param>
        public Paginator(
            Func<int, int, bool, Guid, Task<PageResult<TResult>>> pageSourceLoader,
            Action<ITaskMonitor> onTaskCompleted = null,
            int pageSize = PageSizeDefault,
            int maxItemCount = MaxItemCountDefault,
            float loadingThreshold = LoadingThresholdDefault)
        {
            Contract.Requires(() => pageSize > 0);
            Contract.Requires(() => maxItemCount > 0);
            Contract.Requires(() => loadingThreshold >= 0 && loadingThreshold <= 1);

            InternalLogger.Info($"Building paginator with pageSize: {pageSize}, maxItemCount: {maxItemCount}, loadingThreshold: {loadingThreshold}");

            _maxItemCount = maxItemCount;
            _pageSourceLoader = pageSourceLoader;
            _onTaskCompleted = onTaskCompleted;
            _loadingThreshold = loadingThreshold;

            PageSize = pageSize;
            TotalCount = _maxItemCount;

            Reset();
        }

        public TaskMonitor<PageResult<TResult>> LoadingTask { get; private set; }

        /// <summary>
        /// Number of pages successfully loaded.
        /// </summary>
        public int PageLoadedCount { get; private set; }

        /// <summary>
        /// Number of items successfully loaded.
        /// </summary>
        public int LoadedCount => Items.Count;

        public bool IsFull => LoadedCount >= TotalCount;

        public int PageSize { get; }

        public int TotalCount { get; private set; }

        public int TotalRemoteCount { get; private set; }

        public bool HasStarted => LoadingTask != null;

        public bool IsLoadingSuccessfull => LoadingTask.IsSuccessfullyCompleted;

        /// <summary>
        /// True if the user requested a refresh of the list.
        /// </summary>
        public bool HasRefreshed
        {
            get
            {
                lock (_syncRoot)
                {
                    return _refreshRequested;
                }
            }
        }

        public IReadOnlyList<TResult> Items => _items;

        /// <summary>
        /// Last page returned by the data source.
        /// </summary>
        public PageResult<TResult> LastResult { get; private set; }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        /// <summary>
        /// This method must be called by the UI element in charge of displaying data.
        /// Per example, on android, a scroll listener can reference this paginator as an IInfiniteListLoader and call it from OnScroll.
        /// The call to this method is nearly transparent as it returns immediately and doesn't block the caller.
        /// (benchmarked as 4 ticks for a call (10 000 ticks == 1ms)).
        /// </summary>
        public void OnScroll(int lastVisibleIndex)
        {
            var guid = Guid.NewGuid();
            System.Diagnostics.Debug.WriteLine($"{guid} - OnScroll Start: lastVisibleIndex={lastVisibleIndex}.");

            TaskMonitor<bool>.Create(
                async () =>
                {
                    await _loadPageSemaphore.WaitAsync();
                    return await ShouldLoadNextPage(lastVisibleIndex, guid);
                },
                whenSuccessfullyCompleted: (task, shouldLoad) =>
                {
                    System.Diagnostics.Debug.WriteLine($"{guid} - OnScroll whenSuccessfullyCompleted: lastVisibleIndex={lastVisibleIndex}.");
                    if (shouldLoad)
                    {
                        InternalLogger.Info($"{guid} - Scrolled: loading more (max index of visible item {lastVisibleIndex})");
                        int pageToLoad = lastVisibleIndex / PageSize + 2;
                        LoadPage(pageToLoad, calledFromScroll: true, guid);
                    }

                    _loadPageSemaphore.Release();
                });
            System.Diagnostics.Debug.WriteLine($"{guid} - OnScroll Finishes: lastVisibleIndex={lastVisibleIndex}.");
        }

        /// <summary>
        /// Launch the loading of a data page.
        /// If a task is currently running, it gets discarded (callbacks won't be called).
        /// If the first page loading is asked whereas one or several pages have already been loaded, a "refresh" is detected.
        /// </summary>
        /// <param name="pageNumber">The page number to load (pageNumber = 1 for the first page).</param>
        /// <param name="calledFromScroll">True if LoadPage has been called from OnScroll method of the IInfiniteListLoader.</param>
        public Task<PageResult<TResult>> LoadPage(int pageNumber, bool calledFromScroll, Guid guid)
        {
            System.Diagnostics.Debug.WriteLine($"{guid} - LoadPage: pageNumber={pageNumber}, calledFromScroll={calledFromScroll}.");
            Contract.Requires(() => pageNumber > 0);
            Contract.Requires(
                () => calledFromScroll || (pageNumber == 1 || pageNumber == (PageLoadedCount + 1)),
                "The paginator can only load sequential pages");

            InternalLogger.Info($"Requesting page n°{pageNumber} load, {PageLoadedCount} pages loaded so far");
            lock (_syncRoot)
            {
                if (calledFromScroll)
                {
                    if (pageNumber <= PageLoadedCount)
                    {
                        InternalLogger.Info($"Aborting IInfiniteListLoader call: only a direct call to LoadPage can lead to a refresh");
                        return Task.FromResult(PageResult<TResult>.Empty);
                    }
                }

                if (pageNumber > PageLoadedCount && IsFull)
                {
                    InternalLogger.Info($"Cannot load page {pageNumber} total item count has already been reached ({TotalCount})");
                    return Task.FromResult(PageResult<TResult>.Empty);
                }

                if (pageNumber == 1 && PageLoadedCount > 0)
                {
                    InternalLogger.Info("Refresh detected");
                    _refreshRequested = true;
                }
                else
                {
                    _refreshRequested = false;
                }

                if (LoadingTask != null && LoadingTask.IsNotCompleted)
                {
                    // Cancels callbacks of previous task if not completed
                    LoadingTask.CancelCallbacks();
                }

                System.Diagnostics.Debug.WriteLine($"{guid} - LoadPage Before LoadingTask: pageNumber={pageNumber}, calledFromScroll={calledFromScroll}.");
                LoadingTask = new TaskMonitor<PageResult<TResult>>.Builder(
                    () => _pageSourceLoader(pageNumber, PageSize, _refreshRequested, guid))
                        .WithWhenSuccessfullyCompleted((a, b) => OnPageRetrieved2(a, b, pageNumber, PageSize, _refreshRequested, guid))
                        .WithWhenCompleted((a) => OnTaskCompleted2(a, pageNumber, PageSize, _refreshRequested, guid))
                        .Build();

                InternalLogger.Info($"Page n°{pageNumber} loading started");
                System.Diagnostics.Debug.WriteLine($"{guid} - LoadPage Before LoadingTask.Start: pageNumber={pageNumber}, calledFromScroll={calledFromScroll}.");
                LoadingTask.Start();
                System.Diagnostics.Debug.WriteLine($"{guid} - LoadPage After LoadingTask.Start: pageNumber={pageNumber}, calledFromScroll={calledFromScroll}.");
                return (Task<PageResult<TResult>>)LoadingTask.Task;
            }
        }

        private void OnPageRetrieved2(ITaskMonitor task, PageResult<TResult> result, int pageNumber, int pageSize, bool isRefreshing, Guid guid)
        {
            System.Diagnostics.Debug.WriteLine($"{guid} - OnPageRetrieved2: pageNumber={pageNumber}, pageSize={pageSize}, isRefreshing={isRefreshing}.");
            Contract.Requires(() => task != null);

            InternalLogger.Info("On page retrieved callback");

            if (_isDisposed)
            {
                return;
            }

            lock (_syncRoot)
            {
                LastResult = result;

                InternalLogger.Info($"{result.Items.Count} items retrieved, total remote items is {result.TotalCount}");
                if (_refreshRequested)
                {
                    Reset();
                }

                TotalRemoteCount = result.TotalCount;
                TotalCount = Math.Min(result.TotalCount, _maxItemCount);
                PageLoadedCount++;
            }

            _items.AddRange(result.Items);
            InternalLogger.Info($"{Items.Count} items in paginator collection, {PageLoadedCount} pages loaded");

            Contract.Ensures(() => PageLoadedCount > 0);
            Contract.Ensures(() => result.Items != null && _maxItemCount >= 0);
        }

        private void OnTaskCompleted2(ITaskMonitor task, int pageNumber, int pageSize, bool isRefreshing, Guid guid)
        {
            System.Diagnostics.Debug.WriteLine($"{guid} - OnTaskCompleted2: pageNumber={pageNumber}, pageSize={pageSize}, isRefreshing={isRefreshing}.");
            InternalLogger.Info($"OnTaskCompleted( taskStatus: {task.Status} )");
            if (_isDisposed)
            {
                return;
            }

            _onTaskCompleted?.Invoke(task);
        }

        private void OnPageRetrieved(ITaskMonitor task, PageResult<TResult> result)
        {
            Contract.Requires(() => task != null);

            InternalLogger.Info("On page retrieved callback");

            if (_isDisposed)
            {
                return;
            }

            lock (_syncRoot)
            {
                LastResult = result;

                InternalLogger.Info($"{result.Items.Count} items retrieved, total remote items is {result.TotalCount}");
                if (_refreshRequested)
                {
                    Reset();
                }

                TotalRemoteCount = result.TotalCount;
                TotalCount = Math.Min(result.TotalCount, _maxItemCount);
                PageLoadedCount++;
            }

            _items.AddRange(result.Items);
            InternalLogger.Info($"{Items.Count} items in paginator collection, {PageLoadedCount} pages loaded");

            Contract.Ensures(() => PageLoadedCount > 0);
            Contract.Ensures(() => result.Items != null && _maxItemCount >= 0);
        }

        private void OnTaskCompleted(ITaskMonitor task)
        {
            InternalLogger.Info($"OnTaskCompleted( taskStatus: {task.Status} )");
            if (_isDisposed)
            {
                return;
            }

            _onTaskCompleted?.Invoke(task);
        }

        private void Reset()
        {
            InternalLogger.Info("Resetting paginator");
            PageLoadedCount = 0;
            _items = new List<TResult>();
        }

        private Task<bool> ShouldLoadNextPage(int lastVisibleIndex, Guid guid)
        {
            System.Diagnostics.Debug.WriteLine($"{guid} - ShouldLoadNextPage: lastVisibleIndex={lastVisibleIndex}.");
            return Task.Run(() =>
            {
                System.Diagnostics.Debug.WriteLine($"{guid} - ShouldLoadNextPage Run Start: lastVisibleIndex={lastVisibleIndex}.");
                if (lastVisibleIndex < 0)
                {
                    System.Diagnostics.Debug.WriteLine($"{guid} - ShouldLoadNextPage Run lastVisibleIndex: lastVisibleIndex={lastVisibleIndex}.");
                    return false;
                }

                if (PageLoadedCount == 0)
                {
                    // If no pages are loaded, there is nothing to scroll
                    System.Diagnostics.Debug.WriteLine($"{guid} - ShouldLoadNextPage Run PageLoadedCount: lastVisibleIndex={lastVisibleIndex}.");
                    return false;
                }

                if (IsFull)
                {
                    // All messages are already loaded nothing to paginate
                    System.Diagnostics.Debug.WriteLine($"{guid} - ShouldLoadNextPage Run IsFull: lastVisibleIndex={lastVisibleIndex}.");
                    return false;
                }

                if (HasStarted && LoadingTask.IsNotCompleted)
                {
                    // Currently loading page
                    System.Diagnostics.Debug.WriteLine($"{guid} - ShouldLoadNextPage Run HasStarted && LoadingTask.IsNotCompleted: lastVisibleIndex={lastVisibleIndex}.");
                    return false;
                }

                int itemsCount = LoadedCount;
                System.Diagnostics.Debug.WriteLine($"{guid} - ShouldLoadNextPage Run Finishes: lastVisibleIndex={lastVisibleIndex}.");
                return lastVisibleIndex >= (itemsCount - (PageSize * _loadingThreshold));
            });
        }
    }
}
