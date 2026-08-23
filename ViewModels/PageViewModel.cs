using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NightOwls.Helpers;

namespace NightOwls.ViewModels;

public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    protected readonly Action<MovieCardViewModel> PlayCallback;
    protected readonly Func<MovieCardViewModel, bool>? FavoriteToggleHandler;

    protected PageViewModel(Action<MovieCardViewModel> playCallback)
    {
        PlayCallback = playCallback;
    }

    public abstract void RefreshFromMaster(List<MovieCardViewModel> master);

    [RelayCommand]
    private void Play(MovieCardViewModel? card)
    {
        if (card is not null) PlayCallback(card);
    }

    [RelayCommand]
    private void ToggleFavorite(MovieCardViewModel? card)
    {
        if (card is not null) FavoriteToggleRequested?.Invoke(card);
    }

    public event Action<MovieCardViewModel>? FavoriteToggleRequested;

    protected static ObservableCollection<MovieCardViewModel> ToCollection(IEnumerable<MovieCardViewModel> items) =>
        new(items);
}
