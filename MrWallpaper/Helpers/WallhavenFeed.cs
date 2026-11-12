using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Data;
using MrWallpaper.Models;
using MrWallpaper.Services;
using Windows.Foundation;

namespace MrWallpaper.Helpers;

/// <summary>Search results that load the next page as the grid scrolls (infinite scroll).</summary>
public sealed partial class WallhavenFeed : ObservableCollection<WhWallpaper>, ISupportIncrementalLoading
{
    private readonly SearchQuery _query;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _seen = [];
    private int _page;
    private int _lastPage = int.MaxValue;
    private string? _seed;
    private bool _busy;

    public WallhavenFeed(SearchQuery query) => _query = query;

    public int Total { get; private set; } = -1;
    public bool IsLoading => _busy;
    public string? Error { get; private set; }

    /// <summary>Raised when loading starts/stops, an error occurs, or the total is known.</summary>
    public event EventHandler? StateChanged;

    public bool HasMoreItems => Error is null && !_cts.IsCancellationRequested && _page < _lastPage;

    public void Cancel() => _cts.Cancel();

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count) =>
        AsyncInfo.Run(async _ =>
        {
            if (_busy || !HasMoreItems) return new LoadMoreItemsResult { Count = 0 };
            _busy = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            uint added = 0;
            try
            {
                var response = await WallhavenClient.SearchAsync(_query, _page + 1, _seed, _cts.Token);
                _page = response.Meta.CurrentPage > 0 ? response.Meta.CurrentPage : _page + 1;
                _lastPage = Math.Max(response.Meta.LastPage, 1);
                _seed ??= response.Meta.Seed;
                Total = response.Meta.Total;
                if (response.Data.Count == 0) _lastPage = _page;

                foreach (var w in response.Data)
                {
                    if (!_seen.Add(w.Id)) continue; // new uploads can shift pages
                    Add(w);
                    added++;
                }
            }
            catch (OperationCanceledException) { }
            catch (WallhavenException ex) { Error = ex.Message; }
            catch (HttpRequestException) { Error = "Couldn't reach Wallhaven. Check your internet connection."; }
            catch (Exception ex) { Error = "Something went wrong: " + ex.Message; }
            finally
            {
                _busy = false;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            return new LoadMoreItemsResult { Count = added };
        });

    /// <summary>Clears an error so the grid tries again.</summary>
    public void Retry()
    {
        Error = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
