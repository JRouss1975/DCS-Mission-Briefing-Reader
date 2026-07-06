using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DCSMissionReader;

public class MissionFile : INotifyPropertyChanged
{
    private string _fileName = "";
    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    private string _theater = "";
    public string Theater
    {
        get => _theater;
        set { _theater = value; OnPropertyChanged(); }
    }

    private string _mainUnit = "";
    public string MainUnit
    {
        get => _mainUnit;
        set { _mainUnit = value; OnPropertyChanged(); }
    }

    public string FullPath { get; set; } = "";

    public DateTime FileDate { get; set; }

    public long FileSize { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); }
    }

    private double _score;
    public double Score
    {
        get => _score;
        set { _score = value; OnPropertyChanged(); }
    }

    private string _matchInfo = "";
    public string MatchInfo
    {
        get => _matchInfo;
        set { _matchInfo = value; OnPropertyChanged(); }
    }

    public override string ToString() => FileName;
}

public class PlayableUnitCount
{
    public string Type { get; set; } = "";
    public int Count { get; set; }
}
