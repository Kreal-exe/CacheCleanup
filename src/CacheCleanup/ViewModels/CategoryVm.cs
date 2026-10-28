using System.Windows;
using System.Windows.Media;
using CacheCleanup.Core;
using CacheCleanup.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CacheCleanup.ViewModels;

public sealed partial class CategoryVm : ObservableObject
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    private readonly Action<CategoryVm, bool> _setAll;
    private bool _suppress;

    public CategoryVm(TargetCategory category, IReadOnlyList<TargetVm> targets, Action<CategoryVm, bool> setAll)
    {
        Category = category;
        Targets = targets;
        _setAll = setAll;
        Loc.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Name));
    }

    public TargetCategory Category { get; }
    public IReadOnlyList<TargetVm> Targets { get; }
    public string Name => Loc["cat." + Category];

    public string Glyph => Category switch
    {
        TargetCategory.System => "",
        TargetCategory.Browsers => "",
        TargetCategory.Developer => "",
        TargetCategory.Apps => "",
        TargetCategory.Gaming => "",
        _ => "",
    };

    public Brush Brush => Application.Current?.TryFindResource("Brush.Cat." + Category) as Brush ?? Brushes.Gray;

    [ObservableProperty] private long _bytes;
    [ObservableProperty] private long _selectedBytes;
    [ObservableProperty] private long _fileCount;
    [ObservableProperty] private int _targetCount;
    [ObservableProperty] private int _presentCount;
    [ObservableProperty] private double _share;
    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppress) return;
        _setAll(this, value);
    }

    public void Recalc(long grandTotal)
    {
        var present = Targets.Where(t => t.IsPresent).ToList();
        Bytes = present.Sum(t => t.Bytes);
        SelectedBytes = present.Where(t => t.IsEnabled).Sum(t => t.Bytes);
        FileCount = present.Sum(t => (long)t.FileCount);
        TargetCount = Targets.Count;
        PresentCount = present.Count;
        Share = grandTotal > 0 ? (double)Bytes / grandTotal : 0;

        _suppress = true;
        IsEnabled = present.Any(t => t.IsEnabled);
        _suppress = false;
    }
}
