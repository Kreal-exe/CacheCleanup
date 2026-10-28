using System.Collections.ObjectModel;
using CacheCleanup.Core;
using CacheCleanup.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CacheCleanup.ViewModels;

public enum TargetState { Idle, Scanning, Done, Denied, Error }

public sealed partial class EntryVm : ObservableObject
{
    public EntryVm(EntrySummary summary, long parentBytes)
    {
        Path = summary.Path;
        Name = summary.Name;
        IsDirectory = summary.IsDirectory;
        Bytes = summary.Bytes;
        Files = summary.Files;
        Share = parentBytes > 0 ? (double)summary.Bytes / parentBytes : 0;
    }

    public string Path { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public long Bytes { get; }
    public int Files { get; }
    public double Share { get; }
    public string Glyph => IsDirectory ? "" : "";
}

public sealed partial class TargetVm : ObservableObject
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    private readonly Action<TargetVm> _onSelectionChanged;

    public TargetVm(CleanupTarget target, bool enabled, Action<TargetVm> onSelectionChanged)
    {
        Target = target;
        _onSelectionChanged = onSelectionChanged;
        Roots = target.Kind == TargetKind.Files ? target.ResolveRoots() : [];
        IsPresent = target.Kind == TargetKind.RecycleBin || Roots.Count > 0;
        _isEnabled = enabled && IsPresent;

        Loc.LanguageChanged += OnLanguageChanged;
    }

    public CleanupTarget Target { get; }
    public string Id => Target.Id;
    public TargetCategory Category => Target.Category;
    public string Glyph => Target.Glyph;
    public RiskLevel Risk => Target.Risk;
    public bool RequiresAdmin => Target.RequiresAdmin;
    public bool ShowAdminBadge => Target.RequiresAdmin && !Elevation.IsElevated;
    public bool IsCustom => Target.Category == TargetCategory.Custom;
    public bool IsPresent { get; }
    public bool IsRecycleBin => Target.Kind == TargetKind.RecycleBin;

    public string Name => Target.DisplayName ?? Loc[Target.NameKey];
    public string Description => IsCustom ? Loc["target.custom.desc"] : Loc[Target.DescriptionKey];
    public string RiskText => Loc["risk." + Risk];
    public string RiskHint => Loc["risk." + Risk + ".desc"];

    public IReadOnlyList<string> Roots { get; private set; }
    public string RootsText => Roots.Count == 0 ? (IsRecycleBin ? string.Empty : Loc["targets.notPresent"]) : string.Join("   ·   ", Roots);
    public bool HasRoots => Roots.Count > 0;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private long _bytes;
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private TargetState _state;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private double _share;

    public ObservableCollection<EntryVm> TopEntries { get; } = [];
    public TargetScanResult? LastResult { get; private set; }

    public bool IsScanning => State == TargetState.Scanning;
    public bool IsDenied => State == TargetState.Denied;
    public bool HasEntries => TopEntries.Count > 0;
    public bool HasBytes => Bytes > 0;

    public string StatusText => State switch
    {
        TargetState.Scanning => Loc["targets.scanning"],
        TargetState.Denied => Loc["targets.needsAdmin"],
        TargetState.Error => Loc.Format("targets.error", Error),
        TargetState.Done => Bytes == 0 ? Loc["targets.empty"] : string.Empty,
        _ => IsPresent ? Loc["targets.notScanned"] : Loc["targets.notPresent"],
    };

    partial void OnIsEnabledChanged(bool value) => _onSelectionChanged(this);

    partial void OnStateChanged(TargetState value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(IsDenied));
    }

    partial void OnBytesChanged(long value)
    {
        OnPropertyChanged(nameof(HasBytes));
        OnPropertyChanged(nameof(StatusText));
    }

    public void ResetForScan()
    {
        if (!IsPresent) return;
        State = TargetState.Scanning;
        Bytes = 0;
        FileCount = 0;
        Error = null;
        TopEntries.Clear();
        OnPropertyChanged(nameof(HasEntries));
    }

    public void Apply(TargetScanResult result)
    {
        LastResult = result;
        Roots = result.Target.Kind == TargetKind.Files ? result.Roots : Roots;
        Bytes = result.TotalBytes;
        FileCount = result.FileCount;
        Error = result.Error;

        State = result.AccessDenied && result.TotalBytes == 0 ? TargetState.Denied
            : result.Error is not null && result.TotalBytes == 0 ? TargetState.Error
            : TargetState.Done;

        TopEntries.Clear();
        foreach (var entry in result.TopEntries.Take(60))
            TopEntries.Add(new EntryVm(entry, result.TotalBytes));

        OnPropertyChanged(nameof(RootsText));
        OnPropertyChanged(nameof(HasRoots));
        OnPropertyChanged(nameof(HasEntries));
    }

    public void ApplyClean(TargetCleanResult clean)
    {
        Bytes = clean.BytesRemaining;
        FileCount = clean.Failed;
        TopEntries.Clear();
        OnPropertyChanged(nameof(HasEntries));
        if (clean.Error is not null)
        {
            Error = clean.Error;
            State = TargetState.Error;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(RiskText));
        OnPropertyChanged(nameof(RiskHint));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RootsText));
    }
}
