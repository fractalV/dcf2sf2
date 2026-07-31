using System.ComponentModel;
using System.Runtime.CompilerServices;
using DcfToSf2.Parsing;

namespace DcfToSf2.Ui;

internal sealed class IgnoreBlockNode : INotifyPropertyChanged
{
    private bool? _isChecked;
    private bool _updating;
    private bool _hasPatternMatch;

    public IgnoreBlockNode(
        string path,
        string title,
        List<IgnoreFieldNode> fields,
        List<IgnoreBlockNode>? childBlocks = null,
        string? sectionKey = null
    )
    {
        Path = path;
        Title = title;
        SectionKey = sectionKey;
        Fields = fields;
        ChildBlocks = childBlocks ?? [];
        foreach (var f in Fields)
            f.Parent = this;
        foreach (var c in ChildBlocks)
            c.ParentBlock = this;
        RefreshCheckState();
    }

    public string Path { get; }
    public string Title { get; }

    /// <summary>Non-null when this node is a B-block section (e.g. GTDID), not a DCF path segment.</summary>
    public string? SectionKey { get; }

    public bool IsSection => !string.IsNullOrEmpty(SectionKey);

    public IgnoreBlockNode? ParentBlock { get; set; }

    public string Header
    {
        get
        {
            if (IsSection)
            {
                string key = SectionKey!;
                return string.IsNullOrWhiteSpace(Title)
                    || Title.Equals(key, StringComparison.OrdinalIgnoreCase)
                    ? key
                    : $"{key} — {Title}";
            }

            string leaf = LeafName(Path);
            bool nested = ParentBlock is not null;
            string name = nested ? leaf : Path;
            return string.Equals(Title, leaf, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(Title)
                ? name
                : $"{name} — {Title}";
        }
    }

    public List<IgnoreFieldNode> Fields { get; }
    public List<IgnoreBlockNode> ChildBlocks { get; }

    /// <summary>Child path/section nodes first, then fields — for TreeView binding.</summary>
    public IEnumerable<object> Children => ChildBlocks.Cast<object>().Concat(Fields);

    public IEnumerable<IgnoreFieldNode> AllFields() =>
        Fields.Concat(ChildBlocks.SelectMany(c => c.AllFields()));

    public bool HasPatternMatch
    {
        get => _hasPatternMatch;
        private set
        {
            if (_hasPatternMatch == value)
                return;
            _hasPatternMatch = value;
            OnPropertyChanged();
        }
    }

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }

    public void RefreshCheckState()
    {
        if (_updating)
            return;

        _updating = true;
        try
        {
            foreach (var child in ChildBlocks)
                child.RefreshCheckState();

            var all = AllFields().ToList();
            int checkedCount = all.Count(f => f.IsChecked);
            IsChecked = all.Count == 0 ? false : checkedCount == 0 ? false : checkedCount == all.Count ? true : null;
            HasPatternMatch = all.Any(f => f.IsPatternMatch);
        }
        finally
        {
            _updating = false;
        }
    }

    public static string LeafName(string path)
    {
        int idx = path.LastIndexOf('\\');
        return idx >= 0 ? path[(idx + 1)..] : path;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class IgnoreFieldNode : INotifyPropertyChanged
{
    private bool _isChecked;
    private bool _isPatternMatch;

    public IgnoreFieldNode(string name, string label, bool isChecked)
    {
        Name = name;
        Label = label;
        _isChecked = isChecked;
    }

    public IgnoreBlockNode? Parent { get; set; }
    public string Name { get; }
    public string Label { get; }

    public string Display =>
        string.Equals(Label, Name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Label, DcfParser.GetLastToken(Name), StringComparison.OrdinalIgnoreCase)
            ? Name
            : $"{Name} — {Label}";

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }

    public bool IsPatternMatch
    {
        get => _isPatternMatch;
        set
        {
            if (_isPatternMatch == value)
                return;
            _isPatternMatch = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
