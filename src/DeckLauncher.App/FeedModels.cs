using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

/// <summary>Base of the cards on the Novidades / Amigos / Recomendados screens: pictures load lazily.</summary>
internal abstract class FeedItem : INotifyPropertyChanged
{
    private ImageSource? _image;
    private ImageSource? _icon;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImageSource? Image => _image;

    public ImageSource? Icon => _icon;

    /// <summary>What happens on click: a steam:// or https:// address.</summary>
    public string? Url { get; protected init; }

    protected async void LoadImage(string? url, int width)
    {
        var image = await RemoteImage.LoadAsync(LibraryService.Http, url, width);
        if (image is null) return;
        _image = image;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
    }

    /// <summary>Tries the Steam store pictures one after another (new games use other file names).</summary>
    protected async void LoadStoreImage(string appId, int width)
    {
        string?[] urls = { SteamFeeds.HeaderUrl(appId), SteamFeeds.CapsuleUrl(appId), SteamFeeds.AkamaiHeaderUrl(appId) };
        ImageSource? image = null;
        foreach (var url in urls)
        {
            image = await RemoteImage.LoadAsync(LibraryService.Http, url, width);
            if (image is not null) break;
        }
        if (image is null)
        {
            var official = await SteamFeeds.StoreHeaderUrlAsync(LibraryService.Http, appId);
            image = await RemoteImage.LoadAsync(LibraryService.Http, official, width);
        }
        if (image is null) return;
        _image = image;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
    }

    protected async void LoadIcon(string? url, int width)
    {
        var image = await RemoteImage.LoadAsync(LibraryService.Http, url, width);
        if (image is null) return;
        _icon = image;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }

    // ---- Shared Portuguese texts --------------------------------------------------------------

    private static readonly CultureInfo Portuguese = new("pt-BR");

    /// <summary>"hoje", "ontem", "há 3 dias" or the date.</summary>
    public static string DayLabel(DateTimeOffset date)
    {
        var days = (DateTime.Now.Date - date.ToLocalTime().Date).Days;
        return days <= 0 ? "hoje"
            : days == 1 ? "ontem"
            : days < 7 ? $"há {days} dias"
            : date.ToLocalTime().ToString("d 'de' MMMM", Portuguese);
    }

    public static string AgoLabel(DateTimeOffset date)
    {
        var days = (DateTime.Now.Date - date.ToLocalTime().Date).Days;
        return days <= 0 ? "hoje"
            : days == 1 ? "ontem"
            : days < 7 ? $"há {days} dias"
            : days < 14 ? "há 1 semana"
            : days < 31 ? $"há {days / 7} semanas"
            : date.ToLocalTime().ToString("d 'de' MMM", Portuguese);
    }
}

/// <summary>An announcement of one of the user's Steam games.</summary>
internal sealed class NewsCard : FeedItem
{
    public NewsCard(SteamFeeds.NewsData data)
    {
        Title = data.Title;
        GameName = data.GameName;
        DateLabel = DayLabel(data.Date);
        Url = data.Url;
        LoadImage(data.ImageUrl, 450);
        LoadIcon(SteamFeeds.HeaderUrl(data.AppId), 64);
    }

    public string Title { get; }
    public string GameName { get; }
    public string DateLabel { get; }
}

/// <summary>An installed Steam game that was updated recently ("Recém-atualizados neste dispositivo").</summary>
internal sealed class UpdatedCard : FeedItem
{
    public UpdatedCard(SteamFeeds.UpdatedData data)
    {
        Name = data.Name;
        Detail = $"{SteamDownloads.FormatBytes(data.Size)}   Atualizado: {AgoLabel(data.Updated)}";
        Url = $"steam://nav/games/details/{data.AppId}";
        LoadStoreImage(data.AppId, 96);
    }

    public string Name { get; }
    public string Detail { get; }
}

/// <summary>A game's store capsule (most played list, release calendar).</summary>
internal sealed class CapsuleCard : FeedItem
{
    public CapsuleCard(string appId, string? name, bool inLibrary)
    {
        Name = name ?? "";
        InLibraryVisibility = inLibrary ? Visibility.Visible : Visibility.Collapsed;
        Url = $"steam://store/{appId}";
        LoadStoreImage(appId, 380);
    }

    public string Name { get; }
    public Visibility InLibraryVisibility { get; }
}

/// <summary>One column of the release calendar.</summary>
internal sealed class CalendarDay
{
    private static readonly string[] Days = { "DOM", "SEG", "TER", "QUA", "QUI", "SEX", "SÁB" };

    public CalendarDay(DateTime date, IReadOnlyList<CapsuleCard> items)
    {
        Header = $"{Days[(int)date.DayOfWeek]}. {date:dd/MM}";
        Items = items;
    }

    public string Header { get; }
    public IReadOnlyList<CapsuleCard> Items { get; }
}

/// <summary>A game that friends are playing right now.</summary>
internal sealed class FriendGameCard : FeedItem
{
    public FriendGameCard(string appId, string gameName, IReadOnlyList<string> friends, bool inLibrary)
    {
        GameName = gameName;
        Count = friends.Count == 1 ? "1 amigo(a) jogando" : $"{friends.Count} amigos jogando";
        Friends = string.Join(", ", friends.Take(3)) + (friends.Count > 3 ? $" e mais {friends.Count - 3}" : "");
        LibraryText = inLibrary ? "Na biblioteca" : "";
        Url = $"steam://store/{appId}";
        LoadImage(SteamFeeds.CoverUrl(appId), 150);
    }

    public string GameName { get; }
    public string Count { get; }
    public string Friends { get; }
    public string LibraryText { get; }
}

/// <summary>A friend's recent play session ("Atividade").</summary>
internal sealed class ActivityCard : FeedItem
{
    public ActivityCard(SteamFeeds.ActivityData data)
    {
        FriendName = data.FriendName;
        Text = " jogou " + data.GameName;
        var hours = data.Minutes / 60.0;
        Detail = hours >= 1 ? $"{hours:0.#} h nas últimas 2 semanas" : $"{data.Minutes} min nas últimas 2 semanas";
        Url = $"steam://store/{data.AppId}";
        LoadStoreImage(data.AppId, 260);
        LoadIcon(data.Avatar, 64);
    }

    public string FriendName { get; }
    public string Text { get; }
    public string Detail { get; }
}

/// <summary>An online friend; clicking opens the chat in the official Steam client (the launcher never chats itself).</summary>
internal sealed class FriendChatCard : FeedItem
{
    public FriendChatCard(SteamFeeds.FriendData friend)
    {
        Name = friend.Name;
        Status = SteamFriends.StatusOf(friend.PersonaState, friend.GameName);
        Url = SteamFriends.ChatUrl(friend.SteamId);
        LoadIcon(friend.Avatar, 64);
    }

    public string Name { get; }
    public string Status { get; }
}

/// <summary>A card that only opens an address (the discovery banner).</summary>
internal sealed class LinkCard : FeedItem
{
    public LinkCard(string url) => Url = url;
}
