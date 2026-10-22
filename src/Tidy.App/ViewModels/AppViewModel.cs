using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Tidy.App.Services;
using Tidy.Core.Apps;
using Tidy.Core.Models;

namespace Tidy.App.ViewModels;

public sealed partial class AppViewModel(InstalledApp app) : ObservableObject
{
    public const int UnusedDays = 90;

    public InstalledApp App { get; } = app;

    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial ImageSource? Icon { get; private set; }

    public string Name => App.Name;
    public long SizeBytes => App.SizeBytes;
    public string SizeText => App.SizeBytes > 0 ? Format.Bytes(App.SizeBytes) : "";
    public bool IsSafe => App.Safety == SafetyLevel.Safe;
    public bool IsCheckFirst => App.Safety == SafetyLevel.CheckFirst;
    public bool IsPreinstalled => App.IsPreinstalled;
    public bool IsUnused => App.IsUnusedFor(UnusedDays, DateTime.UtcNow);
    public bool HasExplanation => App.Preinstalled is not null;
    public string Explanation => App.Preinstalled?.Explanation ?? "";
    public bool HasNoIcon => Icon is null;
    public string KindText => App.Kind == AppKind.Store ? "Store app" : "Desktop app";

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (App.Publisher.Length > 0) parts.Add(App.Publisher);
            parts.Add(KindText);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>"Open now", "Last opened 8 months ago", or "No launch recorded".</summary>
    public string UsageText
    {
        get
        {
            if (App.IsRunning) return "Open now";
            if (App.LastUsedUtc is { } used) return "Last opened " + Format.Age(used, DateTime.UtcNow);
            return "No launch recorded";
        }
    }

    public DateTime SortableLastUsed => App.IsRunning ? DateTime.MaxValue : App.LastUsedUtc ?? DateTime.MinValue;

    private bool _iconRequested;

    /// <summary>Loads the app's own icon the first time its row is shown.</summary>
    public async void LoadIcon()
    {
        if (_iconRequested || App.IconPath is not { } path) return;
        _iconRequested = true;
        Icon = await ShellIcons.ImageAsync(path, 64);
        OnPropertyChanged(nameof(HasNoIcon));
    }
}
