using System.ComponentModel;
using System.Runtime.CompilerServices;
using DcfToSf2.Parsing;

namespace DcfToSf2.Ui;

internal sealed class IgnoreBlockNode : INotifyPropertyChanged
{
    private bool? _isChecked;
    private bool _updating;
    private bool _hasPatternMatch;

    public IgnoreBlockNode(string path, string title, List<IgnoreFieldNode> fields)
    {
        Path = path;
        Title = title;
        Fields = fields;
        foreach (var f in fields)
            f.Parent = this;
        RefreshCheckState();
    }

    public string Path { get; }
    public string Title { get; }
    public string Header =>
        string.Equals(Title, LeafName(Path), StringComparison.OrdinalIgnoreCase)
            ? Path
            : $"{Path} — {Title}";

    public List<IgnoreFieldNode> Fields { get; }

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
            int checkedCount = Fields.Count(f => f.IsChecked);
            IsChecked = checkedCount == 0 ? false : checkedCount == Fields.Count ? true : null;
            HasPatternMatch = Fields.Any(f => f.IsPatternMatch);
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
