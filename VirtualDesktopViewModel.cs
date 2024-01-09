using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WindowsDesktop;

namespace DesktopIconHiderWPF;

public class VirtualDesktopViewModel : INotifyPropertyChanged
{
    private string _name;
    private Uri? _wallpaperPath;
    private string _showcaseMessage;
    private bool _isCurrent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string Name
    {
        get => this._name;
        set
        {
            if (this._name != value)
            {
                this._name = value;
                this.OnPropertyChanged();
            }
        }
    }

    public Uri? WallpaperPath
    {
        get => this._wallpaperPath;
        set
        {
            if (this._wallpaperPath != value)
            {
                this._wallpaperPath = value;
                this.OnPropertyChanged();
            }
        }
    }

    public string ShowcaseMessage
    {
        get => this._showcaseMessage;
        set
        {
            if (this._showcaseMessage != value)
            {
                this._showcaseMessage = value;
                this.OnPropertyChanged();
            }
        }
    }

    public bool IsCurrent
    {
        get => this._isCurrent;
        set
        {
            if (this._isCurrent != value)
            {
                this._isCurrent = value;
                this.OnPropertyChanged();
            }
        }
    }

    public VirtualDesktopViewModel(VirtualDesktop source)
    {
        this._name = string.IsNullOrEmpty(source.Name) ? "(no name)" : source.Name;
        this._wallpaperPath = Uri.TryCreate(source.WallpaperPath, UriKind.Absolute, out var uri) ? uri : null;
        this._showcaseMessage = "";
        this.Id = source.Id;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}