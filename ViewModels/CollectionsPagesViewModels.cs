using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NightOwls.ViewModels;

public partial class FavoritesViewModel : PageViewModel
{
    public override string Title => "Favorites";

    private List<MovieCardViewModel> _master = new();

    public ObservableCollection<MovieCardViewModel> Favorites { get; } = new();

    [ObservableProperty]
    private bool hasFavorites;

    public string CountText => Favorites.Count == 1 ? "1 favorite" : $"{Favorites.Count} favorites";

    public FavoritesViewModel(Action<MovieCardViewModel> playCallback) : base(playCallback)
    {
    }

    public override void RefreshFromMaster(List<MovieCardViewModel> master)
    {
        _master = master;
        Rebuild();
    }

    public void Rebuild()
    {
        var favs = _master.Where(m => m.IsFavorite).OrderBy(m => m.Model.Title, StringComparer.OrdinalIgnoreCase).ToList();
        Favorites.Clear();
        foreach (var f in favs) Favorites.Add(f);
        HasFavorites = Favorites.Count > 0;
        OnPropertyChanged(nameof(CountText));
    }

    public void NotifyFavoriteChanged(MovieCardViewModel card)
    {
        if (card.IsFavorite && Favorites.All(f => f.Id != card.Id))
            Favorites.Add(card);
        else if (!card.IsFavorite)
        {
            var existing = Favorites.FirstOrDefault(f => f.Id == card.Id);
            if (existing is not null) Favorites.Remove(existing);
        }
        HasFavorites = Favorites.Count > 0;
        OnPropertyChanged(nameof(CountText));
    }
}

public partial class RecentViewModel : PageViewModel
{
    public override string Title => "Recently Watched";

    private List<MovieCardViewModel> _master = new();

    public ObservableCollection<MovieCardViewModel> Recent { get; } = new();

    [ObservableProperty]
    private bool hasRecent;

    public RecentViewModel(Action<MovieCardViewModel> playCallback) : base(playCallback)
    {
    }

    public override void RefreshFromMaster(List<MovieCardViewModel> master)
    {
        _master = master;
        Rebuild();
    }

    public void Rebuild()
    {
        var recents = _master.Where(m => m.IsWatched).OrderByDescending(m => m.LastWatched).Take(200).ToList();
        Recent.Clear();
        foreach (var r in recents) Recent.Add(r);
        HasRecent = Recent.Count > 0;
    }
}
